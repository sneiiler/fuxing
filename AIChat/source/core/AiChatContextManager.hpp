// -----------------------------------------------------------------------------
// File: AiChatContextManager.hpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-12
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#ifndef AICHAT_CONTEXT_MANAGER_HPP
#define AICHAT_CONTEXT_MANAGER_HPP

#include <QList>
#include <QObject>
#include <QString>

#include "AiChatClient.hpp"

namespace AiChat
{

/// Token usage reported by the API
struct TokenUsage
{
   int promptTokens{0};      ///< Input token count
   int completionTokens{0};  ///< Output token count
   int totalTokens{0};       ///< Total tokens used
};

/// Context window configuration (per model tier)
struct ContextWindowConfig
{
   int contextWindow{128000};   ///< Model's nominal context window size
   int maxAllowedTokens{98000}; ///< Usable ceiling (minus safety buffer)
};

/// Context manager — modeled after Cline's ContextManager.
///
/// Responsibilities:
///   1. Track API-reported token usage
///   2. Decide when context compaction is needed
///   3. Deduplicate repeated file reads
///   4. Perform programmatic sliding-window truncation
///   5. Repair orphaned tool_use/tool_result pairs after truncation
///   6. (Future) Trigger LLM auto-condensation
class ContextManager : public QObject
{
   Q_OBJECT
public:
   explicit ContextManager(QObject* aParent = nullptr);
   ~ContextManager() override = default;

   // ---- Configuration ----

   /// Set the model's context window size.
   /// Automatically computes @c maxAllowedTokens based on the window tier:
   ///   64K  → 37K,  128K → 98K,  200K → 160K,
   ///   else → max(window − 40K, window × 0.8)
   void SetContextWindow(int aContextWindow);

   /// Enable / disable LLM auto-condensation (Phase 4 — future)
   void SetAutoCondenseEnabled(bool aEnabled);

   /// Set the usage ratio threshold that triggers auto-condense (default 0.8)
   void SetAutoCondenseThreshold(double aThreshold);

   /// Reset the condense-requested flag (call after condensation completes or fails)
   void ResetCondenseRequested() { mCondenseRequested = false; }

   // ---- Token awareness ----

   /// Record the token usage returned by the latest API response
   void RecordTokenUsage(const TokenUsage& aUsage);

   /// Retrieve the most recent token usage snapshot
   const TokenUsage& GetLastTokenUsage() const noexcept { return mLastUsage; }

   /// Context window utilization ratio [0.0, 1.0+]
   double GetUsageRatio() const;

   // ---- Core entry point (called before every SendToLLM) ----

   /// Return an optimised / truncated copy of @a aHistory suitable for sending.
   /// The original list is never modified.
   ///
   /// Processing order:
   ///   1. Deduplicate file reads (cheapest, runs first)
   ///   2. If token usage < threshold → return as-is
   ///   3. If dedup savings >= 30 % → skip truncation
   ///   4. Otherwise programmatic truncation (half or three-quarter)
   ///   5. Repair tool_use/tool_result pairing
   QList<ChatMessage> PrepareMessages(const QList<ChatMessage>& aHistory);

   /// Whether compaction is needed based on the last recorded token usage
   /// or local estimation if usage is missing.
   bool NeedsCompaction(const QList<ChatMessage>& aHistory = {}) const;

   // ---- Accessors ----

   const ContextWindowConfig& GetConfig() const noexcept { return mConfig; }

   /// Statistics from the most recent PrepareMessages() call (for UI / debug)
   struct ProcessingStats
   {
      int  originalMessageCount{0};
      int  resultMessageCount{0};
      int  deduplicatedFileReads{0};
      int  truncatedMessages{0};
      bool didTruncate{false};
      bool didDeduplicate{false};
      bool didAutoCondense{false};   ///< Phase 4 — reserved
   };
   const ProcessingStats& GetLastStats() const noexcept { return mLastStats; }

   // ---- Context-overflow recovery (Phase 3) ----

   /// Test whether an API error string indicates a context-window overflow.
   static bool IsContextWindowError(const QString& aError);

   /// Force an aggressive truncation (keep ¼) — used after an API overflow error.
   /// Returns the resulting message list (same contract as PrepareMessages).
   QList<ChatMessage> ForceTruncate(const QList<ChatMessage>& aHistory);

signals:
   /// Context was truncated
   void ContextTruncated(int aRemovedCount, int aRemainingCount);

   /// Usage ratio updated (for UI progress display)
   void UsageUpdated(double aRatio, int aUsedTokens, int aMaxTokens);

   /// Auto-condense requested (Phase 4 — reserved)
   void AutoCondenseRequested();

private:
   // ---- Deduplication ----
   QList<ChatMessage> DeduplicateToolResults(const QList<ChatMessage>& aMessages,
                                              int& aDeduplicatedCount);

   // ---- Truncation ----
   enum class KeepMode { Half, Quarter };
   QList<ChatMessage> TruncateHistory(const QList<ChatMessage>& aMessages,
                                      int& aTruncatedCount,
                                      KeepMode aForceMode = KeepMode::Half);
   KeepMode DetermineKeepMode(int aSystemPromptTokens = 0) const;
   void InsertTruncationNotice(QList<ChatMessage>& aMessages);

   // ---- Structure repair ----
   void RepairToolCallPairing(QList<ChatMessage>& aMessages);
   void RemoveOrphanedToolResults(QList<ChatMessage>& aMessages);

   // ---- Estimation helpers ----
   static int EstimateCharCount(const QList<ChatMessage>& aMessages);

public:
   /// Estimate token count from a message list using character-based heuristic.
   /// Uses ~3.5 chars/token as a blended average.
   /// This is a fallback when the API doesn't return usage statistics.
   static int EstimateTokenCount(const QList<ChatMessage>& aMessages);

   /// Record a local estimation as if it were API usage.
   /// Call this when the API does not return token usage info.
   void RecordLocalEstimation(const QList<ChatMessage>& aMessages, int aCompletionChars);

private:
   ContextWindowConfig mConfig;
   TokenUsage          mLastUsage;
   ProcessingStats     mLastStats;

   bool   mAutoCondenseEnabled{false};
   double mAutoCondenseThreshold{0.8};
   bool   mCondenseRequested{false};  ///< Prevents re-triggering condense while in progress
};

} // namespace AiChat

#endif // AICHAT_CONTEXT_MANAGER_HPP
