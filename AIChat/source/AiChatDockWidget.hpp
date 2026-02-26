// -----------------------------------------------------------------------------
// File: AiChatDockWidget.hpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#ifndef AICHAT_DOCK_WIDGET_HPP
#define AICHAT_DOCK_WIDGET_HPP

#include <QList>
#include <QJsonObject>
#include <QMap>
#include <QSet>
#include <QString>
#include <QUrl>

#include "WkfDockWidget.hpp"
#include "tools/AiChatToolTypes.hpp"
#include "AiChatClient.hpp"
#include "core/AiChatSessionManager.hpp"

class QLabel;
class QComboBox;
class QPlainTextEdit;
class QPushButton;
class QScrollArea;
class QTextBrowser;
class QVBoxLayout;
class QResizeEvent;
class QDialog;
class QTimer;
class QGraphicsOpacityEffect;
class QListWidget;
class QListWidgetItem;
class QVariantAnimation;

namespace AiChat
{
class InlineReviewBar;
class PrefObject;
class Service;
struct InlineReviewSummary;

class DockWidget : public wkf::DockWidget
{
   Q_OBJECT
public:
   explicit DockWidget(PrefObject* aPrefObject, QMainWindow* aParent);
   ~DockWidget() override;

protected:
   void OverrideTitleBar(QWidget* aWidget) override;
   void resizeEvent(QResizeEvent* aEvent) override;
   bool eventFilter(QObject* aObject, QEvent* aEvent) override;

private slots:
   // --- Send / user input ---
   void OnSendClicked();

   // --- Service signals ---
   void OnTaskChunkReceived(const QString& aChunk);
   void OnTaskMessageUpdated(const QString& aMessage);
   void OnTaskFinished(const QString& aSummary);
   void OnTaskError(const QString& aError);
   void OnToolCallStreaming(const QString& aFunctionName, int aArgBytes);
   
   void OnToolStarted(const QString& aToolName);
   void OnToolFinished(const QString& aToolName, const ToolResult& aResult);
   void OnApprovalRequired(const QString& aToolCallId, const QString& aToolName,
                           const QString& aDiffPreview, const QJsonObject& aParams);

   // --- Approval panel ---
   void OnApproveClicked();
   void OnRejectClicked();

   // --- Debug window ---
   void OnDebugClicked();

   // --- Inline editor review ---
   void OnInlineReviewReady(const QString& aFilePath, const InlineReviewSummary& aSummary);
   void OnInlineAccepted();
   void OnInlineRejected();

   // --- Bubble link handling ---
   void OnBubbleAnchorClicked(const QUrl& aUrl);

private:
   // --- Initialization ---
   void SetupUi();
   void UpdateTitle();

   // --- Session management ---
   void OnNewSessionClicked();
   void OnSessionListToggled();
   void OnSessionSelected(QListWidgetItem* aItem);
   void OnSessionDeleteRequested(const QString& aSessionId);
   void ClearConversationUi();
   void RestoreSessionMessages(const QList<ChatMessage>& aHistory);
   void PopulateSessionList();
   void UpdateSessionTitle(const QString& aTitle);

   // --- Messaging helpers ---
   void AppendMessage(const QString& aRole, const QString& aContent);
   void AppendAssistantPlaceholder();
   void UpdateAssistantMessage();
   void AppendToAssistantBubble(const QString& aHtmlSnippet);
   void FinalizeAssistantBubble();
   QTextBrowser* AddMessageWidget(const QString& aRole, const QString& aContent);
   /// Insert a permanent context-event divider into the message list.
   /// @param aLabel   Text shown inside the pill badge.
   /// @param aBadgeColor  CSS colour (hex) for the border and text tint.
   void InsertContextDivider(const QString& aLabel,
                              const QString& aBadgeColor = QStringLiteral("#f0b429"));
   void UpdateMessageSizes();
   QString BuildToolInlineHtml(const QString& aToolName, const QString& aStatus,
                               bool aSuccess, bool aRunning = false) const;
   void StartTypingIndicator();
   void StopTypingIndicator();
   void UpdateTypingIndicator();
   QString BuildTypingHtml(int aDotCount) const;

   // --- Run state + debug helpers ---
   void UpdateSendButtonState(bool aRunning);
   void AppendDebug(const QString& aMessage);
   void EnsureDebugWindow();
   void StartStatusAnimation(const QString& aBaseText);
   void StopStatusAnimation();
   
private:
   PrefObject* mPrefObjectPtr{nullptr};
   Service*    mService{nullptr}; // The Agent Controller

   void UpdateRunningToolSpinner();
   int UiFontSize() const;
   int UiSmallFontSize() const;
   int UiCodeFontSize() const;
   QString StatusLabelStyle(const QString& aColor) const;
   QString BuildBubbleStyle(const QString& aRole) const;
   void ApplyUiFontSize();

   // --- Think tag streaming helpers ---
   /// Process a raw chunk to detect <think>/<thinking> tags.
   /// Returns the portion that should be appended to mPendingAssistantText.
   /// Think content is accumulated in mThinkText and shown as a scrolling indicator.
   QString ProcessThinkTags(const QString& aChunk);
   void StartThinkAnimation();
   void StopThinkAnimation();
   void UpdateThinkScroll();
   QString BuildDeepThinkDoneHtml(int aBlockIndex,
                                   const QStringList& aThinkTexts,
                                   const QSet<int>& aExpandedBlocks) const;
   void    RebuildThinkBlock(int aBlockIndex);
   void    RebuildFinalizedThinkBlock(QTextBrowser* aBubble, int aBlockIndex);

   // --- Scroll helpers ---
   bool IsScrolledToBottom() const;
   int  AutoScrollEpsilon() const;
   void ScrollToBottomIfNeeded();

   // --- Rendering ---
   QString RenderMarkdown(const QString& aText) const;
   QString RenderInlineMarkdown(const QString& aText) const;

   // --- Approval UI ---
   void ShowApprovalPanel(const QString& aToolName, const QString& aDiff);
   void HideApprovalPanel();
   void AnimateApprovalShow();
   void AnimateApprovalHide();
   QString FormatPreviewHtml(const QString& aToolName, const QString& aText) const;

   // ========== Member data ==========

   // --- UI widgets ---
   QScrollArea*   mScrollArea;
   QWidget*       mMessagesContainer;
   QVBoxLayout*   mMessagesLayout;
   QPlainTextEdit* mInput;
   QComboBox*     mModelComboBox;  // New model selector
   QPushButton*   mSendButton;
   QPushButton*   mSettingsButton; // Settings button
   QLabel*        mStatusLabel;
   QTimer*        mStatusTimer{nullptr};
   QString        mStatusBaseText;
   int            mStatusTick{0};
   QTimer*        mTypingTimer{nullptr};
   int            mTypingTick{0};
   bool           mTypingActive{false};    ///< Whether the typing indicator is currently showing

   // Think-block streaming state
   bool           mInThinkBlock{false};   ///< Currently inside <think>/<thinking> tags
   QString        mThinkText;             ///< Accumulated think content for scrolling display
   QString        mThinkTagRemainder;     ///< Partial think tag carried across chunks
   QTimer*        mThinkScrollTimer{nullptr};
   int            mThinkScrollOffset{0};
   bool           mThinkExpanded{false};  ///< Whether think content is expanded (live indicator)
   QString        mThinkIndicatorHtml;    ///< Cached think indicator HTML
   QStringList    mThinkBlockTexts;       ///< Stored think content for each frozen block
   QSet<int>      mExpandedThinkBlocks;   ///< Which frozen think blocks are expanded

   // Tool-call streaming progress state
   qint64         mLastStreamIndicatorMs{0}; ///< Throttle bubble updates during tool_call streaming

   // Session bar
   QWidget*     mSessionBar{nullptr};
   QLabel*      mSessionTitle{nullptr};
   QPushButton* mNewSessionBtn{nullptr};
   QPushButton* mSessionListBtn{nullptr};
   QWidget*     mSessionListPanel{nullptr};
   QListWidget* mSessionListWidget{nullptr};

   // Approval panel
   QWidget*     mApprovalPanel{nullptr};
   QTextBrowser* mDiffPreview{nullptr};
   QLabel*      mApprovalLabel{nullptr};
   QLabel*      mApprovalIcon{nullptr};
   QPushButton* mApproveButton{nullptr};
   QPushButton* mRejectButton{nullptr};
   QPlainTextEdit* mAnswerInput{nullptr};    ///< Text input for ask_question
   QGraphicsOpacityEffect* mApprovalOpacity{nullptr};
   QVariantAnimation*      mApprovalAnim{nullptr};
   QString      mPendingApprovalToolCallId;
   QString      mPendingApprovalToolName;    ///< Tool name of the pending approval

   // Conversation state
   QString       mPendingAssistantText;
   QString       mAssistantBubbleHtml;   ///< Accumulated HTML for the current AI turn
   QString       mFrozenBubbleHtml;      ///< Frozen HTML from previous agentic loop turns (text + tool indicators)
   QTextBrowser* mStreamingMessage{nullptr};
   bool          mAwaitingAssistant{false};
   bool          mIsRunning{false};
   int           mMessageCount{0};
   bool          mAutoScrollEnabled{true};     ///< Auto-scroll only if user stayed at bottom
   bool          mAutoScrollInProgress{false}; ///< Suppress user intent updates during programmatic scrolls

   // Context management UI
   QLabel*       mTokenUsageLabel{nullptr};

   // Inline review bars (keyed by resolved file path)
   QMap<QString, InlineReviewBar*> mReviewBars;

   // Debug window
   QDialog*        mDebugWindow{nullptr};
   QPlainTextEdit* mDebugOutput{nullptr};
};

} // namespace AiChat

#endif // AICHAT_DOCK_WIDGET_HPP
