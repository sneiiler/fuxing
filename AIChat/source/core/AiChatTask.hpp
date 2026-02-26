// -----------------------------------------------------------------------------
// File: AiChatTask.hpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#ifndef AICHAT_TASK_HPP
#define AICHAT_TASK_HPP

#include <QList>
#include <QObject>
#include <QString>

#include "AiChatClient.hpp"
#include "tools/AiChatToolTypes.hpp"

class QJsonArray;

namespace AiChat
{
class Client;
class ToolExecutor;
class ToolRegistry;
class PromptEngine;
class ContextManager;
class SkillManager;

/// Manages a single agentic conversation loop:
///   User message → LLM → (optional tool calls → tool results → LLM)* → final answer
class Task : public QObject
{
   Q_OBJECT
public:
   enum class State
   {
      Idle,
      WaitingForLLM,
      ExecutingTools,
      WaitingForApproval,
      Completed,
      Error
   };

   Task(Client*         aClient,
        ToolExecutor*   aToolExecutor,
        ToolRegistry*   aToolRegistry,
        PromptEngine*   aPromptEngine,
        ContextManager* aContextManager,
        SkillManager*   aSkillManager,
        QObject*        aParent = nullptr);
   ~Task() override = default;

   /// Start a new task with the user's message.
   /// Existing conversation history is preserved (multi-turn).
   void Start(const QString& aUserMessage);

   /// Abort the current task
   void Abort();

   /// Get current state
   State GetState() const noexcept { return mState; }

   /// Get the full conversation history (for display)
   const QList<ChatMessage>& GetHistory() const noexcept { return mHistory; }

   /// Clear conversation history (new session)
   void ClearHistory();

   /// Replace conversation history (session restore)
   void SetHistory(const QList<ChatMessage>& aHistory);

   /// Set the maximum agentic loop iterations (from user preferences)
   void SetMaxIterations(int aMax) { mMaxIterations = qBound(1, aMax, 200); }

   /// Handle user approval/rejection for a pending tool call
   void HandleApproval(const QString& aToolCallId, bool aApproved, const QString& aFeedback = {});

   /// Trigger LLM auto-condense: summarize the conversation and replace history.
   /// Called by Service when ContextManager emits AutoCondenseRequested.
   void CondenseHistory();

signals:
   /// The LLM started generating
   void LLMStarted();

   /// A chunk of assistant text arrived (streaming)
   void AssistantChunk(const QString& aChunk);

   /// The LLM produced a final text response (task complete or intermediate)
   void AssistantMessage(const QString& aFullText);

   /// A tool is about to be executed
   void ToolExecutionStarted(const QString& aToolCallId, const QString& aToolName);

   /// A tool completed execution
   void ToolExecutionFinished(const QString& aToolCallId, const QString& aToolName, const ToolResult& aResult);

   /// A tool needs user approval (with diff preview)
   void ToolApprovalNeeded(const QString& aToolCallId, const QString& aToolName,
                           const QString& aDiffPreview, const QJsonObject& aParams);

   /// The entire task completed (aResult carries the completion summary if any)
   void TaskCompleted(const QString& aResult = {});

   /// An error occurred
   void TaskError(const QString& aError);

   /// State changed
   void StateChanged(State aNewState);

   /// Debug logging
   void DebugEvent(const QString& aMessage);

   /// Emitted before each LLM request with the full prepared message list.
   /// Used by Service to log the conversation context for debugging.
   void ConversationSnapshot(const QList<ChatMessage>& aPreparedMessages,
                             int aLoopCount);

   /// Auto-condense completed (history was summarized and replaced)
   void CondenseCompleted(const QString& aSummary);

private slots:
   void OnResponseChunk(const QString& aChunk);
   void OnResponseCompleted();
   void OnResponseReceived(const QString& aResponse);
   void OnToolCallsReceived(const QList<ToolCall>& aToolCalls);
   void OnError(const QString& aError);
   void OnRequestStarted();
   void OnRequestFinished();

   void OnToolExecutorFinished(const QString& aToolCallId, const ToolResult& aResult);
   void OnApprovalRequired(const QString& aToolCallId, const QString& aToolName,
                           const QString& aDiffPreview, const QJsonObject& aParams);

private:
   void SetState(State aState);
   void SendToLLM();
   void ExecuteToolCalls(const QList<ToolCall>& aToolCalls);
   void CheckToolCallsComplete();

   /// Parse XML-style tool calls from the assistant text (Cline fallback format).
   /// Returns parsed tool calls; clears the matched XML from @a aText.
   QList<ToolCall> ParseXmlToolCalls(QString& aText) const;

   /// Parse <function=NAME><parameter=...>...</parameter></function> tool calls
   /// (DashScope/qwen format). Returns parsed tool calls; clears matched text from @a aText.
   QList<ToolCall> ParseFunctionToolCalls(QString& aText) const;

   State mState{State::Idle};

   Client*       mClientPtr;
   ToolExecutor* mToolExecutorPtr;
   ToolRegistry* mToolRegistryPtr;
   PromptEngine* mPromptEnginePtr;
   ContextManager* mContextManagerPtr;
   SkillManager*   mSkillManagerPtr;

   QList<ChatMessage> mHistory;

   /// Streaming assistant text accumulator
   QString mPendingAssistantText;

   /// Current batch of tool calls from the assistant
   QList<ToolCall> mCurrentToolCalls;

   /// Results collected for the current batch of tool calls
   struct ToolCallState
   {
      ToolCall   call;
      ToolResult result;
      bool       completed{false};
   };
   QList<ToolCallState> mToolCallStates;

   /// Safety: limit the number of agentic loop iterations
   int mLoopCount{0};
   int mMaxIterations{25};

   /// Context overflow recovery: how many times we've retried after truncation
   int mContextRetryCount{0};
   static constexpr int kMaxContextRetries = 2;

   /// Auto-condense state
   bool mIsCondensing{false};
   QString mCondensePendingText;             ///< Accumulator for the summary response
   QList<ChatMessage> mPreCondenseHistory;   ///< Saved history before condense (for rollback)
};

} // namespace AiChat

#endif // AICHAT_TASK_HPP
