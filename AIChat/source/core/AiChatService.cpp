// -----------------------------------------------------------------------------
// File: AiChatService.cpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-12
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#include "AiChatService.hpp"

#include "../AiChatPrefObject.hpp"
#include "AiChatContextManager.hpp"
#include "AiChatPathAccessManager.hpp"
#include "AiChatAutoApprovePolicy.hpp"
#include "AiChatTask.hpp"
#include "AiChatPromptEngine.hpp"
#include "AiChatSkillManager.hpp"
#include "AiChatSessionManager.hpp"

#include "../tools/AiChatToolRegistry.hpp"
#include "../tools/AiChatToolExecutor.hpp"
#include "../tools/AiChatFileEditUtils.hpp"

// Tool Handlers
#include "../tools/ReadFileToolHandler.hpp"
#include "../tools/WriteFileToolHandler.hpp"
#include "../tools/ReplaceInFileToolHandler.hpp"
#include "../tools/DeleteFileToolHandler.hpp"
#include "../tools/InsertBeforeToolHandler.hpp"
#include "../tools/InsertAfterToolHandler.hpp"
#include "../tools/ListFilesToolHandler.hpp"
#include "../tools/SearchFilesToolHandler.hpp"
#include "../tools/ExecuteCommandToolHandler.hpp"
#include "../tools/ListCodeDefinitionNamesToolHandler.hpp"
#include "../tools/AskQuestionToolHandler.hpp"
#include "../tools/AttemptCompletionToolHandler.hpp"
#include "../tools/NormalizeEncodingToolHandler.hpp"
#include "../tools/LoadSkillToolHandler.hpp"
#include "../tools/SetStartupFileToolHandler.hpp"
#include "../tools/RunTestsToolHandler.hpp"

#include "../bridge/AiChatProcessRunner.hpp"
#include "../bridge/AiChatEditorBridge.hpp"
#include "../bridge/AiChatProjectBridge.hpp"

#include <QTimer>
#include <QFileInfo>
#include <QFile>
#include <QDir>
#include <QTextStream>
#include <QDateTime>
#include <QRegularExpression>

#include "UtAlgorithm.hpp"
#include "UtStringUtil.hpp"

namespace AiChat
{

Service::Service(PrefObject* aPrefObject, QObject* aParent)
   : QObject(aParent)
   , mPrefObjectPtr(aPrefObject)
   , mClient(new Client(aPrefObject, this))
{
   connect(mClient, &Client::ErrorOccurred, this, &Service::OnClientError);
   connect(mClient, &Client::ModelsReceived, this, &Service::OnModelsFetched);
   connect(mClient, &Client::DebugEvent, this, &Service::DebugMessage);
   connect(mClient, &Client::ToolCallStreaming, this, &Service::ToolCallStreaming);
   
   if (mPrefObjectPtr) {
      connect(mPrefObjectPtr, &PrefObject::ConfigurationChanged, this, &Service::OnPrefsConfigChanged);
   }
}

Service::~Service() = default;

void Service::Initialize()
{
   SetupSmartCodingEngine();
   
   // Fetch models on startup
   QTimer::singleShot(500, mClient, &Client::FetchModels);
}

void Service::SetupSmartCodingEngine()
{
   // 0. Session manager
   mSessionManager = new SessionManager(this);
   {
      const QString workspaceRoot = ProjectBridge::GetWorkspaceRoot();
      if (!workspaceRoot.isEmpty())
      {
         mSessionManager->SetStorageDirectory(workspaceRoot + QStringLiteral("/.aichat/sessions"));
      }
      connect(mSessionManager, &SessionManager::SessionListChanged, this, &Service::SessionListChanged);
   }

   // 1. Process runner
   mProcessRunner = new ProcessRunner(this);

   // 2. Tool registry
   mToolRegistry = new ToolRegistry();

   // 2.1 Path access + approval policy
   mPathAccess = new PathAccessManager(mPrefObjectPtr);
   mAutoApprovePolicy = new AutoApprovePolicy(mPrefObjectPtr, mPathAccess);

   // 3. Tool executor
   mToolExecutor = new ToolExecutor(mToolRegistry, mAutoApprovePolicy, this);

   // 4. Prompt engine
   mPromptEngine = new PromptEngine();

   // 4.1 Skills (load once from project roots)
   mSkillManager = new SkillManager();
   mSkillManager->LoadFromProject(ProjectBridge::GetWorkspaceRoot(), {});
   if (mPromptEngine) {
      mPromptEngine->SetSkillCatalogSummary(mSkillManager->BuildCatalogSummary());
   }

   // 4.1.1 Allow read_file to access the global skills directory so the LLM
   //       can read SKILL.md / references/ files after load_skill activates a skill.
   if (mPathAccess) {
      mPathAccess->AddAllowedExternalPath(SkillManager::GetGlobalSkillsDirectory());
   }

   // Apply user preferences to the prompt engine
   if (mPrefObjectPtr) {
      mPromptEngine->SetCustomInstructions(mPrefObjectPtr->GetCustomInstructions());
   }

   // 4.2 Context manager
   mContextManager = new ContextManager(this);
   {
      const int contextWindow = mPrefObjectPtr ? mPrefObjectPtr->GetContextWindowSize() : 128000;
      mContextManager->SetContextWindow(contextWindow);
      if (mPrefObjectPtr)
      {
         mContextManager->SetAutoCondenseEnabled(mPrefObjectPtr->GetAutoCondenseEnabled());
         mContextManager->SetAutoCondenseThreshold(mPrefObjectPtr->GetAutoCondenseThreshold());
      }
   }

   // Wire Client::UsageReceived → ContextManager
   connect(mClient, &Client::UsageReceived, this,
           [this](int aPrompt, int aCompletion, int aTotal) {
              if (mContextManager) {
                 TokenUsage usage;
                 usage.promptTokens     = aPrompt;
                 usage.completionTokens = aCompletion;
                 usage.totalTokens      = aTotal;
                 mContextManager->RecordTokenUsage(usage);
              }
           });

   // Forward ContextManager signals to Service level
   connect(mContextManager, &ContextManager::UsageUpdated,
           this, &Service::TokenUsageUpdated);
   connect(mContextManager, &ContextManager::ContextTruncated,
           this, &Service::ContextTruncated);

   // 5. Task
   mTask = new Task(mClient, mToolExecutor, mToolRegistry, mPromptEngine,
                    mContextManager, mSkillManager, this);
   if (mPrefObjectPtr) {
      mTask->SetMaxIterations(mPrefObjectPtr->GetMaxIterations());
   }

   // Register built-in tools
   RegisterAllTools();

   // --- Wire up Task signals ---
   connect(mTask, &Task::AssistantChunk, this, &Service::OnTaskAssistantChunk);
   connect(mTask, &Task::AssistantMessage, this, &Service::OnTaskAssistantMessage);
   connect(mTask, &Task::TaskCompleted, this, &Service::OnTaskCompleted);
   connect(mTask, &Task::TaskError, this, &Service::TaskError);
   connect(mTask, &Task::DebugEvent, this, &Service::DebugMessage);
   connect(mTask, &Task::LLMStarted, this, &Service::TaskStarted);
   connect(mTask, &Task::ConversationSnapshot, this, &Service::OnConversationSnapshot);

   // --- Phase 4: Auto-condense wiring ---
   connect(mContextManager, &ContextManager::AutoCondenseRequested, this,
           [this]() {
              emit DebugMessage(QStringLiteral("Auto-condense triggered by ContextManager"));
              if (mTask) { mTask->CondenseHistory(); }
           });
   connect(mTask, &Task::CondenseCompleted, this,
           [this](const QString& aSummary) {
              emit DebugMessage(QStringLiteral("Auto-condense completed, summary len=%1").arg(aSummary.size()));
              if (mContextManager) { mContextManager->ResetCondenseRequested(); }
              emit ContextCondensed();
           });

   // Wire up ToolExecutor signals
   connect(mToolExecutor, &ToolExecutor::ToolStarted, this,
           [this](const QString& /*aToolCallId*/, const QString& aToolName) {
              emit ToolStarted(aToolName);
           });
   connect(mToolExecutor, &ToolExecutor::ApprovalRequired,
           this, &Service::OnTaskApprovalRequired);

   // Task-level tool finished carries tool name
   connect(mTask, &Task::ToolExecutionFinished,
           this, [this](const QString&, const QString& aToolName, const ToolResult& aResult) {
              OnTaskToolFinished(aToolName, aResult);
           });
   
   // Create an initial session
   NewSession();
}

void Service::RegisterAllTools()
{
   if (!mToolRegistry) return;

   // File operations
   mToolRegistry->Register(std::make_unique<ReadFileToolHandler>(mPathAccess));
   mToolRegistry->Register(std::make_unique<WriteFileToolHandler>(mPathAccess));
   mToolRegistry->Register(std::make_unique<ReplaceInFileToolHandler>(mPathAccess));
   mToolRegistry->Register(std::make_unique<DeleteFileToolHandler>(mPathAccess));
   mToolRegistry->Register(std::make_unique<InsertBeforeToolHandler>(mPathAccess));
   mToolRegistry->Register(std::make_unique<InsertAfterToolHandler>(mPathAccess));
   mToolRegistry->Register(std::make_unique<ListFilesToolHandler>(mPathAccess));
   mToolRegistry->Register(std::make_unique<SearchFilesToolHandler>(mPrefObjectPtr, mPathAccess));

   // Commands and code
   mToolRegistry->Register(std::make_unique<ExecuteCommandToolHandler>(mProcessRunner));
   mToolRegistry->Register(std::make_unique<ListCodeDefinitionNamesToolHandler>(mPathAccess));
   mToolRegistry->Register(std::make_unique<SetStartupFileToolHandler>(mPathAccess));
   mToolRegistry->Register(std::make_unique<RunTestsToolHandler>(mProcessRunner, mPathAccess));

   // Conversation / Logic
   mToolRegistry->Register(std::make_unique<AskQuestionToolHandler>());
   mToolRegistry->Register(std::make_unique<AttemptCompletionToolHandler>());
   mToolRegistry->Register(std::make_unique<NormalizeEncodingToolHandler>());
   
   // Skills
   mToolRegistry->Register(std::make_unique<LoadSkillToolHandler>(mSkillManager));
}

// ---------------------------------------------------------------------------
// Session Management
// ---------------------------------------------------------------------------

void Service::NewSession(const QString& aTitle)
{
   if (!mSessionManager) return;
   
   // Save current before switching? (Not strict, Task holds state)
   // Actually Task::GetHistory() is current.
   if (!mSessionManager->CurrentSessionId().isEmpty() && mTask && !mTask->GetHistory().isEmpty()) {
      mSessionManager->SaveSession(mSessionManager->CurrentSessionId(), mTask->GetHistory());
   }
   
   auto info = mSessionManager->CreateSession(aTitle);
   mClient->SetSessionId(info.id);
   mLlmTitleGenerated = false;
   if (mTask) {
      mTask->ClearHistory();
   }
   emit HistoryChanged();
   // SessionListChanged emitted by Manager
}

void Service::LoadSession(const QString& aSessionId)
{
   if (!mSessionManager) return;

   // Save current
   if (!mSessionManager->CurrentSessionId().isEmpty() && mTask && !mTask->GetHistory().isEmpty()) {
      mSessionManager->SaveSession(mSessionManager->CurrentSessionId(), mTask->GetHistory());
   }

   SessionData data = mSessionManager->LoadSession(aSessionId);
   if (data.info.id.isEmpty()) {
      emit TaskError("Failed to load session: " + aSessionId);
      return;
   }
   
   mSessionManager->SetCurrentSessionId(aSessionId);
   mClient->SetSessionId(aSessionId);
   mLlmTitleGenerated = true; // Loaded session already has a title
   if (mTask) {
      mTask->SetHistory(data.history);
   }
   emit HistoryChanged();
}

void Service::DeleteSession(const QString& aSessionId)
{
   if (!mSessionManager) return;
   mSessionManager->DeleteSession(aSessionId);
   if (mSessionManager->CurrentSessionId() == aSessionId) {
      NewSession();
   }
}

void Service::RenameSession(const QString& aSessionId, const QString& aNewTitle)
{
   if (!mSessionManager) return;
   mSessionManager->RenameSession(aSessionId, aNewTitle);
}

QList<SessionInfo> Service::ListSessions()
{
   return mSessionManager ? mSessionManager->ListSessions() : QList<SessionInfo>();
}

QString Service::CurrentSessionId() const
{
   return mSessionManager ? mSessionManager->CurrentSessionId() : QString();
}

QString Service::CurrentSessionTitle()
{
   return mSessionManager ? mSessionManager->SessionTitle(CurrentSessionId()) : QString();
}

const QList<ChatMessage>& Service::GetHistory() const
{
   static QList<ChatMessage> empty;
   return mTask ? mTask->GetHistory() : empty;
}

// ---------------------------------------------------------------------------
// Chat Interaction
// ---------------------------------------------------------------------------

void Service::SendUserMessage(const QString& aText)
{
   if (!mTask) return;

   // Check if this is the 2nd user message
   int userMsgCount = 0;
   for (const auto& msg : mTask->GetHistory()) {
       if (msg.mRole == MessageRole::User) userMsgCount++;
   }

   if (userMsgCount == 1 && !mLlmTitleGenerated && mSessionManager) {
       // This is the 2nd message and LLM title not yet generated.
       // Generate title using LLM first, then start the task.
       
       if (mTitleClient) {
           mTitleClient->CancelRequest();
           mTitleClient->deleteLater();
       }
       mTitleClient = new Client(mPrefObjectPtr, this);
       
       QString prompt = QStringLiteral("请根据以下对话内容，总结一个简短的标题（30个字符以内）。不要包含任何引号、前缀或解释，只输出标题本身。\n\n");
       
       for (const auto& msg : mTask->GetHistory()) {
           if (msg.mRole == MessageRole::User) {
               prompt += QStringLiteral("User: ") + msg.mContent + QStringLiteral("\n");
           }
       }
       prompt += QStringLiteral("User: ") + aText + QStringLiteral("\n");

       QList<ChatMessage> messages;
       ChatMessage msg;
       msg.mRole = MessageRole::User;
       msg.mContent = prompt;
       messages.append(msg);

       mTitleBuffer.clear();
       
       connect(mTitleClient, &Client::ResponseChunkReceived, this, [this](const QString& chunk) {
           mTitleBuffer += chunk;
       });

       connect(mTitleClient, &Client::ResponseCompleted, this, [this, aText]() {
           QString title = mTitleBuffer.trimmed();
           mTitleBuffer.clear();

           // Strip <think>...</think> blocks (reasoning models expose their chain-of-thought)
           static const QRegularExpression thinkRx(
               QStringLiteral("<think>.*?</think>"),
               QRegularExpression::DotMatchesEverythingOption | QRegularExpression::CaseInsensitiveOption);
           title.remove(thinkRx);
           title = title.trimmed();

           if (title.startsWith('"') && title.endsWith('"') && title.length() >= 2) {
               title = title.mid(1, title.length() - 2);
           }
           if (title.startsWith(QStringLiteral("**")) && title.endsWith(QStringLiteral("**")) && title.length() >= 4) {
               title = title.mid(2, title.length() - 4);
           }
           if (title.length() > 30) {
               title = title.left(27) + QStringLiteral("...");
           }
           
           if (mSessionManager && !title.isEmpty()) {
               mSessionManager->RenameSession(CurrentSessionId(), title);
               mLlmTitleGenerated = true;
           }
           
           if (mTitleClient) {
               mTitleClient->deleteLater();
               mTitleClient = nullptr;
           }
           
           // Now start the actual task
           mClient->MarkNewTrace();
           mTask->Start(aText);
       });

       connect(mTitleClient, &Client::ErrorOccurred, this, [this, aText](const QString& /*error*/) {
           mTitleBuffer.clear();
           
           // Fallback to local generation
           if (mSessionManager) {
               QList<ChatMessage> tempHistory = mTask->GetHistory();
               ChatMessage newMsg;
               newMsg.mRole = MessageRole::User;
               newMsg.mContent = aText;
               tempHistory.append(newMsg);
               QString title = SessionManager::GenerateTitle(tempHistory);
               mSessionManager->RenameSession(CurrentSessionId(), title);
           }
           
           if (mTitleClient) {
               mTitleClient->deleteLater();
               mTitleClient = nullptr;
           }
           
           // Now start the actual task
           mClient->MarkNewTrace();
           mTask->Start(aText);
       });

       // Emit TaskStarted so the UI updates to "Stop" button
       emit TaskStarted();
       mTitleClient->SendChatRequest(messages);
       return;
   }

   // Normal flow
   mClient->MarkNewTrace();
   mTask->Start(aText);
}

void Service::AbortTask()
{
   if (mTitleClient) {
       mTitleClient->CancelRequest();
       mTitleClient->deleteLater();
       mTitleClient = nullptr;
   }
   if (!mTask) return;
   mTask->Abort();
   emit TaskAborted();
}

// ---------------------------------------------------------------------------
// Tool Interaction
// ---------------------------------------------------------------------------

void Service::ApproveTool(const QString& aToolCallId, const QString& aFeedback)
{
   if (!mTask) return;
   mTask->HandleApproval(aToolCallId, true, aFeedback);
}

void Service::RejectTool(const QString& aToolCallId, const QString& aFeedback)
{
   if (!mTask) return;
   mTask->HandleApproval(aToolCallId, false, aFeedback);
}

void Service::ResolveApprovalWithResult(const QString& aToolCallId, bool aApproved,
                                        const ToolResult& aResult)
{
   if (!mToolExecutor) return;
   mToolExecutor->ResolveApprovalWithResult(aToolCallId, aApproved, aResult);
}

InlineReviewStatus Service::BeginInlineReview(const QString& aFilePath,
                                              const QString& aOriginalContent,
                                              const QString& aProposedContent,
                                              InlineReviewSummary& aSummary,
                                              QString& aError)
{
   aSummary = InlineReviewSummary{};

   if (aFilePath.isEmpty())
   {
      aError = QStringLiteral("Missing file path.");
      return InlineReviewStatus::Failed;
   }

   // If nothing changed, auto-accept silently
   if (aOriginalContent == aProposedContent)
   {
      return InlineReviewStatus::AutoAccepted;
   }

   // --- Compute diff for highlighting (proposed content is already on disk) ---
   auto oldLines = UtStringUtil::Split(aOriginalContent.toStdString(), '\n');
   auto newLines = UtStringUtil::Split(aProposedContent.toStdString(), '\n');
   auto diffs = UtSequenceDiffLines::DiffLarge(oldLines, newLines, std::equal_to<std::string>());

   QList<AiLineHighlight> highlights;
   QMap<int, QColor> markers;
   int addedCount   = 0;
   int removedCount = 0;
   int firstChangeLine = -1;

   const QColor greenBg(46, 160, 67, 60);
   const QColor greenMarker(46, 160, 67, 200);

   // We only highlight ADDED lines in green on the proposed (current) file.
   // Removed lines are not shown (they no longer exist on disk).
   int currentLine = 0;  // 0-based line in proposed content
   for (const auto& section : diffs)
   {
      if (section.mSectionType == UtSequenceDiffSection::cSAME)
      {
         const int count = static_cast<int>(section.mAfterRange.second - section.mAfterRange.first);
         currentLine += count;
      }
      else if (section.mSectionType == UtSequenceDiffSection::cREMOVED)
      {
         const int count = static_cast<int>(section.mBeforeRange.second - section.mBeforeRange.first);
         removedCount += count;
         // No lines to highlight — they are absent from the proposed content
      }
      else if (section.mSectionType == UtSequenceDiffSection::cADDED)
      {
         const int startLine = currentLine;
         const int count = static_cast<int>(section.mAfterRange.second - section.mAfterRange.first);
         for (int i = 0; i < count; ++i)
         {
            markers.insert(currentLine + i, greenMarker);
         }
         const int endLine = currentLine + count - 1;
         addedCount += count;

         AiLineHighlight hl;
         hl.startLine = startLine;
         hl.endLine   = endLine;
         hl.color     = greenBg;
         highlights.append(hl);

         if (firstChangeLine < 0) firstChangeLine = startLine;
         currentLine += count;
      }
   }

   // --- Store state (file already has proposed content on disk) ---
   InlineChange change;
   change.filePath        = aFilePath;
   change.originalContent = aOriginalContent;
   change.proposedContent = aProposedContent;
   change.addedLines      = addedCount;
   change.removedLines    = removedCount;
   mPendingInlineChanges.insert(aFilePath, change);

   // --- Open the file and scroll to first change ---
   EditorBridge::OpenFileAtLine(aFilePath,
      firstChangeLine >= 0 ? firstChangeLine + 1 : 1);

   // Apply decorations after editor loads
   QTimer::singleShot(150, this, [this, aFilePath, highlights, markers]() {
      EditorBridge::SetAiChangeDecorations(aFilePath, highlights, markers);
   });

   aSummary.filePath     = aFilePath;
   aSummary.addedLines   = addedCount;
   aSummary.removedLines = removedCount;
   return InlineReviewStatus::Ready;
}

InlineReviewOutcome Service::AcceptInlineReview(const QString& aFilePath)
{
   InlineReviewOutcome outcome;
   auto it = mPendingInlineChanges.find(aFilePath);
   if (it == mPendingInlineChanges.end())
   {
      outcome.success = false;
      outcome.message = QStringLiteral("No pending inline review for %1.").arg(aFilePath);
      return outcome;
   }

   const InlineChange change = it.value();
   mPendingInlineChanges.erase(it);

   // Clear editor decorations — file already has the proposed content on disk
   EditorBridge::ClearAiChangeDecorations(change.filePath);

   outcome.summary.filePath     = change.filePath;
   outcome.summary.addedLines   = change.addedLines;
   outcome.summary.removedLines = change.removedLines;
   outcome.success = true;
   outcome.message = QStringLiteral("Changes accepted for %1 (+%2 -%3)")
                        .arg(change.filePath)
                        .arg(change.addedLines)
                        .arg(change.removedLines);
   return outcome;
}

InlineReviewOutcome Service::RejectInlineReview(const QString& aFilePath)
{
   InlineReviewOutcome outcome;
   auto it = mPendingInlineChanges.find(aFilePath);
   if (it == mPendingInlineChanges.end())
   {
      outcome.success = false;
      outcome.message = QStringLiteral("No pending inline review for %1.").arg(aFilePath);
      return outcome;
   }

   const InlineChange change = it.value();
   mPendingInlineChanges.erase(it);

   // Clear editor decorations
   EditorBridge::ClearAiChangeDecorations(change.filePath);

   // Restore original content (revert the file)
   EditorBridge::WriteFile(change.filePath, change.originalContent);

   outcome.summary.filePath     = change.filePath;
   outcome.summary.addedLines   = change.addedLines;
   outcome.summary.removedLines = change.removedLines;
   outcome.success = false;
   outcome.message = QStringLiteral("User rejected changes to %1").arg(change.filePath);
   return outcome;
}

bool Service::HasPendingInlineReview(const QString& aFilePath) const
{
   return mPendingInlineChanges.contains(aFilePath);
}

void Service::ClearAllInlineReviews()
{
   for (auto it = mPendingInlineChanges.begin(); it != mPendingInlineChanges.end(); ++it)
   {
      EditorBridge::ClearAiChangeDecorations(it.value().filePath);
      EditorBridge::WriteFile(it.value().filePath, it.value().originalContent);
   }
   mPendingInlineChanges.clear();
}

void Service::ForgetInlineReview(const QString& aFilePath)
{
   mPendingInlineChanges.remove(aFilePath);
}

// ---------------------------------------------------------------------------
// Configuration
// ---------------------------------------------------------------------------

void Service::ReloadSkills()
{
   if (!mSkillManager) return;
   mSkillManager->Rediscover(); // Uses last project paths
   if (mPromptEngine) {
      mPromptEngine->SetSkillCatalogSummary(mSkillManager->BuildCatalogSummary());
   }
   emit DebugMessage("Skills reloaded.");
}

void Service::SaveSkillToggles()
{
   // Toggles are saved in QSettings often managed by DockWidget or Prefs
   // Ideally SkillManager should handle this, or Service.
   // For now let's expose it or let SkillManager handle it if upgraded.
   // (SkillManager handles runtime state, but persistence was in DockWidget)
   // We will migrate persistence logic later or assume DockWidget calls SkillManager methods if needed.
}

void Service::RestoreSkillToggles()
{
   // See above.
}

void Service::SetModel(const QString& aModel)
{
   if (mPrefObjectPtr) {
     mPrefObjectPtr->SetModel(aModel);
   }
}

// ---------------------------------------------------------------------------
// Slots / Signal Handlers
// ---------------------------------------------------------------------------

void Service::OnClientError(const QString& aError)
{
   if (mTask && mTask->GetState() != Task::State::Idle) {
      return;
   }
   emit TaskError(aError);
}

void Service::OnModelsFetched(const QStringList& aModels)
{
   emit AvailableModelsChanged(aModels);
}

void Service::OnTaskAssistantChunk(const QString& aChunk)
{
   emit AssistantChunkReceived(aChunk);
   
   // Auto-save session on chunks? maybe too frequent.
   // Save on TaskCompleted is better.
}

void Service::OnTaskAssistantMessage(const QString& aMessage)
{
   emit AssistantMessageUpdated(aMessage);
}

void Service::OnTaskToolFinished(const QString& aToolName, const ToolResult& aResult)
{
   // If the tool modified a file, trigger post-hoc inline review
   if (aResult.success && !aResult.modifiedFilePath.isEmpty())
   {
      InlineReviewSummary summary;
      QString error;
      InlineReviewStatus status = BeginInlineReview(
         aResult.modifiedFilePath,
         aResult.preChangeContent,
         EditorBridge::ReadFile(aResult.modifiedFilePath),
         summary,
         error);
      if (status == InlineReviewStatus::Ready)
      {
         emit InlineReviewReady(aResult.modifiedFilePath, summary);
      }
   }
   emit ToolFinished(aToolName, aResult);
}

void Service::OnTaskApprovalRequired(const QString& aToolCallId, const QString& aToolName,
                                     const QString& aDiffPreview, const QJsonObject& aParams)
{
   emit ApprovalRequired(aToolCallId, aToolName, aDiffPreview, aParams);
}

void Service::OnTaskCompleted(const QString& aSummary)
{
   // Auto-save session state
   if (mSessionManager && mTask) {
      mSessionManager->SaveSession(CurrentSessionId(), mTask->GetHistory());
   }
   emit TaskFinished(aSummary);
}

void Service::OnPrefsConfigChanged()
{
   if (mPromptEngine && mPrefObjectPtr) {
      mPromptEngine->SetCustomInstructions(mPrefObjectPtr->GetCustomInstructions());
   }
   if (mTask && mPrefObjectPtr) {
      mTask->SetMaxIterations(mPrefObjectPtr->GetMaxIterations());
   }
   if (mPathAccess) {
      mPathAccess->Refresh();
   }
   // Update context window configuration
   if (mContextManager && mPrefObjectPtr)
   {
      mContextManager->SetContextWindow(mPrefObjectPtr->GetContextWindowSize());
      mContextManager->SetAutoCondenseEnabled(mPrefObjectPtr->GetAutoCondenseEnabled());
      mContextManager->SetAutoCondenseThreshold(mPrefObjectPtr->GetAutoCondenseThreshold());
   }
}

// ---------------------------------------------------------------------------
// Conversation Debug Logging
// ---------------------------------------------------------------------------

void Service::OnConversationSnapshot(const QList<ChatMessage>& aMessages, int aLoopCount)
{
   if (!mPrefObjectPtr || !mPrefObjectPtr->IsDebugEnabled()) {
      return;
   }
   LogConversationToFile(aMessages, aLoopCount);
}

void Service::LogConversationToFile(const QList<ChatMessage>& aMessages, int aLoopCount)
{
   const QString workspaceRoot = ProjectBridge::GetWorkspaceRoot();
   if (workspaceRoot.isEmpty()) {
      return;
   }

   const QString logDir = workspaceRoot + QStringLiteral("/.aichat/logs");
   QDir().mkpath(logDir);

   const QString logPath = logDir + QStringLiteral("/conversation_log.txt");

   // Check if the log file exceeds 5MB, and if so, rename it to start a new one
   QFileInfo fileInfo(logPath);
   if (fileInfo.exists() && fileInfo.size() > 5 * 1024 * 1024) {
      const QString newName = logDir + QStringLiteral("/conversation_log_%1.txt")
         .arg(QDateTime::currentDateTime().toString(QStringLiteral("yyyyMMdd_HHmmss")));
      QFile::rename(logPath, newName);
   }

   QFile file(logPath);
   if (!file.open(QIODevice::Append | QIODevice::Text)) {
      emit DebugMessage(QStringLiteral("Failed to open conversation log: %1").arg(logPath));
      return;
   }

   QTextStream out(&file);
   out.setCodec("UTF-8");

   const QString timestamp = QDateTime::currentDateTime().toString(Qt::ISODate);
   const QString sessionId = CurrentSessionId();

   out << QStringLiteral("\n")
       << QStringLiteral("================================================================\n")
       << QStringLiteral("[%1] Session: %2  |  Loop: %3  |  Messages: %4\n")
            .arg(timestamp, sessionId)
            .arg(aLoopCount)
            .arg(aMessages.size())
       << QStringLiteral("================================================================\n");

   for (int i = 0; i < aMessages.size(); ++i)
   {
      const ChatMessage& msg = aMessages[i];
      QString roleTag;
      switch (msg.mRole)
      {
      case MessageRole::System:    roleTag = QStringLiteral("System");    break;
      case MessageRole::User:      roleTag = QStringLiteral("User");      break;
      case MessageRole::Assistant: roleTag = QStringLiteral("Assistant"); break;
      case MessageRole::Tool:      roleTag = QStringLiteral("Tool");      break;
      }

      out << QStringLiteral("[%1] %2").arg(timestamp, roleTag);

      // For tool messages, show tool name and call ID
      if (msg.mRole == MessageRole::Tool) {
         out << QStringLiteral(" (%1, id=%2)").arg(msg.mToolName, msg.mToolCallId);
      }

      out << QStringLiteral(":\n");

      // Output content as-is, preserving original information
      out << msg.mContent << QStringLiteral("\n");

      // Show tool calls if the assistant requested any
      if (msg.mRole == MessageRole::Assistant && !msg.mToolCalls.isEmpty())
      {
         out << QStringLiteral("  [Tool Calls: ");
         for (int t = 0; t < msg.mToolCalls.size(); ++t)
         {
            if (t > 0) out << QStringLiteral(", ");
            out << msg.mToolCalls[t].functionName
                << QStringLiteral("(") << msg.mToolCalls[t].arguments.size()
                << QStringLiteral(" keys)");
         }
         out << QStringLiteral("]\n");
      }

      out << QStringLiteral("------------------------\n");
   }

   out.flush();
   file.close();

   emit DebugMessage(QStringLiteral("Conversation snapshot logged: %1 messages → %2")
                        .arg(aMessages.size())
                        .arg(logPath));
}

} // namespace AiChat
