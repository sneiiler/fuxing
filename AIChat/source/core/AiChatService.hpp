// -----------------------------------------------------------------------------
// File: AiChatService.hpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-12
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#ifndef AICHAT_SERVICE_HPP
#define AICHAT_SERVICE_HPP

#include <QObject>
#include <QMap>
#include <QString>
#include <QList>
#include <QJsonObject>

#include "tools/AiChatToolTypes.hpp"
#include "AiChatClient.hpp"
#include "AiChatSessionManager.hpp"

namespace AiChat
{

struct InlineReviewSummary
{
   QString filePath;
   int addedLines{0};
   int removedLines{0};
};

struct InlineReviewOutcome
{
   InlineReviewSummary summary;
   bool success{false};
   QString message;
};

enum class InlineReviewStatus
{
   Ready,
   AutoAccepted,
   Failed
};


class PrefObject;
class Client;
class Task;
class ToolRegistry;
class ToolExecutor;
class PromptEngine;
class ProcessRunner;
class SessionManager;
class SkillManager;
class PathAccessManager;
class AutoApprovePolicy;
class ContextManager;

/// Central business logic controller for the AI Chat plugin.
/// Owns the agent lifecycle, session management, and tool execution.
/// Decouples the UI (DockWidget) from the underlying logic.
class Service : public QObject
{
   Q_OBJECT
public:
   explicit Service(PrefObject* aPrefObject, QObject* aParent = nullptr);
   ~Service() override;

   // --- Lifecycle ---
   void Initialize();

   // --- Session Management ---
   void NewSession(const QString& aTitle = QString());
   void LoadSession(const QString& aSessionId);
   void DeleteSession(const QString& aSessionId);
   void RenameSession(const QString& aSessionId, const QString& aNewTitle);
   QList<SessionInfo> ListSessions();
   QString CurrentSessionId() const;
   QString CurrentSessionTitle();

   // --- Chat Interaction ---
   void SendUserMessage(const QString& aText);
   void AbortTask();
   const QList<ChatMessage>& GetHistory() const;

   // --- Tool Interaction ---
   void ApproveTool(const QString& aToolCallId, const QString& aFeedback = {});
   void RejectTool(const QString& aToolCallId, const QString& aFeedback);
   void ResolveApprovalWithResult(const QString& aToolCallId, bool aApproved,
                                  const ToolResult& aResult);

   // --- Inline Review (post-hoc, does not block AI) ---
   InlineReviewStatus BeginInlineReview(const QString& aFilePath,
                                        const QString& aOriginalContent,
                                        const QString& aProposedContent,
                                        InlineReviewSummary& aSummary,
                                        QString& aError);
   InlineReviewOutcome AcceptInlineReview(const QString& aFilePath);
   InlineReviewOutcome RejectInlineReview(const QString& aFilePath);
   bool HasPendingInlineReview(const QString& aFilePath) const;
   void ClearAllInlineReviews();
   /// Remove pending review bookkeeping only (no editor interaction).
   /// Used when the editor is being destroyed externally.
   void ForgetInlineReview(const QString& aFilePath);

   // --- Configuration ---
   void ReloadSkills();
   void SaveSkillToggles();
   void RestoreSkillToggles();
   void SetModel(const QString& aModel);

signals:
   // --- Task Status ---
   void TaskStarted();
   void TaskFinished(const QString& aSummary); // Summary if available
   void TaskError(const QString& aError);
   void TaskAborted();

   // --- Chat Stream ---
   void AssistantChunkReceived(const QString& aChunk);
   void AssistantMessageUpdated(const QString& aFullText);
   void HistoryChanged(); // Emitted on new session, load session, or cleared history

   // --- Tool Activity ---
   void ToolStarted(const QString& aToolName);
   void ToolFinished(const QString& aToolName, const ToolResult& aResult);
   void ApprovalRequired(const QString& aToolCallId, const QString& aToolName,
                         const QString& aDiffPreview, const QJsonObject& aParams);
   void ToolCallStreaming(const QString& aFunctionName, int aArgBytes);

   // --- Inline Review ---
   /// Emitted when a file modification is ready for post-hoc inline review in the editor.
   void InlineReviewReady(const QString& aFilePath, const InlineReviewSummary& aSummary);
   
   // --- Internal Events (Debug) ---
   void DebugMessage(const QString& aMessage);
   
   // --- Metadata ---
   void SessionListChanged();
   void AvailableModelsChanged(const QStringList& aModels);

   // --- Context Management ---
   /// Emitted when token usage is updated (for UI display)
   void TokenUsageUpdated(double aRatio, int aUsedTokens, int aMaxTokens);
   /// Emitted when context was programmatically truncated (sliding window)
   void ContextTruncated(int aRemovedCount, int aRemainingCount);
   /// Emitted when the LLM auto-condense (summarisation) completes
   void ContextCondensed();

private slots:
   void OnClientError(const QString& aError);
   void OnModelsFetched(const QStringList& aModels);
   
   void OnTaskAssistantChunk(const QString& aChunk);
   void OnTaskAssistantMessage(const QString& aMessage);
   void OnTaskToolFinished(const QString& aToolName, const ToolResult& aResult);
   void OnTaskApprovalRequired(const QString& aToolCallId, const QString& aToolName,
                               const QString& aDiffPreview, const QJsonObject& aParams);
   void OnTaskCompleted(const QString& aSummary);
   
   void OnPrefsConfigChanged();
   void OnConversationSnapshot(const QList<ChatMessage>& aMessages, int aLoopCount);

private:
   void SetupSmartCodingEngine();
   void RegisterAllTools();
   void LogConversationToFile(const QList<ChatMessage>& aMessages, int aLoopCount);

   PrefObject*        mPrefObjectPtr{nullptr};
   Client*            mClient{nullptr};

   // Core Components
   ContextManager*    mContextManager{nullptr};
   SessionManager*    mSessionManager{nullptr};
   ProcessRunner*     mProcessRunner{nullptr};
   ToolRegistry*      mToolRegistry{nullptr};
   PathAccessManager* mPathAccess{nullptr};
   AutoApprovePolicy* mAutoApprovePolicy{nullptr};
   ToolExecutor*      mToolExecutor{nullptr};
   PromptEngine*      mPromptEngine{nullptr};
   SkillManager*      mSkillManager{nullptr};
   Task*              mTask{nullptr};
   Client*            mTitleClient{nullptr};
   QString            mTitleBuffer;
   bool               mLlmTitleGenerated{false};
   // Agent-only mode: Task owns history

   struct InlineChange
   {
      QString filePath;
      QString originalContent;
      QString proposedContent;
      int     addedLines{0};
      int     removedLines{0};
   };

   /// Pending inline reviews keyed by resolved file path
   QMap<QString, InlineChange> mPendingInlineChanges;
};

} // namespace AiChat

#endif // AICHAT_SERVICE_HPP
