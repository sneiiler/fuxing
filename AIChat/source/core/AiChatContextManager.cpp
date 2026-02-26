// -----------------------------------------------------------------------------
// File: AiChatContextManager.cpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-12
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#include "AiChatContextManager.hpp"

#include <QJsonDocument>
#include <QMap>
#include <QRegularExpression>
#include <QSet>

#include <algorithm>

namespace AiChat
{

// ============================================================================
// Construction
// ============================================================================

ContextManager::ContextManager(QObject* aParent)
   : QObject(aParent)
{
}

// ============================================================================
// Configuration
// ============================================================================

void ContextManager::SetContextWindow(int aContextWindow)
{
   mConfig.contextWindow = aContextWindow;

   // Compute safety-buffered max-allowed following the tiered model:
   //   <=  64K  → window − 27K
   //   <= 128K  → window − 30K
   //   <= 200K  → window − 40K
   //   else     → max(window − 40K, window × 0.8)
   if (aContextWindow <= 64000)
   {
      mConfig.maxAllowedTokens = aContextWindow - 27000;
   }
   else if (aContextWindow <= 128000)
   {
      mConfig.maxAllowedTokens = aContextWindow - 30000;
   }
   else if (aContextWindow <= 200000)
   {
      mConfig.maxAllowedTokens = aContextWindow - 40000;
   }
   else
   {
      mConfig.maxAllowedTokens =
         qMax(aContextWindow - 40000,
              static_cast<int>(aContextWindow * 0.8));
   }

   // Clamp to at least 8K usable
   if (mConfig.maxAllowedTokens < 8000)
   {
      mConfig.maxAllowedTokens = 8000;
   }
}

void ContextManager::SetAutoCondenseEnabled(bool aEnabled)
{
   mAutoCondenseEnabled = aEnabled;
}

void ContextManager::SetAutoCondenseThreshold(double aThreshold)
{
   mAutoCondenseThreshold = qBound(0.1, aThreshold, 1.0);
}

// ============================================================================
// Token awareness
// ============================================================================

void ContextManager::RecordTokenUsage(const TokenUsage& aUsage)
{
   mLastUsage = aUsage;

   // Display ratio uses contextWindow (user's preference setting)
   const double displayRatio = (mConfig.contextWindow > 0)
      ? static_cast<double>(mLastUsage.totalTokens) / mConfig.contextWindow
      : 0.0;
   emit UsageUpdated(displayRatio, mLastUsage.totalTokens, mConfig.contextWindow);
}

double ContextManager::GetUsageRatio() const
{
   if (mConfig.maxAllowedTokens <= 0)
   {
      return 0.0;
   }
   return static_cast<double>(mLastUsage.totalTokens) /
          static_cast<double>(mConfig.maxAllowedTokens);
}

bool ContextManager::NeedsCompaction(const QList<ChatMessage>& aHistory) const
{
   double ratio = GetUsageRatio();
   
   // If the API hasn't reported usage (e.g. streaming without usage chunk),
   // or if the usage is 0, fall back to local estimation.
   if (ratio <= 0.0 && !aHistory.isEmpty())
   {
      const int estimatedTokens = EstimateTokenCount(aHistory);
      if (mConfig.maxAllowedTokens > 0)
      {
         ratio = static_cast<double>(estimatedTokens) / static_cast<double>(mConfig.maxAllowedTokens);
      }
   }
   
   return ratio >= mAutoCondenseThreshold;
}

// ============================================================================
// PrepareMessages  —  core entry point
// ============================================================================

QList<ChatMessage> ContextManager::PrepareMessages(const QList<ChatMessage>& aHistory)
{
   mLastStats = {};
   mLastStats.originalMessageCount = aHistory.size();

   // Trivial cases: nothing to optimise
   if (aHistory.size() <= 2 || !NeedsCompaction(aHistory))
   {
      mLastStats.resultMessageCount = aHistory.size();
      return aHistory;
   }

   // --- Phase 4: LLM auto-condense path ---
   // If auto-condense is enabled and we haven't already triggered it this cycle,
   // emit AutoCondenseRequested and return history as-is.
   // The Task layer will handle the actual LLM summarization asynchronously.
   if (mAutoCondenseEnabled && !mCondenseRequested)
   {
      mCondenseRequested = true;
      mLastStats.didAutoCondense    = true;
      mLastStats.resultMessageCount = aHistory.size();
      emit AutoCondenseRequested();
      return aHistory;
   }

   // --- Step 1: tool-result deduplication (read_file + load_skill) ---
   int deduplicatedCount = 0;
   QList<ChatMessage> result = DeduplicateToolResults(aHistory, deduplicatedCount);
   mLastStats.deduplicatedFileReads = deduplicatedCount;

   if (deduplicatedCount > 0)
   {
      mLastStats.didDeduplicate = true;

      // Evaluate dedup benefit
      const int originalChars    = EstimateCharCount(aHistory);
      const int deduplicatedChars = EstimateCharCount(result);
      const double savedRatio = (originalChars > 0)
         ? (1.0 - static_cast<double>(deduplicatedChars) / static_cast<double>(originalChars))
         : 0.0;

      if (savedRatio >= 0.3)
      {
         // Dedup alone saved >= 30 % — skip truncation
         mLastStats.resultMessageCount = result.size();
         return result;
      }
   }

   // --- Step 2: programmatic truncation ---
   int truncatedCount = 0;
   
   // Calculate the dynamic size of the system prompt (including loaded skills)
   // so we can accurately determine how much space is left for the message history.
   int systemPromptTokens = 0;
   if (!aHistory.isEmpty() && aHistory.first().mRole == MessageRole::System)
   {
      systemPromptTokens = EstimateTokenCount({aHistory.first()});
   }
   
   result = TruncateHistory(result, truncatedCount, DetermineKeepMode(systemPromptTokens));
   mLastStats.truncatedMessages = truncatedCount;
   mLastStats.didTruncate       = (truncatedCount > 0);

   if (truncatedCount > 0)
   {
      // Step 3: insert notice
      InsertTruncationNotice(result);

      // Step 4: repair tool pairing
      RemoveOrphanedToolResults(result);
      RepairToolCallPairing(result);

      emit ContextTruncated(truncatedCount, result.size());
   }

   mLastStats.resultMessageCount = result.size();
   return result;
}

// ============================================================================
// ForceTruncate  —  aggressive recovery after API overflow
// ============================================================================

QList<ChatMessage> ContextManager::ForceTruncate(const QList<ChatMessage>& aHistory)
{
   if (aHistory.size() <= 2)
   {
      return aHistory;
   }

   int truncatedCount = 0;
   QList<ChatMessage> result = TruncateHistory(aHistory, truncatedCount, KeepMode::Quarter);

   if (truncatedCount > 0)
   {
      InsertTruncationNotice(result);
      RemoveOrphanedToolResults(result);
      RepairToolCallPairing(result);
      emit ContextTruncated(truncatedCount, result.size());
   }
   return result;
}

// ============================================================================
// IsContextWindowError  —  detect overflow errors
// ============================================================================

bool ContextManager::IsContextWindowError(const QString& aError)
{
   const QString lower = aError.toLower();

   // OpenAI patterns
   if (lower.contains(QStringLiteral("maximum context length")))   return true;
   if (lower.contains(QStringLiteral("context_length_exceeded")))  return true;

   // Anthropic patterns
   if (lower.contains(QStringLiteral("prompt is too long")))       return true;
   if (lower.contains(QStringLiteral("input too long")))           return true;

   // Generic pattern: "context" + "length/limit/window/exceeded"
   if (lower.contains(QStringLiteral("context")))
   {
      static const QRegularExpression contextKeywords(
         QStringLiteral("length|limit|window|exceeded|overflow"),
         QRegularExpression::CaseInsensitiveOption);
      if (contextKeywords.match(lower).hasMatch())
      {
         return true;
      }
   }

   // Token limit patterns
   if (lower.contains(QStringLiteral("token")) &&
       (lower.contains(QStringLiteral("limit")) || lower.contains(QStringLiteral("exceeded"))))
   {
      return true;
   }

   return false;
}

// ============================================================================
// DeduplicateToolResults  (covers read_file and load_skill)
// ============================================================================

QList<ChatMessage> ContextManager::DeduplicateToolResults(
   const QList<ChatMessage>& aMessages, int& aDeduplicatedCount)
{
   aDeduplicatedCount = 0;

   // Tools whose results should be deduplicated.  For each, the "identity key"
   // is extracted differently:
   //   read_file       → file path from the call arguments ("path")
   //   load_skill      → skill name from the call arguments ("skill_name")
   //   write_to_file   → file path from the call arguments ("path")
   //   replace_in_file → file path from the call arguments ("path")
   static const QSet<QString> deduplicableTools = {
      QStringLiteral("read_file"),
      QStringLiteral("load_skill"),
      QStringLiteral("write_to_file"),
      QStringLiteral("replace_in_file")
   };

   // Pass 1: Build { toolCallId → toolName } from assistant messages,
   //         and track { identityKey → lastToolCallId } for deduplicable tools.

   // Build a quick lookup: toolCallId → functionName
   QMap<QString, QString> toolCallIdToName;
   for (const auto& msg : aMessages)
   {
      if (msg.mRole == MessageRole::Assistant)
      {
         for (const auto& tc : msg.mToolCalls)
         {
            toolCallIdToName.insert(tc.id, tc.functionName);
         }
      }
   }

   // Identify deduplicable tool results and remember the last occurrence per key.
   // Identity key extraction:
   //   read_file  → arguments["path"]
   //   load_skill → arguments["skill_name"]
   struct ReadInfo
   {
      int     lastIndex{-1};
      QString identityKey;
   };

   // toolCallId → identityKey  (extracted from the assistant's tool_call arguments)
   QMap<QString, QString> toolCallIdToKey;
   for (const auto& msg : aMessages)
   {
      if (msg.mRole == MessageRole::Assistant)
      {
         for (const auto& tc : msg.mToolCalls)
         {
            if (!deduplicableTools.contains(tc.functionName)) {
               continue;
            }

            QString key;
            if (tc.functionName == QStringLiteral("read_file")
                || tc.functionName == QStringLiteral("write_to_file")
                || tc.functionName == QStringLiteral("replace_in_file")) {
               key = tc.arguments.value(QStringLiteral("path")).toString();
            } else if (tc.functionName == QStringLiteral("load_skill")) {
               key = QStringLiteral("skill:") +
                     tc.arguments.value(QStringLiteral("skill_name")).toString();
            }

            if (!key.isEmpty()) {
               toolCallIdToKey.insert(tc.id, key);
            }
         }
      }
   }

   // identityKey → last index in the message array
   QMap<QString, int> keyLastIndex;
   for (int i = 0; i < aMessages.size(); ++i)
   {
      const auto& msg = aMessages[i];
      if (msg.mRole != MessageRole::Tool)
      {
         continue;
      }

      // Determine tool name
      QString toolName = msg.mToolName;
      if (toolName.isEmpty())
      {
         toolName = toolCallIdToName.value(msg.mToolCallId);
      }
      if (!deduplicableTools.contains(toolName))
      {
         continue;
      }

      // Determine identity key
      QString identityKey = toolCallIdToKey.value(msg.mToolCallId);
      if (identityKey.isEmpty())
      {
         continue;
      }

      keyLastIndex[identityKey] = i;
   }

   // Pass 2: Rebuild message list; replace non-last results with a stub.
   QList<ChatMessage> result;
   result.reserve(aMessages.size());

   for (int i = 0; i < aMessages.size(); ++i)
   {
      const auto& msg = aMessages[i];

      if (msg.mRole == MessageRole::Tool)
      {
         QString toolName = msg.mToolName;
         if (toolName.isEmpty())
         {
            toolName = toolCallIdToName.value(msg.mToolCallId);
         }

         if (deduplicableTools.contains(toolName))
         {
            const QString identityKey = toolCallIdToKey.value(msg.mToolCallId);
            if (!identityKey.isEmpty() && keyLastIndex.value(identityKey, -1) != i)
            {
               // This is a duplicate — replace content with a note
               ChatMessage deduped = msg;
               deduped.mContent = QStringLiteral(
                  "[NOTE] Duplicate %1 result removed to save context space. "
                  "A more recent result for '%2' exists later in the conversation.")
                  .arg(toolName, identityKey);
               result.append(deduped);
               ++aDeduplicatedCount;
               continue;
            }
         }
      }

      result.append(msg);
   }

   return result;
}

// ============================================================================
// TruncateHistory
// ============================================================================

QList<ChatMessage> ContextManager::TruncateHistory(
   const QList<ChatMessage>& aMessages, int& aTruncatedCount, KeepMode aForceMode)
{
   aTruncatedCount = 0;
   if (aMessages.size() <= 2)
   {
      return aMessages;
   }

   const KeepMode mode = aForceMode;
   const int startIndex = 2;     // always keep [0] user and [1] assistant
   const int totalRemaining = aMessages.size() - startIndex;

   // Protect the most recent tool-call / tool-result pairs from truncation.
   // Count backwards from the end to find the minimum number of messages
   // that must be preserved to keep the last few tool exchanges intact.
   const int kMinProtectedToolPairs = 3;  // protect at most 3 recent tool rounds
   int protectedTailCount = 0;
   {
      int pairsFound = 0;
      for (int i = aMessages.size() - 1; i >= startIndex && pairsFound < kMinProtectedToolPairs; --i)
      {
         ++protectedTailCount;
         const auto& msg = aMessages[i];
         // An assistant message with tool_calls marks the start of a tool round
         if (msg.mRole == MessageRole::Assistant && !msg.mToolCalls.isEmpty())
         {
            ++pairsFound;
         }
      }
   }
   // Ensure we never remove into the protected tail
   const int maxRemovable = totalRemaining - protectedTailCount;

   int removeCount;
   if (mode == KeepMode::Quarter)
   {
      // Remove ¾, keep ¼ — make it even
      removeCount = (totalRemaining * 3 / 4 / 2) * 2;
   }
   else
   {
      // Remove ½ — make it even
      removeCount = (totalRemaining / 2 / 2) * 2;
   }

   // Clamp to maxRemovable so the protected tail is always kept
   if (removeCount > maxRemovable)
   {
      removeCount = (maxRemovable / 2) * 2;  // keep even
   }

   // Clamp
   if (removeCount <= 0)
   {
      return aMessages;
   }
   if (removeCount > totalRemaining - 1)
   {
      removeCount = ((totalRemaining - 1) / 2) * 2;
   }

   // Boundary adjustment: if the truncation boundary falls inside a
   // tool-related cluster (assistant-with-tool_calls followed by tool results),
   // shift forward to keep the cluster intact.
   int cutEnd = startIndex + removeCount;          // exclusive end of removed range
   while (cutEnd < aMessages.size())
   {
      const auto& msg = aMessages[cutEnd];
      if (msg.mRole == MessageRole::Tool)
      {
         // This is a tool result that would be orphaned; skip it into the kept area
         ++cutEnd;
         ++removeCount;
      }
      else
      {
         break;
      }
   }

   QList<ChatMessage> result;
   result.reserve(aMessages.size() - removeCount);

   // Keep the first two (system context)
   // Note: If the system prompt was injected by Task, it's at index 0.
   // We want to keep the system prompt and the first user message.
   result.append(aMessages[0]);
   if (aMessages.size() > 1) {
      result.append(aMessages[1]);
   }

   // Keep everything after the removed range
   for (int i = cutEnd; i < aMessages.size(); ++i)
   {
      result.append(aMessages[i]);
   }

   aTruncatedCount = removeCount;
   return result;
}

ContextManager::KeepMode ContextManager::DetermineKeepMode(int aSystemPromptTokens) const
{
   // If usage is more than double the allowed window → aggressive trim
   // We also account for the system prompt size, which can be large if many skills are loaded.
   const int effectiveUsage = qMax(mLastUsage.totalTokens, aSystemPromptTokens);
   if (effectiveUsage > mConfig.maxAllowedTokens * 2)
   {
      return KeepMode::Quarter;
   }
   return KeepMode::Half;
}

// ============================================================================
// InsertTruncationNotice
// ============================================================================

void ContextManager::InsertTruncationNotice(QList<ChatMessage>& aMessages)
{
   if (aMessages.size() < 2)
   {
      return;
   }

   // The first user message after the preserved first pair becomes a
   // continuation prompt so the LLM knows the conversation was truncated.
   // We look for the first user message after index 1.
   for (int i = 2; i < aMessages.size(); ++i)
   {
      if (aMessages[i].mRole == MessageRole::User)
      {
         // Prepend a notice to this user message
         aMessages[i].mContent =
            QStringLiteral("[NOTE] Some previous conversation history has been "
                           "removed to manage context length. The first exchange "
                           "and the recent conversation remain intact. "
                           "Please continue assisting the user without "
                           "re-introducing yourself.\n\n")
            + aMessages[i].mContent;
         return;
      }
   }

   // If there's no user message after the preserved pair, insert one.
   ChatMessage notice;
   notice.mRole    = MessageRole::User;
   notice.mContent = QStringLiteral(
      "[Previous conversation history has been truncated. "
      "Continue assisting the user with the remaining context.]");
   aMessages.insert(2, notice);
}

// ============================================================================
// Structure repair
// ============================================================================

void ContextManager::RemoveOrphanedToolResults(QList<ChatMessage>& aMessages)
{
   // Build a set of all tool_call IDs that exist in assistant messages
   QSet<QString> validToolCallIds;
   for (const auto& msg : aMessages)
   {
      if (msg.mRole == MessageRole::Assistant)
      {
         for (const auto& tc : msg.mToolCalls)
         {
            validToolCallIds.insert(tc.id);
         }
      }
   }

   // Remove Tool messages whose tool_call_id has no matching assistant
   for (int i = aMessages.size() - 1; i >= 0; --i)
   {
      if (aMessages[i].mRole == MessageRole::Tool &&
          !validToolCallIds.contains(aMessages[i].mToolCallId))
      {
         aMessages.removeAt(i);
      }
   }
}

void ContextManager::RepairToolCallPairing(QList<ChatMessage>& aMessages)
{
   // Build a set of existing tool result IDs
   QSet<QString> existingToolResultIds;
   for (const auto& msg : aMessages)
   {
      if (msg.mRole == MessageRole::Tool && !msg.mToolCallId.isEmpty())
      {
         existingToolResultIds.insert(msg.mToolCallId);
      }
   }

   // For each assistant message that has tool_calls, verify every
   // tool_call has a corresponding tool result.  If not, insert a
   // stub result immediately after the assistant message.
   for (int i = 0; i < aMessages.size(); ++i)
   {
      if (aMessages[i].mRole != MessageRole::Assistant)
      {
         continue;
      }
      const auto& toolCalls = aMessages[i].mToolCalls;
      if (toolCalls.isEmpty())
      {
         continue;
      }

      int insertPos = i + 1;
      for (const auto& tc : toolCalls)
      {
         if (!existingToolResultIds.contains(tc.id))
         {
            ChatMessage stub;
            stub.mRole       = MessageRole::Tool;
            stub.mToolCallId = tc.id;
            stub.mToolName   = tc.functionName;

            // For load_skill results, preserve a summary so the model retains
            // domain knowledge after truncation instead of losing it entirely.
            if (tc.functionName == QStringLiteral("load_skill"))
            {
               // Extract skill name from arguments for the stub
               QString skillName;
               if (tc.arguments.contains(QStringLiteral("skill_name"))) {
                  skillName = tc.arguments.value(QStringLiteral("skill_name")).toString();
               }
               stub.mContent = QStringLiteral(
                  "[Skill '%1' was loaded earlier but its full content was removed "
                  "during context truncation. The skill remains available in the "
                  "system prompt skill catalog. Re-load it with load_skill(\"%1\") "
                  "if you need the detailed reference.]").arg(
                     skillName.isEmpty() ? QStringLiteral("unknown") : skillName);
            }
            else
            {
               stub.mContent = QStringLiteral("[Result removed during context truncation]");
            }

            aMessages.insert(insertPos, stub);
            existingToolResultIds.insert(tc.id);
            ++insertPos;
         }
         else
         {
            ++insertPos;
         }
      }
   }
}

// ============================================================================
// EstimateCharCount
// ============================================================================

int ContextManager::EstimateCharCount(const QList<ChatMessage>& aMessages)
{
   int total = 0;
   for (const auto& msg : aMessages)
   {
      total += msg.mContent.size();
      for (const auto& tc : msg.mToolCalls)
      {
         total += tc.functionName.size();
         total += QJsonDocument(tc.arguments).toJson(QJsonDocument::Compact).size();
      }
   }
   return total;
}

// ============================================================================
// EstimateTokenCount  —  local fallback when API doesn't report usage
// ============================================================================

int ContextManager::EstimateTokenCount(const QList<ChatMessage>& aMessages)
{
   const int charCount = EstimateCharCount(aMessages);
   // Heuristic: ~3.5 chars per token for mixed content (English + code).
   // CJK text tends to be ~2 chars/token, but tool results and code are mostly ASCII.
   // Using 3.2 as a slightly conservative blended estimate.
   return qMax(1, charCount * 10 / 32);
}

// ============================================================================
// RecordLocalEstimation  —  called when API doesn't return usage info
// ============================================================================

void ContextManager::RecordLocalEstimation(const QList<ChatMessage>& aMessages,
                                           int aCompletionChars)
{
   const int promptTokens = EstimateTokenCount(aMessages);
   const int completionTokens = qMax(1, aCompletionChars * 10 / 32);

   TokenUsage usage;
   usage.promptTokens     = promptTokens;
   usage.completionTokens = completionTokens;
   usage.totalTokens      = promptTokens + completionTokens;

   RecordTokenUsage(usage);
}

} // namespace AiChat
