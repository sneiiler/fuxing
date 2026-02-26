// -----------------------------------------------------------------------------
// File: AiChatTask.cpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#include "AiChatTask.hpp"
#include "AiChatContextManager.hpp"
#include "AiChatPromptEngine.hpp"
#include "AiChatSkillManager.hpp"
#include "tools/AiChatToolExecutor.hpp"
#include "tools/AiChatToolRegistry.hpp"

#include <QJsonArray>
#include <QJsonDocument>
#include <QRegularExpression>

namespace AiChat
{

namespace
{
constexpr int kMaxToolResultChars = 60000;

QString TruncateToolResult(const QString& aContent)
{
   if (aContent.size() <= kMaxToolResultChars)
   {
      return aContent;
   }

   const int extra = aContent.size() - kMaxToolResultChars;
   QString truncated = aContent.left(kMaxToolResultChars);
   truncated += QStringLiteral("\n[Truncated %1 chars to fit input limits]").arg(extra);
   return truncated;
}
} // namespace

Task::Task(Client*         aClient,
           ToolExecutor*   aToolExecutor,
           ToolRegistry*   aToolRegistry,
           PromptEngine*   aPromptEngine,
           ContextManager* aContextManager,
           SkillManager*   aSkillManager,
           QObject*        aParent)
   : QObject(aParent)
   , mClientPtr(aClient)
   , mToolExecutorPtr(aToolExecutor)
   , mToolRegistryPtr(aToolRegistry)
   , mPromptEnginePtr(aPromptEngine)
   , mContextManagerPtr(aContextManager)
   , mSkillManagerPtr(aSkillManager)
{
   // Wire up Client signals
   connect(mClientPtr, &Client::ResponseChunkReceived, this, &Task::OnResponseChunk);
   connect(mClientPtr, &Client::ResponseCompleted, this, &Task::OnResponseCompleted);
   connect(mClientPtr, &Client::ResponseReceived, this, &Task::OnResponseReceived);
   connect(mClientPtr, &Client::ToolCallsReceived, this, &Task::OnToolCallsReceived);
   connect(mClientPtr, &Client::ErrorOccurred, this, &Task::OnError);
   connect(mClientPtr, &Client::RequestStarted, this, &Task::OnRequestStarted);
   connect(mClientPtr, &Client::RequestFinished, this, &Task::OnRequestFinished);

   // Wire up ToolExecutor signals
   connect(mToolExecutorPtr, &ToolExecutor::ToolFinished, this, &Task::OnToolExecutorFinished);
   connect(mToolExecutorPtr, &ToolExecutor::ApprovalRequired, this, &Task::OnApprovalRequired);
}

void Task::Start(const QString& aUserMessage)
{
   if (mState == State::WaitingForLLM || mState == State::ExecutingTools || mState == State::WaitingForApproval)
   {
      emit TaskError("A task is already in progress.");
      return;
   }

   if (aUserMessage.trimmed().isEmpty())
   {
      emit TaskError("User message is empty.");
      return;
   }

   // Append user message to history
   ChatMessage userMsg;
   userMsg.mRole    = MessageRole::User;
   userMsg.mContent = aUserMessage;
   mHistory.append(userMsg);

   mLoopCount = 0;
   mContextRetryCount = 0;
   mPendingAssistantText.clear();

   emit DebugEvent(QStringLiteral("Task start: userMessage len=%1").arg(aUserMessage.size()));

   SendToLLM();
}

void Task::Abort()
{
   mClientPtr->CancelRequest();
   if (mToolExecutorPtr) {
      mToolExecutorPtr->ClearPendingApprovals();
   }
   mCurrentToolCalls.clear();
   mToolCallStates.clear();
   mPendingAssistantText.clear();

   // Clean up condense state if aborting during auto-condense
   if (mIsCondensing)
   {
      mIsCondensing = false;
      mCondensePendingText.clear();
      if (!mPreCondenseHistory.isEmpty())
      {
         mHistory = mPreCondenseHistory;
         mPreCondenseHistory.clear();
      }
   }

   emit DebugEvent(QStringLiteral("Task aborted"));
   SetState(State::Idle);
}

void Task::ClearHistory()
{
   mHistory.clear();
   mLoopCount = 0;
   mIsCondensing = false;
   mCondensePendingText.clear();
   mPreCondenseHistory.clear();

   // Reset activated skills — new session should start fresh
   if (mSkillManagerPtr) {
      mSkillManagerPtr->ClearActivatedSkills();
   }
   if (mPromptEnginePtr) {
      mPromptEnginePtr->SetActiveSkillContent({});
   }

   SetState(State::Idle);
}

void Task::SetHistory(const QList<ChatMessage>& aHistory)
{
   mHistory = aHistory;
   mLoopCount = 0;
   SetState(State::Idle);
}

void Task::HandleApproval(const QString& aToolCallId, bool aApproved, const QString& aFeedback)
{
   mToolExecutorPtr->ResolveApproval(aToolCallId, aApproved, aFeedback);
}

// ---- Auto-condense (Phase 4) ----

void Task::CondenseHistory()
{
   if (mIsCondensing)
   {
      emit DebugEvent(QStringLiteral("CondenseHistory called but already condensing, ignoring"));
      return;
   }

   if (mHistory.size() <= 4)
   {
      emit DebugEvent(QStringLiteral("CondenseHistory: history too short (%1 msgs), skipping")
                        .arg(mHistory.size()));
      return;
   }

   emit DebugEvent(QStringLiteral("CondenseHistory: starting auto-condense, history=%1 msgs")
                     .arg(mHistory.size()));

   // Save original history for rollback on failure
   mPreCondenseHistory = mHistory;
   mCondensePendingText.clear();
   mIsCondensing = true;

   // Build the summarization system prompt
   QString summarizePrompt = mPromptEnginePtr->BuildSummarizePrompt();

   // Prepare the history to summarize: use ForceTruncate to ensure it fits
   // within the context window for the summary request itself
   QList<ChatMessage> historyToSummarize = mHistory;
   if (mContextManagerPtr)
   {
      historyToSummarize = mContextManagerPtr->ForceTruncate(historyToSummarize);
   }

   emit DebugEvent(QStringLiteral("CondenseHistory: sending %1 msgs to LLM for summarization")
                     .arg(historyToSummarize.size()));

   // Send summarization request — no tools, just text completion
   SetState(State::WaitingForLLM);
   mClientPtr->SendChatRequest(historyToSummarize, summarizePrompt, QJsonArray{});
}

// ---- XML tool call fallback parser ----

QList<ToolCall> Task::ParseXmlToolCalls(QString& aText) const
{
   QList<ToolCall> toolCalls;

   // List of known tool names to scan for
   static const QStringList knownTools = {
      QStringLiteral("attempt_completion"),
      QStringLiteral("read_file"),
      QStringLiteral("write_to_file"),
      QStringLiteral("replace_in_file"),
      QStringLiteral("delete_file"),
      QStringLiteral("insert_before"),
      QStringLiteral("insert_after"),
      QStringLiteral("list_files"),
      QStringLiteral("search_files"),
      QStringLiteral("execute_command"),
      QStringLiteral("run_tests"),
      QStringLiteral("list_code_definition_names"),
      QStringLiteral("set_startup_file"),
      QStringLiteral("load_skill"),
      QStringLiteral("ask_question"),
      QStringLiteral("normalize_workspace_encoding"),
   };

   for (const QString& toolName : knownTools)
   {
      // Match <tool_name>...</tool_name> (non-greedy, across multiple lines)
      const QRegularExpression toolRe(
         QStringLiteral("<%1>(.*?)</%1>").arg(QRegularExpression::escape(toolName)),
         QRegularExpression::DotMatchesEverythingOption);

      QRegularExpressionMatchIterator it = toolRe.globalMatch(aText);
      while (it.hasNext())
      {
         const QRegularExpressionMatch match = it.next();
         const QString innerXml = match.captured(1);

         // Parse inner parameter tags: <param_name>value</param_name>
         QJsonObject args;
         const QRegularExpression paramRe(
            QStringLiteral("<(\\w+)>(.*?)</\\1>"),
            QRegularExpression::DotMatchesEverythingOption);

         QRegularExpressionMatchIterator paramIt = paramRe.globalMatch(innerXml);
         while (paramIt.hasNext())
         {
            const QRegularExpressionMatch paramMatch = paramIt.next();
            args[paramMatch.captured(1)] = paramMatch.captured(2);
         }

         ToolCall tc;
         tc.id           = QStringLiteral("xml_%1_%2").arg(toolName).arg(toolCalls.size());
         tc.functionName = toolName;
         tc.arguments    = args;
         toolCalls.append(tc);
      }

      // Remove matched XML from the text
      if (toolRe.match(aText).hasMatch())
      {
         aText.remove(toolRe);
      }
   }

   return toolCalls;
}

// ---- Private: LLM communication ----

void Task::SendToLLM()
{
   if (mLoopCount >= mMaxIterations)
   {
      emit TaskError("Maximum agentic loop iterations reached. Stopping.");
      SetState(State::Error);
      return;
   }
   ++mLoopCount;

   emit DebugEvent(QStringLiteral("LLM request loop=%1 max=%2 history=%3")
                     .arg(mLoopCount)
                     .arg(mMaxIterations)
                     .arg(mHistory.size()));

   mPendingAssistantText.clear();
   SetState(State::WaitingForLLM);

   // Refresh activated skill content into the system prompt before each request.
   // This ensures loaded skills survive context truncation — their content lives
   // in the system prompt, not just in the (truncatable) message history.
   if (mSkillManagerPtr && mPromptEnginePtr) {
      mPromptEnginePtr->SetActiveSkillContent(
         mSkillManagerPtr->BuildActivatedSkillsContent());
   }

   QString    systemPrompt = mPromptEnginePtr->BuildSystemPrompt();
   QJsonArray tools        = mToolRegistryPtr->GetToolDefinitionsJson();

   // Apply context management: dedup, truncate, repair
   QList<ChatMessage> prepared = mHistory;
   if (mContextManagerPtr)
   {
      // Inject the system prompt as the first message so ContextManager can
      // accurately estimate its size (including loaded skills) and truncate accordingly.
      QList<ChatMessage> historyWithSystem;
      ChatMessage sysMsg;
      sysMsg.mRole = MessageRole::System;
      sysMsg.mContent = systemPrompt;
      historyWithSystem.append(sysMsg);
      historyWithSystem.append(mHistory);

      prepared = mContextManagerPtr->PrepareMessages(historyWithSystem);
      
      // Remove the system prompt from the prepared history, as it's sent separately
      if (!prepared.isEmpty() && prepared.first().mRole == MessageRole::System)
      {
         prepared.removeFirst();
      }

      const auto& stats = mContextManagerPtr->GetLastStats();
      if (stats.didTruncate)
      {
         emit DebugEvent(QStringLiteral("Context truncated: removed=%1 remaining=%2")
                           .arg(stats.truncatedMessages)
                           .arg(stats.resultMessageCount));
      }
      if (stats.didDeduplicate)
      {
         emit DebugEvent(QStringLiteral("File reads deduplicated: count=%1")
                           .arg(stats.deduplicatedFileReads));
      }
   }

   emit DebugEvent(QStringLiteral("System prompt len=%1 tools=%2")
                     .arg(systemPrompt.size())
                     .arg(tools.size()));

   // Emit a snapshot of the full conversation context for debug logging
   emit ConversationSnapshot(prepared, mLoopCount);

   // If auto-condense was triggered synchronously by PrepareMessages(),
   // the Client already has a pending condense request in flight.
   // We must NOT send another request now — the condense completion
   // path will call SendToLLM() again with the condensed history.
   if (mIsCondensing)
   {
      emit DebugEvent(QStringLiteral("Auto-condense initiated during PrepareMessages, deferring main request"));
      return;
   }

   mClientPtr->SendChatRequest(prepared, systemPrompt, tools);
}

// ---- <function=NAME> tool call parser (DashScope/qwen format) ----

QList<ToolCall> Task::ParseFunctionToolCalls(QString& aText) const
{
   QList<ToolCall> toolCalls;

   static const QRegularExpression funcRe(
      QStringLiteral("<function=(\\w+)>(.*?)</function>"),
      QRegularExpression::DotMatchesEverythingOption);

   static const QRegularExpression paramRe(
      QStringLiteral("<parameter=(\\w+)>(.*?)</parameter>"),
      QRegularExpression::DotMatchesEverythingOption);

   QRegularExpressionMatchIterator it = funcRe.globalMatch(aText);
   while (it.hasNext())
   {
      const QRegularExpressionMatch match = it.next();
      const QString funcName = match.captured(1);
      const QString inner    = match.captured(2);

      QJsonObject args;
      QRegularExpressionMatchIterator paramIt = paramRe.globalMatch(inner);
      while (paramIt.hasNext())
      {
         const QRegularExpressionMatch pmatch = paramIt.next();
         args[pmatch.captured(1)] = pmatch.captured(2).trimmed();
      }

      ToolCall tc;
      tc.id           = QStringLiteral("func_%1_%2").arg(funcName).arg(toolCalls.size());
      tc.functionName = funcName;
      tc.arguments    = args;
      toolCalls.append(tc);
   }

   if (!toolCalls.isEmpty())
   {
      aText.remove(funcRe);
      // Also remove trailing </tool_call> tags that some models append
      static const QRegularExpression toolCallClose(QStringLiteral("</tool_call>"));
      aText.remove(toolCallClose);
   }

   return toolCalls;
}

// ---- Slots: Client signals ----

void Task::OnRequestStarted()
{
   emit LLMStarted();
}

void Task::OnRequestFinished()
{
   // No-op; final handling is in OnResponseCompleted / OnToolCallsReceived / OnError
}

void Task::OnResponseChunk(const QString& aChunk)
{
   // During auto-condense, accumulate into the condense buffer silently
   if (mIsCondensing)
   {
      mCondensePendingText += aChunk;
      return;
   }

   mPendingAssistantText += aChunk;
   emit DebugEvent(QStringLiteral("Assistant chunk len=%1 total=%2")
                     .arg(aChunk.size())
                     .arg(mPendingAssistantText.size()));
   emit AssistantChunk(aChunk);
}

void Task::OnResponseCompleted()
{
   if (mState != State::WaitingForLLM)
   {
      return;
   }

   // --- Auto-condense completion path ---
   if (mIsCondensing)
   {
      mIsCondensing = false;
      const QString summary = mCondensePendingText.trimmed();
      mCondensePendingText.clear();

      emit DebugEvent(QStringLiteral("Auto-condense response completed, summary len=%1")
                        .arg(summary.size()));

      if (summary.isEmpty())
      {
         // Summary failed — restore original history and fall back to programmatic truncation
         emit DebugEvent(QStringLiteral("Auto-condense produced empty summary, falling back"));
         mHistory = mPreCondenseHistory;
         mPreCondenseHistory.clear();

         if (mContextManagerPtr)
         {
            mHistory = mContextManagerPtr->ForceTruncate(mHistory);
         }
         SendToLLM();
         return;
      }

      // Build condensed history:
      //   1. Keep the first user message (establishes original intent)
      //   2. Insert the condensation-continuation as a user message
      mHistory.clear();
      if (!mPreCondenseHistory.isEmpty())
      {
         // Find and keep the first user message
         for (const auto& msg : mPreCondenseHistory)
         {
            if (msg.mRole == MessageRole::User)
            {
               mHistory.append(msg);
               break;
            }
         }
         // Also keep the last user message if different from first
         for (int i = mPreCondenseHistory.size() - 1; i >= 0; --i)
         {
            if (mPreCondenseHistory[i].mRole == MessageRole::User && mHistory.size() < 2)
            {
               if (mHistory.isEmpty() || mPreCondenseHistory[i].mContent != mHistory.first().mContent)
               {
                  mHistory.append(mPreCondenseHistory[i]);
               }
               break;
            }
         }
      }
      mPreCondenseHistory.clear();

      // Insert the continuation prompt as a user message so the LLM sees the summary
      ChatMessage continuationMsg;
      continuationMsg.mRole    = MessageRole::User;
      continuationMsg.mContent = PromptEngine::BuildContinuationPrompt(summary);
      mHistory.append(continuationMsg);

      emit CondenseCompleted(summary);

      // Resume the task — send the condensed history to the LLM
      SendToLLM();
      return;
   }

   // --- Normal completion path ---
   emit DebugEvent(QStringLiteral("Assistant streaming completed len=%1")
                     .arg(mPendingAssistantText.size()));

   // Streaming text-only response completed
   if (!mPendingAssistantText.isEmpty())
   {
      // Strip <think>/<thinking> tags and their content before saving to history.
      // The UI layer handles the animated display; the history should be clean text.
      // static const QRegularExpression thinkBlock(
      //    QStringLiteral("<think(?:ing)?>.*?</think(?:ing)?>"),
      //    QRegularExpression::DotMatchesEverythingOption | QRegularExpression::CaseInsensitiveOption);
      QString cleanText = mPendingAssistantText;
      // cleanText.remove(thinkBlock);
      // Also remove any unclosed think block at the start (model may omit closing tag)
      // static const QRegularExpression unclosedThink(
      //    QStringLiteral("^<think(?:ing)?>.*"),
      //    QRegularExpression::DotMatchesEverythingOption | QRegularExpression::CaseInsensitiveOption);
      // cleanText.remove(unclosedThink);
      cleanText = cleanText.trimmed();

      // --- XML tool call fallback (Cline-style) ---
      // Some models output tool calls as XML tags instead of using the
      // function calling API.  Detect and route them properly.
      QList<ToolCall> xmlToolCalls = ParseXmlToolCalls(cleanText);
      if (!xmlToolCalls.isEmpty())
      {
         cleanText = cleanText.trimmed();
         emit DebugEvent(QStringLiteral("XML fallback: parsed %1 tool call(s) from text")
                           .arg(xmlToolCalls.size()));

         // Record the remaining text (before the XML tags) as assistant message
         ChatMessage assistantMsg;
         assistantMsg.mRole      = MessageRole::Assistant;
         assistantMsg.mContent   = cleanText;
         assistantMsg.mToolCalls = xmlToolCalls;
         mHistory.append(assistantMsg);

         if (!cleanText.isEmpty())
         {
            emit AssistantMessage(cleanText);
         }

         // Execute the parsed tool calls (same flow as OnToolCallsReceived)
         ExecuteToolCalls(xmlToolCalls);
         return;
      }

      // --- <function=NAME> tool call fallback (DashScope/qwen format) ---
      // Models using DashScope may return tool calls as:
      //   <function=NAME><parameter=PARAM>VALUE</parameter></function>
      QList<ToolCall> funcToolCalls = ParseFunctionToolCalls(cleanText);
      if (!funcToolCalls.isEmpty())
      {
         cleanText = cleanText.trimmed();
         emit DebugEvent(QStringLiteral("Function-style fallback: parsed %1 tool call(s) from text")
                           .arg(funcToolCalls.size()));

         ChatMessage assistantMsg;
         assistantMsg.mRole      = MessageRole::Assistant;
         assistantMsg.mContent   = cleanText;
         assistantMsg.mToolCalls = funcToolCalls;
         mHistory.append(assistantMsg);

         if (!cleanText.isEmpty())
         {
            emit AssistantMessage(cleanText);
         }

         ExecuteToolCalls(funcToolCalls);
         return;
      }

      // Record assistant message in history
      ChatMessage assistantMsg;
      assistantMsg.mRole    = MessageRole::Assistant;
      assistantMsg.mContent = cleanText.isEmpty() ? mPendingAssistantText : cleanText;
      mHistory.append(assistantMsg);

      emit AssistantMessage(cleanText.isEmpty() ? mPendingAssistantText : cleanText);
   }

   SetState(State::Completed);
   emit TaskCompleted();
}

void Task::OnResponseReceived(const QString& aResponse)
{
   if (mState != State::WaitingForLLM)
   {
      return;
   }

   // During auto-condense (non-streaming API), treat as if the full response is the summary
   if (mIsCondensing)
   {
      mCondensePendingText = aResponse;
      OnResponseCompleted();
      return;
   }

   emit DebugEvent(QStringLiteral("Assistant response received len=%1")
                     .arg(aResponse.size()));

   // --- XML tool call fallback (non-streaming) ---
   QString responseText = aResponse;
   QList<ToolCall> xmlToolCalls = ParseXmlToolCalls(responseText);
   if (!xmlToolCalls.isEmpty())
   {
      responseText = responseText.trimmed();
      emit DebugEvent(QStringLiteral("XML fallback (non-stream): parsed %1 tool call(s)")
                        .arg(xmlToolCalls.size()));

      ChatMessage assistantMsg;
      assistantMsg.mRole      = MessageRole::Assistant;
      assistantMsg.mContent   = responseText;
      assistantMsg.mToolCalls = xmlToolCalls;
      mHistory.append(assistantMsg);

      if (!responseText.isEmpty())
      {
         emit AssistantMessage(responseText);
      }

      ExecuteToolCalls(xmlToolCalls);
      return;
   }

   // --- <function=NAME> tool call fallback (non-streaming, DashScope/qwen format) ---
   QList<ToolCall> funcToolCalls = ParseFunctionToolCalls(responseText);
   if (!funcToolCalls.isEmpty())
   {
      responseText = responseText.trimmed();
      emit DebugEvent(QStringLiteral("Function-style fallback (non-stream): parsed %1 tool call(s)")
                        .arg(funcToolCalls.size()));

      ChatMessage assistantMsg;
      assistantMsg.mRole      = MessageRole::Assistant;
      assistantMsg.mContent   = responseText;
      assistantMsg.mToolCalls = funcToolCalls;
      mHistory.append(assistantMsg);

      if (!responseText.isEmpty())
      {
         emit AssistantMessage(responseText);
      }

      ExecuteToolCalls(funcToolCalls);
      return;
   }

   // Non-streaming full response
   ChatMessage assistantMsg;
   assistantMsg.mRole    = MessageRole::Assistant;
   assistantMsg.mContent = aResponse;
   mHistory.append(assistantMsg);

   emit AssistantMessage(aResponse);
   SetState(State::Completed);
   emit TaskCompleted();
}

void Task::OnToolCallsReceived(const QList<ToolCall>& aToolCalls)
{
   if (mState != State::WaitingForLLM)
   {
      return;
   }

   // During auto-condense, ignore tool calls (the summary prompt should not request tools).
   // However, the proxy may have injected synthetic tool_calls from <function=...> content.
   // We must still trigger the condense completion path, otherwise ResponseCompleted
   // will never fire and the task gets stuck in WaitingForLLM.
   if (mIsCondensing)
   {
      emit DebugEvent(QStringLiteral("Ignoring tool calls during auto-condense, triggering completion"));
      OnResponseCompleted();
      return;
   }

   emit DebugEvent(QStringLiteral("Tool calls received count=%1")
                     .arg(aToolCalls.size()));

   for (const auto& tc : aToolCalls)
   {
      if (tc.functionName.isEmpty())
      {
         SetState(State::Error);
         emit TaskError("Malformed tool call received (missing function name)." );
         return;
      }
   }

   // Strip <function=...> patterns from the accumulated text.
   // When the proxy injects tool_calls from content-embedded function calls,
   // the raw text is already streamed into mPendingAssistantText.  We must
   // clean it before storing in history to avoid redundant/confusing content.
   static const QRegularExpression funcStripRe(
      QStringLiteral("<function=\\w+>.*?</function>\\s*(?:</tool_call>)?"),
      QRegularExpression::DotMatchesEverythingOption);
   QString cleanContent = mPendingAssistantText;
   cleanContent.remove(funcStripRe);
   cleanContent = cleanContent.trimmed();

   // Record assistant message with tool_calls in history
   ChatMessage assistantMsg;
   assistantMsg.mRole      = MessageRole::Assistant;
   assistantMsg.mContent   = cleanContent;
   assistantMsg.mToolCalls = aToolCalls;
   mHistory.append(assistantMsg);

   ExecuteToolCalls(aToolCalls);
}

void Task::OnError(const QString& aError)
{
   // If error occurs during auto-condense, restore history and fall back
   if (mIsCondensing)
   {
      mIsCondensing = false;
      mCondensePendingText.clear();
      emit DebugEvent(QStringLiteral("Auto-condense request failed: %1, falling back to truncation")
                        .arg(aError));

      mHistory = mPreCondenseHistory;
      mPreCondenseHistory.clear();

      if (mContextManagerPtr)
      {
         mHistory = mContextManagerPtr->ForceTruncate(mHistory);
      }
      --mLoopCount; // Don't count the condense attempt
      SendToLLM();
      return;
   }

   // Phase 3: Context overflow auto-recovery
   if (mContextManagerPtr && ContextManager::IsContextWindowError(aError))
   {
      if (mContextRetryCount < kMaxContextRetries)
      {
         ++mContextRetryCount;
         emit DebugEvent(QStringLiteral("Context overflow detected, force-truncating (attempt %1/%2)")
                           .arg(mContextRetryCount)
                           .arg(kMaxContextRetries));

         // Force aggressive truncation and retry
         mHistory = mContextManagerPtr->ForceTruncate(mHistory);
         --mLoopCount; // Don't count the failed attempt
         SendToLLM();
         return;
      }
      else
      {
         emit DebugEvent(QStringLiteral("Context overflow recovery exhausted after %1 attempts")
                           .arg(kMaxContextRetries));
      }
   }

   mContextRetryCount = 0;
   SetState(State::Error);
   emit TaskError(aError);
}

// ---- Tool execution ----

void Task::ExecuteToolCalls(const QList<ToolCall>& aToolCalls)
{
   SetState(State::ExecutingTools);

   mCurrentToolCalls = aToolCalls;
   mToolCallStates.clear();

   // IMPORTANT: Register ALL tool call states BEFORE executing any of them.
   // Some tools (e.g. list_files, read_file) complete synchronously, which
   // triggers OnToolExecutorFinished -> CheckToolCallsComplete during Execute().
   // If we interleave registration and execution, CheckToolCallsComplete may see
   // only a partial set and incorrectly conclude all tools are done.
   for (const auto& tc : aToolCalls)
   {
      ToolCallState state;
      state.call = tc;
      mToolCallStates.append(state);
   }

   // Now execute them. Synchronous completions will correctly see the full batch.
   for (const auto& tc : aToolCalls)
   {
      emit ToolExecutionStarted(tc.id, tc.functionName);
      emit DebugEvent(QStringLiteral("Tool call: %1 (id=%2, args=%3 bytes)")
            .arg(tc.functionName, tc.id)
            .arg(QJsonDocument(tc.arguments).toJson(QJsonDocument::Compact).size()));

      mToolExecutorPtr->Execute(tc);
   }
}

void Task::OnToolExecutorFinished(const QString& aToolCallId, const ToolResult& aResult)
{
   emit DebugEvent(QStringLiteral("Tool finished id=%1 success=%2")
                     .arg(aToolCallId)
                     .arg(aResult.success ? QStringLiteral("true") : QStringLiteral("false")));
   for (auto& state : mToolCallStates)
   {
      if (state.call.id == aToolCallId && !state.completed)
      {
         state.result    = aResult;
         state.completed = true;
         emit ToolExecutionFinished(aToolCallId, state.call.functionName, aResult);
         break;
      }
   }
   CheckToolCallsComplete();
}

void Task::OnApprovalRequired(const QString& aToolCallId, const QString& aToolName,
                               const QString& aDiffPreview, const QJsonObject& aParams)
{
   // Only ask_question triggers this path (all other tools auto-execute).
   // Block the agentic loop until the user provides input.
   SetState(State::WaitingForApproval);
   emit DebugEvent(QStringLiteral("Approval required id=%1 tool=%2 diffLen=%3")
                     .arg(aToolCallId, aToolName)
                     .arg(aDiffPreview.size()));
   emit ToolApprovalNeeded(aToolCallId, aToolName, aDiffPreview, aParams);
}

void Task::CheckToolCallsComplete()
{
   // Are all tool calls done?
   for (const auto& state : mToolCallStates)
   {
      if (!state.completed)
      {
         return; // Still waiting
      }
   }

   // All tool calls are done. Append tool results to history and continue the loop.
   bool hasCompletion  = false;
   QString completionResult;

   for (const auto& state : mToolCallStates)
   {
      ChatMessage toolMsg;
      toolMsg.mRole       = MessageRole::Tool;
      toolMsg.mToolCallId = state.call.id;
      toolMsg.mToolName   = state.call.functionName;   // Populate tool name for dedup
      toolMsg.mContent    = TruncateToolResult(state.result.content);
      if (toolMsg.mContent.size() != state.result.content.size())
      {
         emit DebugEvent(QStringLiteral("Tool result truncated id=%1 orig=%2 max=%3")
                           .arg(state.call.id)
                           .arg(state.result.content.size())
                           .arg(kMaxToolResultChars));
      }
      mHistory.append(toolMsg);

      // Detect attempt_completion — stop the agentic loop
      if (state.call.functionName == QStringLiteral("attempt_completion"))
      {
         hasCompletion    = true;
         completionResult = state.result.content;
         emit DebugEvent(QStringLiteral("attempt_completion detected len=%1")
                           .arg(completionResult.size()));
      }
   }

   mToolCallStates.clear();
   mCurrentToolCalls.clear();

   if (hasCompletion)
   {
      // Task is done — stop the agentic loop.
      // Do NOT emit AssistantMessage here: the completion text is already
      // displayed via OnToolFinished as the tool result indicator.
      // Emitting it would overwrite the accumulated bubble content
      // (including all tool call indicators from the session).
      SetState(State::Completed);
      emit TaskCompleted(completionResult);
      return;
   }

   emit DebugEvent(QStringLiteral("Tool batch complete, continuing loop"));

   // Continue the agentic loop — send history back to LLM
   SendToLLM();
}

void Task::SetState(State aState)
{
   if (mState != aState)
   {
      mState = aState;
      emit DebugEvent(QStringLiteral("State changed to %1").arg(static_cast<int>(aState)));
      emit StateChanged(aState);
   }
}

} // namespace AiChat
