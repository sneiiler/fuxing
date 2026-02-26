// -----------------------------------------------------------------------------
// File: AiChatDockWidget.cpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-12
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#include "AiChatDockWidget.hpp"
#include "AiChatPrefObject.hpp"
#include "core/AiChatService.hpp"
#include "AiChatInlineReviewBar.hpp"
#include "bridge/AiChatEditorBridge.hpp"
#include "WkfEnvironment.hpp"
#include "ProjectWorkspace.hpp"
#include "WkfMainWindow.hpp"

#include <QFrame>
#include <QComboBox>
#include <QDialog>
#include <QDateTime>
#include <QHBoxLayout>
#include <QLabel>
#include <QLocale>
#include <QMainWindow>
#include <QPlainTextEdit>
#include <QPushButton>
#include <QStringList>
#include <QEvent>
#include <QKeyEvent>
#include <QFileInfo>
#include <QRegularExpression>
#include <QScrollArea>
#include <QScrollBar>
#include <QTextBrowser>
#include <QListWidget>
#include <QResizeEvent>
#include <QTextOption>
#include <QTimer>
#include <QGraphicsOpacityEffect>
#include <QBuffer>
#include <QDesktopServices>
#include <QFile>
#include <QHash>
#include <QIcon>
#include <QMovie>
#include <QPixmap>
#include <QUrl>
#include <QVariantAnimation>
#include <QVBoxLayout>

namespace AiChat
{

// ============================================================================
// Image Resource Helpers
// ============================================================================

/// Resolve the plugin source directory at runtime using __FILE__.
static QString PluginSourceDir()
{
   static const QString sDir = QFileInfo(QStringLiteral(__FILE__)).absolutePath();
   return sDir;
}

/// Load an image file from the plugin source directory and return as base64 data URI.
/// The result is cached so the file is read only once.
static QString ImageDataUri(const QString& aFileName, const QString& aMimeType)
{
   static QHash<QString, QString> sCache;
   auto it = sCache.constFind(aFileName);
   if (it != sCache.constEnd()) return *it;

   const QString path = PluginSourceDir() + QStringLiteral("/") + aFileName;
   QFile file(path);
   if (!file.open(QIODevice::ReadOnly)) {
      sCache.insert(aFileName, QString());
      return QString();
   }
   const QByteArray raw = file.readAll();
   const QString uri = QStringLiteral("data:%1;base64,%2")
      .arg(aMimeType, QString::fromLatin1(raw.toBase64()));
   sCache.insert(aFileName, uri);
   return uri;
}

static QString CogwheelPngUri()
{
   return ImageDataUri(QStringLiteral("cogwheel.png"), QStringLiteral("image/png"));
}

static QIcon CogwheelIcon()
{
   static QIcon sIcon;
   if (sIcon.isNull()) {
      const QString path = PluginSourceDir() + QStringLiteral("/cogwheel.png");
      sIcon = QIcon(path);
   }
   return sIcon;
}

// ============================================================================
// Construction / Destruction
// ============================================================================

DockWidget::DockWidget(PrefObject* aPrefObject, QMainWindow* aParent)
   : wkf::DockWidget("AiChat", aParent, Qt::WindowFlags{}, false)
   , mPrefObjectPtr(aPrefObject)
   , mService(new Service(aPrefObject, this))
   , mScrollArea(nullptr)
   , mMessagesContainer(nullptr)
   , mMessagesLayout(nullptr)
   , mInput(nullptr)
   , mSendButton(nullptr)
   , mSettingsButton(nullptr)
   , mStatusLabel(nullptr)
{
   SetupUi();
   mService->Initialize();
   UpdateTitle();

   // --- Service signals ---
   
   // Chat & Task Output
   connect(mService, &Service::AssistantChunkReceived, this, &DockWidget::OnTaskChunkReceived);
   connect(mService, &Service::AssistantMessageUpdated, this, &DockWidget::OnTaskMessageUpdated);
   connect(mService, &Service::TaskFinished, this, &DockWidget::OnTaskFinished);
   connect(mService, &Service::TaskError, this, &DockWidget::OnTaskError);
   connect(mService, &Service::TaskStarted, this, [this]() {
       UpdateSendButtonState(true);
       AppendDebug("Task started.");
       // Start typing indicator for agentic loop continuation
       // (first call is handled by AppendAssistantPlaceholder, so only restart
       // if we already have a streaming message = agentic loop iteration)
       if (mStreamingMessage && mAwaitingAssistant && !mTypingActive) {
          StartTypingIndicator();
       }
   });

   // Tool Activity
   connect(mService, &Service::ToolStarted, this, &DockWidget::OnToolStarted);
   connect(mService, &Service::ToolFinished, this, &DockWidget::OnToolFinished);
   connect(mService, &Service::ApprovalRequired, this, &DockWidget::OnApprovalRequired);
   connect(mService, &Service::ToolCallStreaming, this, &DockWidget::OnToolCallStreaming);

   // Inline Review (post-hoc, per-file)
   connect(mService, &Service::InlineReviewReady, this, &DockWidget::OnInlineReviewReady);

   // Context Management
   connect(mService, &Service::TokenUsageUpdated, this,
           [this](double aRatio, int aUsedTokens, int aMaxTokens) {
              if (!mTokenUsageLabel) return;
              const QString text = QStringLiteral("Token: %1 / %2 (%3%)")
                 .arg(QLocale().toString(aUsedTokens))
                 .arg(QLocale().toString(aMaxTokens))
                 .arg(static_cast<int>(aRatio * 100));
              mTokenUsageLabel->setText(text);
              mTokenUsageLabel->setVisible(true);
              const int tokenFont = qMax(9, UiFontSize() - 3);
              // Color by usage ratio
              if (aRatio >= 0.9) {
                 mTokenUsageLabel->setStyleSheet(
                    QStringLiteral("QLabel { color: #f14c4c; font-size: %1px; padding: 0 6px;"
                                   " background: transparent; font-weight: bold; }")
                       .arg(tokenFont));
              } else if (aRatio >= 0.7) {
                 mTokenUsageLabel->setStyleSheet(
                    QStringLiteral("QLabel { color: #f0b429; font-size: %1px; padding: 0 6px;"
                                   " background: transparent; }")
                       .arg(tokenFont));
              } else {
                 mTokenUsageLabel->setStyleSheet(
                    QStringLiteral("QLabel { color: #8b949e; font-size: %1px; padding: 0 6px;"
                                   " background: transparent; }")
                       .arg(tokenFont));
              }
           });
   connect(mService, &Service::ContextTruncated, this,
           [this](int aRemovedCount, int /*aRemainingCount*/) {
              InsertContextDivider(
                 QStringLiteral("\u29b8  \u4e0a\u4e0b\u6587\u5df2\u88c1\u526a  \u00b7  \u79fb\u9664 %1 \u6761")
                    .arg(aRemovedCount),
                 QStringLiteral("#f0b429"));
           });
   connect(mService, &Service::ContextCondensed, this,
           [this]() {
              InsertContextDivider(
                 QStringLiteral("\u2726  \u4e0a\u4e0b\u6587\u5df2\u63d0\u708a"),
                 QStringLiteral("#3dd27a"));
           });
   
   // Chat Mode (Simple vs Smart) routing is now handled by Service internally or unified
   // For now, we listen to Unified signals. TaskChunk works for both.

   // Debug
   connect(mService, &Service::DebugMessage, this, &DockWidget::AppendDebug);

   // Models
   connect(mService, &Service::AvailableModelsChanged, this, [this](const QStringList& aModels) {
      if (mPrefObjectPtr) {
         mPrefObjectPtr->SetAvailableModels(aModels);
      }
      if (mModelComboBox && !aModels.isEmpty()) {
         const QString current = mModelComboBox->currentText();
         mModelComboBox->blockSignals(true);
         mModelComboBox->clear();
         mModelComboBox->addItems(aModels);
         int idx = mModelComboBox->findText(current);
         if (idx >= 0) {
            mModelComboBox->setCurrentIndex(idx);
         } else {
            mModelComboBox->setCurrentText(current);
         }
         mModelComboBox->blockSignals(false);
      }
   });
   
   // Sessions
   connect(mService, &Service::HistoryChanged, this, [this]() {
       ClearConversationUi();
       RestoreSessionMessages(mService->GetHistory());
         if (mSessionListWidget && mSessionListPanel->isVisible()) {
           PopulateSessionList();
       }
       UpdateSessionTitle(mService->CurrentSessionTitle());
   });
   
   connect(mService, &Service::SessionListChanged, this, [this]() {
         if (mSessionListWidget && mSessionListPanel->isVisible()) {
           PopulateSessionList();
       }
       // Also refresh the title label in case the current session was renamed
       UpdateSessionTitle(mService->CurrentSessionTitle());
   });


   // Apply UI preferences
   if (mPrefObjectPtr) {
      connect(mPrefObjectPtr, &PrefObject::ConfigurationChanged, this, [this]() {
         ApplyUiFontSize();
         // Sync model selection
         if (mModelComboBox) {
            const QString prefModel = mPrefObjectPtr->GetModel();
            if (mModelComboBox->currentText() != prefModel) {
               mModelComboBox->blockSignals(true);
               int idx = mModelComboBox->findText(prefModel);
               if (idx >= 0) {
                  mModelComboBox->setCurrentIndex(idx);
               } else {
                  mModelComboBox->addItem(prefModel);
                  mModelComboBox->setCurrentText(prefModel);
               }
               mModelComboBox->blockSignals(false);
            }
         }
         // Sync custom instructions & max iterations passed to Service automatically via its connection
      });
      
      connect(mPrefObjectPtr, &PrefObject::OpenDebugWindowRequested, this, &DockWidget::OnDebugClicked);
      connect(mPrefObjectPtr, &PrefObject::ReloadSkillsRequested, this, [this]() {
         if (mService) {
            mService->ReloadSkills();
         }
      });
   }

   // --- Auto-reload skills when a project is opened or changed ---
   auto* workspace = wizard::ProjectWorkspace::Instance();
   if (workspace)
   {
      connect(workspace, &wizard::ProjectWorkspace::ProjectOpened,
              this, [this](wizard::Project* /*aProject*/) {
                 if (mService) {
                    mService->ReloadSkills();
                 }
              });
      connect(workspace, &wizard::ProjectWorkspace::ActiveProjectChanged,
              this, [this](wizard::Project* /*aProject*/) {
                 if (mService) {
                    mService->ReloadSkills();
                 }
              });
   }

}

DockWidget::~DockWidget() = default;

// ============================================================================
// UI Setup
// ============================================================================

void DockWidget::SetupUi()
{
   auto* container  = new QWidget(this);
   container->setObjectName("AiChatContainer");
   auto* mainLayout = new QVBoxLayout(container);
   mainLayout->setContentsMargins(0, 0, 0, 0);
   mainLayout->setSpacing(0);

   // Global dark theme with subtle gradient
   container->setStyleSheet(
      "#AiChatContainer { background: qlineargradient(x1:0, y1:0, x2:0, y2:1,"
      "  stop:0 #0d1117, stop:1 #161b22); }");

   // --- Conversation area ---
   mScrollArea = new QScrollArea(container);
   mScrollArea->setWidgetResizable(true);
   mScrollArea->setFrameShape(QFrame::NoFrame);
   mScrollArea->setHorizontalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
   mScrollArea->viewport()->installEventFilter(this);
   // Modern scrollbar
   mScrollArea->setStyleSheet(
      "QScrollArea { background: transparent; border: none; }"
      "QScrollBar:vertical { background: transparent; width: 8px; margin: 4px 2px; }"
      "QScrollBar::handle:vertical { background: rgba(255,255,255,0.15); border-radius: 4px; min-height: 40px; }"
      "QScrollBar::handle:vertical:hover { background: rgba(255,255,255,0.25); }"
      "QScrollBar::add-line:vertical, QScrollBar::sub-line:vertical { height: 0; }"
      "QScrollBar::add-page:vertical, QScrollBar::sub-page:vertical { background: none; }");

   mMessagesContainer = new QWidget(mScrollArea);
   mMessagesContainer->setStyleSheet("background: transparent;");
   mMessagesLayout = new QVBoxLayout(mMessagesContainer);
   mMessagesLayout->setContentsMargins(16, 20, 16, 20);
   mMessagesLayout->setSpacing(16);
   mMessagesLayout->addStretch(0);
   mMessagesContainer->setLayout(mMessagesLayout);
   mMessagesContainer->setSizePolicy(QSizePolicy::Expanding, QSizePolicy::Preferred);

   mScrollArea->setWidget(mMessagesContainer);

   if (auto* bar = mScrollArea->verticalScrollBar()) {
      connect(bar, &QScrollBar::valueChanged, this, [this, bar](int value) {
         if (mAutoScrollInProgress) return;
         const int epsilon = AutoScrollEpsilon();
         mAutoScrollEnabled = (bar->maximum() - value) <= epsilon;
      });
      connect(bar, &QScrollBar::rangeChanged, this, [this](int, int) {
         ScrollToBottomIfNeeded();
      });
   }

   // --- Session bar (compact header above conversation) ---
   mSessionBar = new QWidget(container);
   mSessionBar->setObjectName("SessionBar");
   mSessionBar->setFixedHeight(36);
   mSessionBar->setStyleSheet(
      "#SessionBar {"
      "  background: #161b22;"
      "  border-bottom: 1px solid #30363d;"
      "}");
   auto* sessionBarLayout = new QHBoxLayout(mSessionBar);
   sessionBarLayout->setContentsMargins(12, 0, 12, 0);
   sessionBarLayout->setSpacing(8);

   mSessionListBtn = new QPushButton(mSessionBar);
   mSessionListBtn->setText(QStringLiteral("\u2261")); // ≡ hamburger
   mSessionListBtn->setFixedSize(28, 28);
   mSessionListBtn->setCursor(Qt::PointingHandCursor);
   mSessionListBtn->setToolTip("Toggle session history");
   mSessionListBtn->setStyleSheet(
      "QPushButton {"
      "  background: transparent;"
      "  border: none;"
      "  border-radius: 6px;"
      "  color: #8b949e;"
      "  font-size: 16px;"
      "  font-weight: bold;"
      "}"
      "QPushButton:hover {"
      "  background: rgba(139,148,158,0.15);"
      "  color: #e6edf3;"
      "}");
   sessionBarLayout->addWidget(mSessionListBtn);

   mSessionTitle = new QLabel(QStringLiteral("New Chat"), mSessionBar);
   mSessionTitle->setStyleSheet(
      "QLabel { color: #c9d1d9; font-size: 13px; font-weight: 600;"
      "  letter-spacing: 0.3px; background: transparent; }");
   sessionBarLayout->addWidget(mSessionTitle, 1);

   // --- Token usage label (session bar) ---
   mTokenUsageLabel = new QLabel(mSessionBar);
   mTokenUsageLabel->setFixedHeight(18);
   mTokenUsageLabel->setStyleSheet(
      "QLabel { color: #8b949e; font-size: 11px; padding: 0 6px;"
      " background: transparent; }");
   mTokenUsageLabel->setText(QStringLiteral("Token: --"));
   mTokenUsageLabel->setVisible(true);
   sessionBarLayout->addWidget(mTokenUsageLabel);

   mNewSessionBtn = new QPushButton(mSessionBar);
   mNewSessionBtn->setText("+");
   mNewSessionBtn->setFixedSize(28, 28);
   mNewSessionBtn->setCursor(Qt::PointingHandCursor);
   mNewSessionBtn->setToolTip("New Chat (Ctrl+N)");
   mNewSessionBtn->setShortcut(QKeySequence("Ctrl+N"));
   mNewSessionBtn->setStyleSheet(
      "QPushButton {"
      "  background: rgba(56,139,253,0.1);"
      "  border: 1px solid rgba(56,139,253,0.4);"
      "  border-radius: 6px;"
      "  color: #58a6ff;"
      "  font-size: 16px;"
      "  font-weight: bold;"
      "}"
      "QPushButton:hover {"
      "  background: rgba(56,139,253,0.25);"
      "  border: 1px solid rgba(56,139,253,0.6);"
      "}");
   sessionBarLayout->addWidget(mNewSessionBtn);

   connect(mNewSessionBtn, &QPushButton::clicked, this, &DockWidget::OnNewSessionClicked);
   connect(mSessionListBtn, &QPushButton::clicked, this, &DockWidget::OnSessionListToggled);

   // --- Session list panel (hidden by default) ---
   mSessionListPanel = new QWidget(container);
   mSessionListPanel->setObjectName("SessionListPanel");
   mSessionListPanel->setStyleSheet(
      "#SessionListPanel {"
      "  background: #0d1117;"
      "  border-bottom: 1px solid #30363d;"
      "}");
   mSessionListPanel->setMaximumHeight(280);
   mSessionListPanel->setVisible(false);
   auto* sessionListLayout = new QVBoxLayout(mSessionListPanel);
   sessionListLayout->setContentsMargins(8, 8, 8, 8);
   sessionListLayout->setSpacing(0);

   mSessionListWidget = new QListWidget(mSessionListPanel);
   mSessionListWidget->setStyleSheet(
      "QListWidget {"
      "  background: transparent;"
      "  border: none;"
      "  outline: none;"
      "  padding: 0;"
      "}"
      "QListWidget::item {"
      "  background: transparent;"
      "  color: #c9d1d9;"
      "  padding: 8px 12px;"
      "  border-radius: 6px;"
      "  margin: 1px 0;"
      "}"
      "QListWidget::item:hover {"
      "  background: rgba(56, 139, 253, 0.1);"
      "}"
      "QListWidget::item:selected {"
      "  background: rgba(56, 139, 253, 0.2);"
      "  color: #e6edf3;"
      "}");
   mSessionListWidget->setHorizontalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
   sessionListLayout->addWidget(mSessionListWidget);

   connect(mSessionListWidget, &QListWidget::itemClicked,
           this, &DockWidget::OnSessionSelected);

   mainLayout->addWidget(mSessionBar);
   mainLayout->addWidget(mSessionListPanel);
   mainLayout->addWidget(mScrollArea, 1);

   // --- Approval panel (hidden by default — Modern card with slide animation) ---
   mApprovalPanel = new QWidget(container);
   mApprovalPanel->setObjectName("ApprovalPanel");
   mApprovalPanel->setStyleSheet(
      "#ApprovalPanel {"
      "  background: qlineargradient(x1:0, y1:0, x2:0, y2:1,"
      "    stop:0 #1e2233, stop:1 #171b2e);"
      "  border-top: 3px solid #6366f1;"
      "}");

   // Opacity effect for fade animation
   mApprovalOpacity = new QGraphicsOpacityEffect(mApprovalPanel);
   mApprovalOpacity->setOpacity(1.0);
   mApprovalPanel->setGraphicsEffect(mApprovalOpacity);

   auto* approvalLayout = new QVBoxLayout(mApprovalPanel);
   approvalLayout->setContentsMargins(0, 0, 0, 0);
   approvalLayout->setSpacing(0);

   // -- Header row --
   auto* headerWidget = new QWidget(mApprovalPanel);
   headerWidget->setObjectName("ApprovalHeader");
   headerWidget->setStyleSheet(
      "#ApprovalHeader {"
      "  background: rgba(99, 102, 241, 0.06);"
      "  border-bottom: 1px solid rgba(99, 102, 241, 0.12);"
      "}");
   auto* headerLayout = new QHBoxLayout(headerWidget);
   headerLayout->setContentsMargins(16, 10, 16, 10);
   headerLayout->setSpacing(10);

   mApprovalIcon = new QLabel(headerWidget);
   mApprovalIcon->setFixedSize(28, 28);
   mApprovalIcon->setAlignment(Qt::AlignCenter);
   mApprovalIcon->setStyleSheet(
      "QLabel { background: rgba(99,102,241,0.15); color: #a5b4fc;"
      " border-radius: 6px; font-size: 14px; font-weight: 700; }");
   headerLayout->addWidget(mApprovalIcon);

   mApprovalLabel = new QLabel(headerWidget);
   mApprovalLabel->setStyleSheet(
      "QLabel { color: #e2e8f0; font-weight: 600; font-size: 13px;"
      " letter-spacing: 0.4px; background: transparent; }");
   mApprovalLabel->setText("Tool requires approval");
   headerLayout->addWidget(mApprovalLabel, 1);
   approvalLayout->addWidget(headerWidget);

   // -- Body (diff preview + optional answer input) --
   auto* bodyWidget = new QWidget(mApprovalPanel);
   bodyWidget->setObjectName("ApprovalBody");
   bodyWidget->setStyleSheet("#ApprovalBody { background: transparent; }");
   auto* bodyLayout = new QVBoxLayout(bodyWidget);
   bodyLayout->setContentsMargins(16, 12, 16, 6);
   bodyLayout->setSpacing(10);

   mDiffPreview = new QTextBrowser(bodyWidget);
   mDiffPreview->setMaximumHeight(200);
   mDiffPreview->setFrameShape(QFrame::NoFrame);
   mDiffPreview->setOpenExternalLinks(false);
   mDiffPreview->setStyleSheet(
      "QTextBrowser {"
      "  background: rgba(13, 17, 23, 0.85);"
      "  color: #c9d1d9;"
      "  font-family: 'Cascadia Code', 'Fira Code', 'JetBrains Mono', Consolas, monospace;"
      "  font-size: 12px;"
      "  padding: 12px 14px;"
      "  border: 1px solid rgba(63, 68, 81, 0.4);"
      "  border-radius: 8px;"
      "  selection-background-color: rgba(99, 102, 241, 0.3);"
      "}");
   bodyLayout->addWidget(mDiffPreview);

   mAnswerInput = new QPlainTextEdit(bodyWidget);
   mAnswerInput->setPlaceholderText("Type your answer...");
   mAnswerInput->setMaximumHeight(80);
   mAnswerInput->setStyleSheet(
      "QPlainTextEdit {"
      "  background: rgba(13, 17, 23, 0.85);"
      "  color: #e2e8f0;"
      "  border: 1px solid rgba(63, 68, 81, 0.4);"
      "  border-radius: 8px;"
      "  padding: 10px 12px;"
      "  font-size: 13px;"
      "  font-family: 'Segoe UI', system-ui, sans-serif;"
      "  selection-background-color: rgba(99, 102, 241, 0.3);"
      "}"
      "QPlainTextEdit:focus {"
      "  border: 1px solid rgba(99, 102, 241, 0.6);"
      "}");
   mAnswerInput->setVisible(false);
   bodyLayout->addWidget(mAnswerInput);
   approvalLayout->addWidget(bodyWidget);

   // -- Button bar --
   auto* btnBarWidget = new QWidget(mApprovalPanel);
   btnBarWidget->setObjectName("ApprovalButtons");
   btnBarWidget->setStyleSheet("#ApprovalButtons { background: transparent; }");
   auto* approvalBtnLayout = new QHBoxLayout(btnBarWidget);
   approvalBtnLayout->setContentsMargins(16, 6, 16, 14);
   approvalBtnLayout->setSpacing(10);

   mApproveButton = new QPushButton(mApprovalPanel);
   mApproveButton->setCursor(Qt::PointingHandCursor);
   mApproveButton->setStyleSheet(
      "QPushButton {"
      "  background: qlineargradient(x1:0,y1:0,x2:0,y2:1,"
      "    stop:0 #34d399, stop:1 #10b981);"
      "  color: #022c22;"
      "  border: none;"
      "  border-radius: 8px;"
      "  padding: 9px 28px;"
      "  font-weight: 700;"
      "  font-size: 13px;"
      "  letter-spacing: 0.3px;"
      "  min-width: 100px;"
      "  min-height: 34px;"
      "}"
      "QPushButton:hover {"
      "  background: qlineargradient(x1:0,y1:0,x2:0,y2:1,"
      "    stop:0 #6ee7b7, stop:1 #34d399);"
      "}"
      "QPushButton:pressed {"
      "  background: #10b981;"
      "}"
      "QPushButton:focus {"
      "  outline: none;"
      "  border: 2px solid rgba(52,211,153,0.5);"
      "}");

   mRejectButton = new QPushButton(mApprovalPanel);
   mRejectButton->setCursor(Qt::PointingHandCursor);
   mRejectButton->setStyleSheet(
      "QPushButton {"
      "  background: rgba(239, 68, 68, 0.08);"
      "  color: #fca5a5;"
      "  border: 1px solid rgba(239, 68, 68, 0.25);"
      "  border-radius: 8px;"
      "  padding: 9px 28px;"
      "  font-weight: 600;"
      "  font-size: 13px;"
      "  letter-spacing: 0.3px;"
      "  min-width: 100px;"
      "  min-height: 34px;"
      "}"
      "QPushButton:hover {"
      "  background: rgba(239, 68, 68, 0.16);"
      "  border-color: rgba(239, 68, 68, 0.4);"
      "  color: #fecaca;"
      "}"
      "QPushButton:pressed {"
      "  background: rgba(239, 68, 68, 0.24);"
      "}"
      "QPushButton:focus {"
      "  outline: none;"
      "  border: 2px solid rgba(239,68,68,0.4);"
      "}");

   approvalBtnLayout->addStretch();
   approvalBtnLayout->addWidget(mApproveButton);
   approvalBtnLayout->addWidget(mRejectButton);
   approvalLayout->addWidget(btnBarWidget);

   mApprovalPanel->setVisible(false);
   mApprovalPanel->setMaximumHeight(0);
   mainLayout->addWidget(mApprovalPanel);

   connect(mApproveButton, &QPushButton::clicked, this, &DockWidget::OnApproveClicked);
   connect(mRejectButton, &QPushButton::clicked, this, &DockWidget::OnRejectClicked);

   // --- Status label ---
   mStatusLabel = new QLabel(container);
   mStatusLabel->setFixedHeight(24);
   mStatusLabel->setStyleSheet(
      "QLabel { color: #8b949e; font-style: italic; padding: 4px 16px;"
      " background: qlineargradient(x1:0, y1:0, x2:0, y2:1,"
      "   stop:0 #21262d, stop:1 #161b22);"
      " font-size: 12px; font-weight: 500; letter-spacing: 0.3px; }");
   mainLayout->addWidget(mStatusLabel);

   mStatusTimer = new QTimer(this);
   mStatusTimer->setInterval(450);
   connect(mStatusTimer, &QTimer::timeout, this, [this]() {
      if (mStatusBaseText.isEmpty()) {
         return;
      }
      const int dots = mStatusTick % 4;
      QString suffix;
      for (int i = 0; i < dots; ++i) {
         suffix.append('.');
      }
      mStatusLabel->setText(mStatusBaseText + suffix);
      ++mStatusTick;

      // Also animate inline running tool indicator in the bubble
      UpdateRunningToolSpinner();
   });

   mTypingTimer = new QTimer(this);
   mTypingTimer->setInterval(350);
   connect(mTypingTimer, &QTimer::timeout, this, [this]() {
      UpdateTypingIndicator();
   });

   // Think-block scrolling animation timer (used when LLM emits <think>/<thinking> tags)
   mThinkScrollTimer = new QTimer(this);
   mThinkScrollTimer->setInterval(600);   // vertical line scroll ~1.6 fps
   connect(mThinkScrollTimer, &QTimer::timeout, this, [this]() {
      UpdateThinkScroll();
   });

   // --- Input area (Modern floating card style) ---
   auto* inputContainer = new QWidget(container);
   inputContainer->setObjectName("InputContainer");
   inputContainer->setStyleSheet(
      "#InputContainer {"
      "  background: qlineargradient(x1:0, y1:0, x2:0, y2:1,"
      "    stop:0 #1c2128, stop:1 #0d1117);"
      "  border-top: 1px solid rgba(48,54,61,0.8);"
      "}");
   auto* inputLayout = new QVBoxLayout(inputContainer);
   inputLayout->setContentsMargins(16, 12, 16, 16);
   inputLayout->setSpacing(8);

   // Input Frame (Floating card with glow effect)
   auto* inputBox = new QFrame(inputContainer);
   inputBox->setObjectName("InputBox");
   inputBox->setStyleSheet(
      "#InputBox {"
      "  background: qlineargradient(x1:0, y1:0, x2:0, y2:1,"
      "    stop:0 #21262d, stop:0.5 #1c2128, stop:1 #161b22);"
      "  border: 1px solid #30363d;"
      "  border-radius: 12px;"
      "}"
      "#InputBox:focus-within {"
      "  border: 1px solid #388bfd;"
      "}");
   auto* boxLayout = new QVBoxLayout(inputBox);
   boxLayout->setContentsMargins(0, 0, 0, 0);
   boxLayout->setSpacing(0);

   // Text Edit (Modern clean look)
   mInput = new QPlainTextEdit(inputBox);
   mInput->setPlaceholderText("Ask anything...");
   mInput->setSizePolicy(QSizePolicy::Expanding, QSizePolicy::Fixed);
   mInput->setFixedHeight(72);
   mInput->setTabChangesFocus(true);
   mInput->installEventFilter(this);
   mInput->setStyleSheet(
      "QPlainTextEdit {"
      "  background-color: transparent;"
      "  border: none;"
      "  padding: 12px 14px;"
      "  color: #e6edf3;"
      "  font-size: 14px;"
      "  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Helvetica, Arial, sans-serif;"
      "  selection-background-color: #264f78;"
      "  line-height: 1.5;"
      "}");
   boxLayout->addWidget(mInput);

   // Bottom Toolbar inside Input Frame (Sleek bar)
   auto* toolbar = new QWidget(inputBox);
   toolbar->setObjectName("InputToolbar");
   toolbar->setStyleSheet(
      "#InputToolbar {"
      "  background: transparent;"
      "  border-top: 1px solid rgba(48,54,61,0.6);"
      "}");
   auto* toolbarLayout = new QHBoxLayout(toolbar);
   toolbarLayout->setContentsMargins(10, 6, 10, 8);
   toolbarLayout->setSpacing(10);

   // Model Selector (Pill style)
   mModelComboBox = new QComboBox(toolbar);
   mModelComboBox->setCursor(Qt::PointingHandCursor);
   if (mPrefObjectPtr && !mPrefObjectPtr->GetAvailableModels().isEmpty()) {
      mModelComboBox->addItems(mPrefObjectPtr->GetAvailableModels());
   } else if (mPrefObjectPtr && !mPrefObjectPtr->GetBaseUrl().isEmpty()) {
      const QString currentModel = mPrefObjectPtr->GetModel();
      if (!currentModel.isEmpty()) {
         mModelComboBox->addItem(currentModel);
      }
   }
   mModelComboBox->setStyleSheet(
      "QComboBox {"
      "  background: rgba(56,139,253,0.1);"
      "  color: #58a6ff;"
      "  border: 1px solid rgba(56,139,253,0.4);"
      "  border-radius: 6px;"
      "  padding: 4px 8px 4px 10px;"
      "  font-size: 12px;"
      "  font-weight: 500;"
      "  min-width: 100px;"
      "}"
      "QComboBox:hover {"
      "  background: rgba(56,139,253,0.2);"
      "  border: 1px solid rgba(56,139,253,0.6);"
      "}"
      "QComboBox::drop-down { border: none; width: 20px; }"
      "QComboBox::down-arrow {"
      "  image: none;"
      "  border-left: 4px solid transparent;"
      "  border-right: 4px solid transparent;"
      "  border-top: 5px solid #58a6ff;"
      "  margin-right: 6px;"
      "}"
      "QComboBox QAbstractItemView {"
      "  background: #161b22;"
      "  color: #e6edf3;"
      "  border: 1px solid #30363d;"
      "  border-radius: 8px;"
      "  selection-background-color: #388bfd;"
      "  selection-color: white;"
      "  outline: none;"
      "  padding: 4px;"
      "}"
      "QComboBox QAbstractItemView::item {"
      "  padding: 6px 12px;"
      "  border-radius: 4px;"
      "}"
      "QComboBox QAbstractItemView::item:hover {"
      "  background: rgba(56,139,253,0.15);"
      "}");
   
   // Set initial selection from prefs
   if (mPrefObjectPtr) {
       int idx = mModelComboBox->findText(mPrefObjectPtr->GetModel());
       if (idx >= 0) mModelComboBox->setCurrentIndex(idx);
   }
   // Connect change
   connect(mModelComboBox, QOverload<int>::of(&QComboBox::currentIndexChanged), this, [this](int index) {
       if (mPrefObjectPtr) {
           mPrefObjectPtr->SetModel(mModelComboBox->itemText(index));
           mPrefObjectPtr->Apply(); // Save immediately? Or wait for close? apply saves usually.
       }
   });

   toolbarLayout->addWidget(mModelComboBox);

   toolbarLayout->addStretch(1);

   // Settings Button (Subtle icon button)
   mSettingsButton = new QPushButton(toolbar);
   mSettingsButton->setFixedSize(28, 28);
   mSettingsButton->setCursor(Qt::PointingHandCursor);
   mSettingsButton->setToolTip("Open AI Chat Settings");
   mSettingsButton->setIcon(CogwheelIcon());
   mSettingsButton->setIconSize(QSize(18, 18));
   mSettingsButton->setStyleSheet(
      "QPushButton {"
      "  background: transparent;"
      "  border: none;"
      "  border-radius: 6px;"
      "}"
      "QPushButton:hover {"
      "  background: rgba(139,148,158,0.15);"
      "}");
   toolbarLayout->addWidget(mSettingsButton);

   // Send Button (Primary action - gradient with glow)
   mSendButton = new QPushButton("Send", toolbar);
   mSendButton->setCursor(Qt::PointingHandCursor);
   mSendButton->setFixedSize(64, 32);
   mSendButton->setStyleSheet(
      "QPushButton {"
      "  background: qlineargradient(x1:0, y1:0, x2:0, y2:1,"
      "    stop:0 #388bfd, stop:1 #1f6feb);"
      "  border: none;"
      "  border-radius: 8px;"
      "  color: white;"
      "  font-size: 13px;"
      "  font-weight: 600;"
      "  letter-spacing: 0.3px;"
      "}"
      "QPushButton:hover {"
      "  background: qlineargradient(x1:0, y1:0, x2:0, y2:1,"
      "    stop:0 #58a6ff, stop:1 #388bfd);"
      "}"
      "QPushButton:pressed {"
      "  background: qlineargradient(x1:0, y1:0, x2:0, y2:1,"
      "    stop:0 #1f6feb, stop:1 #1158c7);"
      "}"
      "QPushButton:disabled {"
      "  background: #21262d;"
      "  color: #484f58;"
      "}");
   toolbarLayout->addWidget(mSendButton);

   boxLayout->addWidget(toolbar);
   inputLayout->addWidget(inputBox);

   connect(mSettingsButton, &QPushButton::clicked, this, [this]() {
         if (wkfEnv.GetMainWindow()) {
            wkfEnv.GetMainWindow()->ShowPreferencePage(QStringLiteral("AI Chat"));
         }
   });

   mainLayout->addWidget(inputContainer);

   container->setLayout(mainLayout);
   setWidget(container);

   ApplyUiFontSize();

   connect(mSendButton, &QPushButton::clicked, this, &DockWidget::OnSendClicked);

}

void DockWidget::UpdateTitle()
{
   const QString baseTitle = QStringLiteral("AI Chat");
   if (mMessageCount > 0) {
      setWindowTitle(QStringLiteral("%1 (%2)").arg(baseTitle).arg(mMessageCount));
   } else {
      setWindowTitle(baseTitle);
   }
}

// ============================================================================
// Send / User Input
// ============================================================================

void DockWidget::OnSendClicked()
{
   if (mIsRunning) {
      AppendDebug("User requested stop.");
      HideApprovalPanel();
      if (mService) {
         mService->AbortTask();
      }
      StopStatusAnimation();
      StopTypingIndicator();
      if (mStreamingMessage) {
         const int smallFont = UiSmallFontSize();
         AppendToAssistantBubble(
            QStringLiteral("<div style='margin:6px 0; padding:8px 12px; background:#2d2a1a;"
                           " border-radius:6px; border-left:3px solid #f0b429; color:#f0b429;"
                           " font-size:%1px; line-height:1.6;'>Task cancelled by user.</div>")
               .arg(QString::number(smallFont)));
         FinalizeAssistantBubble();
      } else {
         AppendMessage("AI", QStringLiteral("Task cancelled by user."));
      }
      UpdateSendButtonState(false);
      mSendButton->setEnabled(true);
      mInput->setEnabled(true);
      mInput->setFocus();
      return;
   }

   const QString userText = mInput->toPlainText().trimmed();
   if (userText.isEmpty()) {
      return;
   }

   // Apply model selection first
   if (mPrefObjectPtr && mModelComboBox) {
       mPrefObjectPtr->SetModel(mModelComboBox->currentText());
   }
   
   // Pass model to Service (if not already synced via prefs)
   if (mService) {
      mService->SetModel(mModelComboBox->currentText());
   }

   // Add to UI
   AppendMessage("You", userText);

   mInput->clear();
   mMessageCount++;
   UpdateTitle();

   // Start the task via Service
   AppendAssistantPlaceholder();
   if (mService) {
      mService->SendUserMessage(userText);
   }
}

// ============================================================================
// Service / Task Slots
// ============================================================================

void DockWidget::OnTaskChunkReceived(const QString& aChunk)
{
   if (!mAwaitingAssistant) return;

   // Remember if typing indicator was active before processing
   const bool wasTyping = mTypingActive;

   // Filter <think>/<thinking> tags (may start/stop think animation)
   const QString visibleText = ProcessThinkTags(aChunk);

   // Handle typing indicator transition.
   // Note: if a <think> tag was encountered, StartThinkAnimation() already
   // stopped the typing indicator, so mTypingActive will be false and this
   // block is skipped — which is the correct behaviour.
   if (wasTyping && mTypingActive) {
      if (!visibleText.isEmpty()) {
         // Normal text arrived — stop the typing indicator
         StopTypingIndicator();
      }
   }

   if (visibleText.isEmpty()) return;

   mPendingAssistantText += visibleText;
   mAssistantBubbleHtml = mFrozenBubbleHtml + RenderMarkdown(mPendingAssistantText);
   UpdateAssistantMessage();
}

void DockWidget::OnTaskMessageUpdated(const QString& aMessage)
{
   if (mAwaitingAssistant && mStreamingMessage) {
      // Ignore full-text updates during streaming, as they strip think blocks
      // and would overwrite our rich UI state (including open think animations).
      return; 
   }

   // Full message update (e.g. from history reload or non-streaming)
   StopTypingIndicator();
   mPendingAssistantText = aMessage;
   mAssistantBubbleHtml = mFrozenBubbleHtml + RenderMarkdown(mPendingAssistantText);
   UpdateAssistantMessage();
   
   if (!mAwaitingAssistant) {
       // If we weren't expecting it (e.g. history load), ensure we show it?
       // Actually this might be called during generation too.
   }
}

void DockWidget::OnToolCallStreaming(const QString& aFunctionName, int aArgBytes)
{
   if (!mAwaitingAssistant) return;

   // Stop the generic thinking indicator (text generation is done)
   StopTypingIndicator();

   // Format byte count for human readability
   QString sizeText;
   if (aArgBytes < 1024) {
      sizeText = QStringLiteral("%1 B").arg(aArgBytes);
   } else if (aArgBytes < 1024 * 1024) {
      sizeText = QStringLiteral("%1 KB").arg(aArgBytes / 1024.0, 0, 'f', 1);
   } else {
      sizeText = QStringLiteral("%1 MB").arg(aArgBytes / (1024.0 * 1024.0), 0, 'f', 2);
   }

   // Map tool name to friendly display
   QString friendlyName = aFunctionName;
   if (aFunctionName == QStringLiteral("write_to_file"))       friendlyName = QStringLiteral("Writing file");
   else if (aFunctionName == QStringLiteral("replace_in_file")) friendlyName = QStringLiteral("Editing file");
   else if (aFunctionName == QStringLiteral("delete_file"))    friendlyName = QStringLiteral("Deleting file");
   else if (aFunctionName == QStringLiteral("insert_before"))  friendlyName = QStringLiteral("Inserting before");
   else if (aFunctionName == QStringLiteral("insert_after"))   friendlyName = QStringLiteral("Inserting after");
   else if (aFunctionName == QStringLiteral("execute_command")) friendlyName = QStringLiteral("Preparing command");
   else if (aFunctionName == QStringLiteral("run_tests"))       friendlyName = QStringLiteral("Running tests");
   else if (aFunctionName == QStringLiteral("read_file"))       friendlyName = QStringLiteral("Reading file");
   else if (aFunctionName == QStringLiteral("search_files"))    friendlyName = QStringLiteral("Searching files");
   else if (aFunctionName == QStringLiteral("attempt_completion")) friendlyName = QStringLiteral("Completing");
   else if (aFunctionName == QStringLiteral("normalize_workspace_encoding")) friendlyName = QStringLiteral("Normalizing encoding");
   else if (aFunctionName == QStringLiteral("list_code_definition_names")) friendlyName = QStringLiteral("Listing definitions");

   // Update status bar with progress
   StartStatusAnimation(QStringLiteral("Generating: %1 (%2)").arg(friendlyName, sizeText));
   mStatusLabel->setStyleSheet(StatusLabelStyle(QStringLiteral("#c586c0")));

   // Throttle bubble HTML updates to max ~3 fps to avoid excessive re-rendering
   const qint64 nowMs = QDateTime::currentMSecsSinceEpoch();
   if (nowMs - mLastStreamIndicatorMs < 300) return;
   mLastStreamIndicatorMs = nowMs;

   // Insert or update a streaming progress indicator in the bubble
   if (mStreamingMessage) {
      static const QString kStreamStart = QStringLiteral("<!--tool-stream-indicator-->");
      static const QString kStreamEnd   = QStringLiteral("<!--/tool-stream-indicator-->");

      const int smallFont = UiSmallFontSize();
      const int spinnerFont = qMax(9, smallFont - 1);
      const QString indicatorHtml = QStringLiteral(
         "%1<div style='margin:6px 0; padding:8px 12px;"
         " background:rgba(197,134,192,0.08); border:1px solid rgba(197,134,192,0.25);"
         " border-radius:8px; font-size:%5px; color:#c586c0;"
         " font-family:-apple-system,BlinkMacSystemFont,Segoe UI,Helvetica,sans-serif;'>"
         "<span style='font-weight:600;'>%3</span>"
         " <span style='color:#8b949e;'>%4</span>"
         " <span style='color:#6a737d;font-size:%6px;'>"
         "<!--tool-spinner-->generating...<!--/tool-spinner--></span>"
         "</div>%2")
         .arg(kStreamStart, kStreamEnd, friendlyName.toHtmlEscaped(), sizeText,
              QString::number(smallFont), QString::number(spinnerFont));

      // Remove old stream indicator if present
      if (mAssistantBubbleHtml.contains(kStreamStart)) {
         const int si = mAssistantBubbleHtml.indexOf(kStreamStart);
         const int ei = mAssistantBubbleHtml.indexOf(kStreamEnd, si);
         if (si >= 0 && ei > si) {
            mAssistantBubbleHtml.remove(si, ei + kStreamEnd.size() - si);
         }
      }

      // Append indicator after current content
      mAssistantBubbleHtml += indicatorHtml;
      UpdateAssistantMessage();
   }
}

void DockWidget::OnTaskFinished(const QString& aSummary)
{
   AppendDebug(QStringLiteral("Task finished."));
   StopStatusAnimation();
   StopTypingIndicator();
   UpdateSendButtonState(false);

   FinalizeAssistantBubble();

   mSendButton->setEnabled(true);
   mInput->setEnabled(true);
   mInput->setFocus();
   mStatusLabel->setText("Ready");
   mStatusLabel->setStyleSheet(StatusLabelStyle(QStringLiteral("#73c991")));
   
   // Refresh session list if needed (handled by signal)
}

void DockWidget::OnTaskError(const QString& aError)
{
   AppendDebug(QStringLiteral("Task error: %1").arg(aError));
   StopStatusAnimation();
   StopTypingIndicator();
   UpdateSendButtonState(false);

   if (mStreamingMessage) {
      const int smallFont = UiSmallFontSize();
      AppendToAssistantBubble(
         QStringLiteral("<div style='margin:6px 0; padding:6px 10px; background:#3b1c1c;"
                        " border-radius:6px; border-left:3px solid #f14c4c; color:#f14c4c;"
                        " font-size:%1px;'>%2</div>")
            .arg(QString::number(smallFont))
            .arg(aError.toHtmlEscaped()));
      FinalizeAssistantBubble();
   } else {
      AppendMessage("Error", aError);
   }

   mSendButton->setEnabled(true);
   mInput->setEnabled(true);
   mInput->setFocus();
   mStatusLabel->setText(QStringLiteral("Error"));
   mStatusLabel->setStyleSheet(StatusLabelStyle(QStringLiteral("#f14c4c")));
}

void DockWidget::OnToolStarted(const QString& aToolName)
{
   StopTypingIndicator();
   StartStatusAnimation(QStringLiteral("Running tool: %1").arg(aToolName));
   mStatusLabel->setStyleSheet(StatusLabelStyle(QStringLiteral("#dcdcaa")));

   // Remove the streaming progress indicator
   if (mStreamingMessage) {
      static const QString kStreamStart = QStringLiteral("<!--tool-stream-indicator-->");
      static const QString kStreamEnd   = QStringLiteral("<!--/tool-stream-indicator-->");
      if (mAssistantBubbleHtml.contains(kStreamStart)) {
         const int si = mAssistantBubbleHtml.indexOf(kStreamStart);
         const int ei = mAssistantBubbleHtml.indexOf(kStreamEnd, si);
         if (si >= 0 && ei > si) {
            mAssistantBubbleHtml.remove(si, ei + kStreamEnd.size() - si);
         }
      }
      // Insert inline "running" block
      AppendToAssistantBubble(BuildToolInlineHtml(aToolName, QString(), true, true));

      // Freeze current bubble state so that subsequent text chunks
      // (from the next LLM turn) build on top of the tool HTML
      // instead of overwriting it.
      mFrozenBubbleHtml = mAssistantBubbleHtml;
      mPendingAssistantText.clear();
   }
}

void DockWidget::OnToolFinished(const QString& aToolName, const ToolResult& aResult)
{
   if (mStreamingMessage) {
      // Replace running with result
      const QString runningTag = QStringLiteral("<!--tool-running-%1-->").arg(aToolName);
      mAssistantBubbleHtml.remove(runningTag);

      const QString startMarker = QStringLiteral("<!--tool-start-%1-->").arg(aToolName);
      const QString endMarker   = QStringLiteral("<!--tool-end-%1-->").arg(aToolName);
      int startIdx = mAssistantBubbleHtml.lastIndexOf(startMarker);
      int endIdx   = mAssistantBubbleHtml.lastIndexOf(endMarker);
      if (startIdx >= 0 && endIdx > startIdx) {
         mAssistantBubbleHtml.remove(startIdx, endIdx + endMarker.size() - startIdx);
      }

      // For attempt_completion, show short badge + render full result as Markdown
      if (aToolName == QStringLiteral("attempt_completion")) {
         // Badge shows just "Completed" with a short summary
         AppendToAssistantBubble(
            BuildToolInlineHtml(aToolName, QStringLiteral("Task completed"), aResult.success, false));
         // Render the full completion result as Markdown content below the badge
         if (!aResult.content.isEmpty()) {
            const QString renderedHtml = RenderMarkdown(aResult.content);
            AppendToAssistantBubble(
               QStringLiteral("<div style='margin:8px 0 4px 0;'>%1</div>").arg(renderedHtml));
         }
      } else {
         AppendToAssistantBubble(
            BuildToolInlineHtml(aToolName, aResult.userDisplayMessage, aResult.success, false));
      }

      // Freeze current bubble state so that subsequent text chunks
      // build on top of the tool result HTML.
      mFrozenBubbleHtml = mAssistantBubbleHtml;
      mPendingAssistantText.clear();
   }

   StopStatusAnimation();
   mStatusLabel->setText(
      aResult.success
         ? QStringLiteral("Tool '%1' completed").arg(aToolName)
         : QStringLiteral("Tool '%1' failed").arg(aToolName));
}

void DockWidget::OnApprovalRequired(const QString& aToolCallId, const QString& aToolName,
                                    const QString& aDiffPreview, const QJsonObject& aParams)
{
   Q_UNUSED(aParams);
   AppendDebug(QStringLiteral("Approval required for %1").arg(aToolName));

   // Only ask_question reaches here (all other tools auto-execute).
   // Show the approval panel for user input.
   mPendingApprovalToolCallId = aToolCallId;
   mPendingApprovalToolName   = aToolName;

   StopStatusAnimation();
   mStatusLabel->setText(QStringLiteral("AI is asking a question"));
   mStatusLabel->setStyleSheet(StatusLabelStyle(QStringLiteral("#3b82f6")));

   ShowApprovalPanel(aToolName, aDiffPreview);
}

// ============================================================================
// Approval Panel  – modern card with slide+fade animation
// ============================================================================

QString DockWidget::FormatPreviewHtml(const QString& aToolName, const QString& aText) const
{
   if (aText.isEmpty())
      return QStringLiteral("<i style='color:#64748b;'>No preview available</i>");

   // Question tool → styled prose
   if (aToolName == QStringLiteral("ask_question")) {
      const int baseFont = UiFontSize();
      return QStringLiteral(
         "<div style='color:#93c5fd; font-size:%1px; line-height:1.7;"
         " font-family: Segoe UI, system-ui, sans-serif;'>%2</div>")
            .arg(baseFont)
            .arg(aText.toHtmlEscaped()
                     .replace(QStringLiteral("\n"), QStringLiteral("<br>")));
   }

   // Other tools → code block with coloured labels / diff lines
   const QStringList lines = aText.split('\n');
   const int codeFont = UiCodeFontSize();
   QString html = QStringLiteral(
      "<pre style='margin:0; white-space:pre-wrap; line-height:1.6;"
      " font-family: Cascadia Code, Fira Code, JetBrains Mono, Consolas, monospace;"
      " font-size: %1px;'>")
      .arg(codeFont);

   for (const QString& line : lines) {
      const QString esc = line.toHtmlEscaped();
      if (line.startsWith(QStringLiteral("Command:"))  ||
          line.startsWith(QStringLiteral("Working directory:")) ||
          line.startsWith(QStringLiteral("Timeout:")) ||
          line.startsWith(QStringLiteral("File:"))) {
         html += QStringLiteral("<span style='color:#818cf8; font-weight:600;'>%1</span>\n").arg(esc);
      } else if (line.startsWith('+')) {
         html += QStringLiteral("<span style='color:#34d399;'>%1</span>\n").arg(esc);
      } else if (line.startsWith('-')) {
         html += QStringLiteral("<span style='color:#f87171;'>%1</span>\n").arg(esc);
      } else if (line.startsWith(QStringLiteral("@@"))) {
         html += QStringLiteral("<span style='color:#60a5fa;'>%1</span>\n").arg(esc);
      } else {
         html += QStringLiteral("<span style='color:#d1d5db;'>%1</span>\n").arg(esc);
      }
   }
   html += QStringLiteral("</pre>");
   return html;
}

void DockWidget::ShowApprovalPanel(const QString& aToolName, const QString& aDiff)
{
   const bool isQuestion = (aToolName == QStringLiteral("ask_question"));

   // -- Per-tool accent colour & icon (ASCII-safe for Windows font compat) --
   QString accentColor, iconText, iconBg, iconFg;
   if (isQuestion) {
      accentColor = QStringLiteral("#3b82f6");
      iconText    = QStringLiteral("?");
      iconBg      = QStringLiteral("rgba(59,130,246,0.15)");
      iconFg      = QStringLiteral("#93c5fd");
   } else if (aToolName == QStringLiteral("execute_command")) {
      accentColor = QStringLiteral("#f59e0b");
      iconText    = QStringLiteral(">");
      iconBg      = QStringLiteral("rgba(245,158,11,0.15)");
      iconFg      = QStringLiteral("#fcd34d");
   } else {
      accentColor = QStringLiteral("#8b5cf6");
      iconText    = QStringLiteral("*");
      iconBg      = QStringLiteral("rgba(139,92,246,0.15)");
      iconFg      = QStringLiteral("#c4b5fd");
   }

   // Dynamic top-border accent
   mApprovalPanel->setStyleSheet(
      QStringLiteral(
         "#ApprovalPanel {"
         "  background: qlineargradient(x1:0, y1:0, x2:0, y2:1,"
         "    stop:0 #1e2233, stop:1 #171b2e);"
         "  border-top: 3px solid %1;"
         "}").arg(accentColor));

   // Icon badge
   mApprovalIcon->setText(iconText);
   mApprovalIcon->setStyleSheet(
      QStringLiteral(
         "QLabel { background: %1; color: %2;"
         " border-radius: 6px; font-size: 14px; font-weight: 700; }")
         .arg(iconBg, iconFg));

   // Header & content
   if (isQuestion) {
      mApprovalLabel->setText(QStringLiteral("AI is asking you a question"));
      mDiffPreview->setHtml(FormatPreviewHtml(aToolName, aDiff));
      mAnswerInput->setVisible(true);
      mAnswerInput->clear();
      mApproveButton->setText(QStringLiteral("Submit Answer"));
      mRejectButton->setText(QStringLiteral("Skip"));
   } else {
      mApprovalLabel->setText(
         QStringLiteral("Tool [%1] requires approval").arg(aToolName));
      mDiffPreview->setHtml(FormatPreviewHtml(aToolName, aDiff));
      mAnswerInput->setVisible(false);
      mApproveButton->setText(QStringLiteral("Approve"));
      mRejectButton->setText(QStringLiteral("Reject"));
   }

   // Disable chat input while waiting
   mSendButton->setEnabled(true);
   UpdateSendButtonState(true);
   mInput->setEnabled(false);

   // Animate in
   AnimateApprovalShow();

   QTimer::singleShot(60, this, [this, isQuestion]() {
      if (mScrollArea && mScrollArea->verticalScrollBar())
         mScrollArea->verticalScrollBar()->setValue(
            mScrollArea->verticalScrollBar()->maximum());
      if (isQuestion)
         mAnswerInput->setFocus();
      else
         mApproveButton->setFocus();
   });
}

void DockWidget::HideApprovalPanel()
{
   mPendingApprovalToolCallId.clear();
   mPendingApprovalToolName.clear();
   mAnswerInput->setVisible(false);
   mAnswerInput->clear();
   AnimateApprovalHide();
}

// ---------------------------------------------------------------------------
// Slide + fade animations
// ---------------------------------------------------------------------------

void DockWidget::AnimateApprovalShow()
{
   // Cancel any running animation
   if (mApprovalAnim) {
      mApprovalAnim->stop();
      mApprovalAnim->deleteLater();
      mApprovalAnim = nullptr;
   }

   mApprovalPanel->setVisible(true);
   mApprovalPanel->setMaximumHeight(QWIDGETSIZE_MAX);  // let sizeHint be accurate
   mApprovalPanel->adjustSize();
   const int targetH = qBound(80, mApprovalPanel->sizeHint().height(), 420);
   mApprovalPanel->setMaximumHeight(0);
   mApprovalOpacity->setOpacity(0.0);

   auto* anim = new QVariantAnimation(this);
   anim->setDuration(300);
   anim->setStartValue(0.0);
   anim->setEndValue(1.0);
   anim->setEasingCurve(QEasingCurve::OutCubic);

   connect(anim, &QVariantAnimation::valueChanged, this,
           [this, targetH](const QVariant& v) {
      const qreal t = v.toReal();
      mApprovalPanel->setMaximumHeight(static_cast<int>(t * targetH));
      if (mApprovalOpacity) mApprovalOpacity->setOpacity(t);
   });
   connect(anim, &QVariantAnimation::finished, this, [this]() {
      mApprovalPanel->setMaximumHeight(QWIDGETSIZE_MAX);
      mApprovalAnim = nullptr;
   });

   mApprovalAnim = anim;
   anim->start();
}

void DockWidget::AnimateApprovalHide()
{
   if (mApprovalAnim) {
      mApprovalAnim->stop();
      mApprovalAnim->deleteLater();
      mApprovalAnim = nullptr;
   }

   if (!mApprovalPanel->isVisible()) {
      mApprovalPanel->setMaximumHeight(0);
      return;
   }

   const int startH = mApprovalPanel->height();

   auto* anim = new QVariantAnimation(this);
   anim->setDuration(200);
   anim->setStartValue(1.0);
   anim->setEndValue(0.0);
   anim->setEasingCurve(QEasingCurve::InCubic);

   connect(anim, &QVariantAnimation::valueChanged, this,
           [this, startH](const QVariant& v) {
      const qreal t = v.toReal();
      mApprovalPanel->setMaximumHeight(static_cast<int>(t * startH));
      if (mApprovalOpacity) mApprovalOpacity->setOpacity(t);
   });
   connect(anim, &QVariantAnimation::finished, this, [this]() {
      mApprovalPanel->setVisible(false);
      mApprovalPanel->setMaximumHeight(0);
      mApprovalAnim = nullptr;
   });

   mApprovalAnim = anim;
   anim->start();
}

void DockWidget::OnApproveClicked()
{
   if (mPendingApprovalToolCallId.isEmpty()) return;

   const QString toolCallId = mPendingApprovalToolCallId;
   const QString toolName   = mPendingApprovalToolName;

   // For ask_question, the answer text is the feedback
   QString feedback;
   if (toolName == QStringLiteral("ask_question")) {
      feedback = mAnswerInput->toPlainText().trimmed();
      if (feedback.isEmpty()) {
         feedback = QStringLiteral("[User provided no answer]");
      }
   }

   HideApprovalPanel();
   AppendDebug(QStringLiteral("Approval accepted: %1 (%2)").arg(toolName, toolCallId));
   if (mService) {
      mService->ApproveTool(toolCallId, feedback);
   }
}

void DockWidget::OnRejectClicked()
{
   if (mPendingApprovalToolCallId.isEmpty()) return;

   const QString toolCallId = mPendingApprovalToolCallId;
   const QString toolName   = mPendingApprovalToolName;

   HideApprovalPanel();
   AppendDebug(QStringLiteral("Approval rejected: %1 (%2)").arg(toolName, toolCallId));
   if (mService) {
      mService->RejectTool(toolCallId, QStringLiteral("User rejected the operation."));
   }
}

// ============================================================================
// Inline Editor Review  (Copilot-style: apply → decorate → Accept/Reject bar)
// ============================================================================

void DockWidget::OnInlineReviewReady(const QString& aFilePath, const InlineReviewSummary& aSummary)
{
   // If a bar already exists for this file, remove the old one first
   auto it = mReviewBars.find(aFilePath);
   if (it != mReviewBars.end())
   {
      if (it.value()) {
         it.value()->hide();
         it.value()->deleteLater();
      }
      mReviewBars.erase(it);
   }

   // Obtain the editor viewport for this file
   QWidget* viewport = EditorBridge::GetEditorViewport(aFilePath);
   if (!viewport) return;

   auto* bar = new InlineReviewBar(viewport);
   bar->SetInfo(aSummary.filePath, aSummary.addedLines, aSummary.removedLines);
   bar->setProperty("reviewFilePath", aFilePath);
   bar->show();

   connect(bar, &InlineReviewBar::Accepted, this, &DockWidget::OnInlineAccepted);
   connect(bar, &InlineReviewBar::Rejected, this, &DockWidget::OnInlineRejected);

   // Safety: if the editor (and bar) is destroyed externally (e.g. tab closed),
   // clean up bookkeeping. File already has proposed content = implicit accept.
   connect(bar, &QObject::destroyed, this, [this, aFilePath]() {
      if (mReviewBars.remove(aFilePath) > 0) {
         // Editor is being destroyed — only remove state, don't touch editor APIs
         if (mService) {
            mService->ForgetInlineReview(aFilePath);
         }
      }
   });

   mReviewBars.insert(aFilePath, bar);
}

void DockWidget::OnInlineAccepted()
{
   if (!mService) return;

   auto* bar = qobject_cast<InlineReviewBar*>(sender());
   const QString filePath = bar ? bar->property("reviewFilePath").toString() : QString();
   if (filePath.isEmpty()) return;

   const InlineReviewOutcome outcome = mService->AcceptInlineReview(filePath);

   // Remove from map first so the destroyed handler becomes a no-op
   mReviewBars.remove(filePath);
   if (bar) {
      bar->hide();
      bar->deleteLater();
   }

   // Inline notification in the assistant bubble
   if (mStreamingMessage && !outcome.summary.filePath.isEmpty()) {
      const int smallFont = UiSmallFontSize();
      AppendToAssistantBubble(
         QStringLiteral("<div style='margin:4px 0; padding:4px 10px; background:#1a2e1a;"
                        " border-radius:6px; border-left:3px solid #3fb950; color:#73c991;"
               " font-size:%1px;'>Changes accepted: <b>%2</b></div>")
         .arg(QString::number(smallFont))
         .arg(QFileInfo(outcome.summary.filePath).fileName()));
   }

   mStatusLabel->setText(QStringLiteral("Changes accepted."));
   mStatusLabel->setStyleSheet(StatusLabelStyle(QStringLiteral("#73c991")));
}

void DockWidget::OnInlineRejected()
{
   if (!mService) return;

   auto* bar = qobject_cast<InlineReviewBar*>(sender());
   const QString filePath = bar ? bar->property("reviewFilePath").toString() : QString();
   if (filePath.isEmpty()) return;

   const InlineReviewOutcome outcome = mService->RejectInlineReview(filePath);

   // Remove from map first so the destroyed handler becomes a no-op
   mReviewBars.remove(filePath);
   if (bar) {
      bar->hide();
      bar->deleteLater();
   }

   // Inline notification in the assistant bubble
   if (mStreamingMessage && !outcome.summary.filePath.isEmpty()) {
      const int smallFont = UiSmallFontSize();
      AppendToAssistantBubble(
         QStringLiteral("<div style='margin:4px 0; padding:4px 10px; background:#3b1c1c;"
                        " border-radius:6px; border-left:3px solid #f14c4c; color:#f14c4c;"
               " font-size:%1px;'>Changes rejected: <b>%2</b></div>")
         .arg(QString::number(smallFont))
         .arg(QFileInfo(outcome.summary.filePath).fileName()));
   }

   mStatusLabel->setText(QStringLiteral("Changes rejected."));
   mStatusLabel->setStyleSheet(StatusLabelStyle(QStringLiteral("#f14c4c")));
}


// ============================================================================
// Debug Window
// ============================================================================

void DockWidget::OnDebugClicked()
{
   if (mPrefObjectPtr && !mPrefObjectPtr->IsDebugEnabled()) {
      if (wkfEnv.GetMainWindow()) {
         wkfEnv.GetMainWindow()->ShowPreferencePage(QStringLiteral("AI Chat"));
      }
      return;
   }
   EnsureDebugWindow();
   mDebugWindow->show();
   mDebugWindow->raise();
   mDebugWindow->activateWindow();
}

void DockWidget::UpdateSendButtonState(bool aRunning)
{
   mIsRunning = aRunning;
   if (mIsRunning) {
      mSendButton->setText(QStringLiteral("Stop"));
      mSendButton->setStyleSheet(
         "QPushButton { background-color: #da3633; border: none; border-radius: 3px;"
         " color: white; font-size: 11px; font-weight: bold; }"
         "QPushButton:hover { background-color: #f85149; }");
   } else {
      mSendButton->setText(QStringLiteral("Send"));
      mSendButton->setStyleSheet(
         "QPushButton { background-color: #0078d4; border: none; border-radius: 3px;"
         " color: white; font-size: 11px; font-weight: bold; }"
         "QPushButton:hover { background-color: #1084d8; }"
         "QPushButton:pressed { background-color: #006cbd; }"
         "QPushButton:disabled { background-color: #333333; color: #888888; }");
   }
}

void DockWidget::StartStatusAnimation(const QString& aBaseText)
{
   mStatusBaseText = aBaseText;
   while (mStatusBaseText.endsWith('.')) {
      mStatusBaseText.chop(1);
   }
   mStatusTick = 0;
   if (mStatusTimer) {
      mStatusTimer->start();
   }
   mStatusLabel->setText(mStatusBaseText);
}

void DockWidget::StopStatusAnimation()
{
   mStatusBaseText.clear();
   mStatusTick = 0;
   if (mStatusTimer) {
      mStatusTimer->stop();
   }
}

// ============================================================================
// Think-tag streaming helpers
// ============================================================================

/// Process a raw chunk that may contain <think>, </think>, <thinking>, </thinking>.
/// Returns the portion that should be appended as visible assistant text.
/// Think content is accumulated in mThinkText and displayed as a scrolling indicator.
QString DockWidget::ProcessThinkTags(const QString& aChunk)
{
   // We support both <think>/<thinking> tags (various model formats)
   static const QStringList kOpenTags  = { QStringLiteral("<think>"),    QStringLiteral("<thinking>") };
   static const QStringList kCloseTags = { QStringLiteral("</think>"),   QStringLiteral("</thinking>") };
   static const QStringList kAllTags   = { QStringLiteral("<think>"), QStringLiteral("<thinking>"),
                                          QStringLiteral("</think>"), QStringLiteral("</thinking>") };

   // Local reference with automatic storage duration so it can be captured by the lambda
   const QStringList& allTags = kAllTags;

   auto extractRemainder = [&allTags](const QString& text) -> QString {
      if (text.isEmpty()) return QString();
      QString lower = text.toLower();
      int maxLen = 0;
      for (const auto& tag : allTags) {
         const QString tagLower = tag.toLower();
         const int maxCheck = qMin(tagLower.size() - 1, lower.size());
         for (int len = maxCheck; len >= 1; --len) {
            if (lower.endsWith(tagLower.left(len))) {
               maxLen = qMax(maxLen, len);
               break;
            }
         }
      }
      return maxLen > 0 ? text.right(maxLen) : QString();
   };

   QString visible;
   QString remaining = mThinkTagRemainder + aChunk;
   mThinkTagRemainder.clear();

   while (!remaining.isEmpty()) {
      if (mInThinkBlock) {
         // Look for any closing tag
         int closeIdx = -1;
         int closeLen = 0;
         for (const auto& tag : kCloseTags) {
            const int idx = remaining.indexOf(tag, 0, Qt::CaseInsensitive);
            if (idx >= 0 && (closeIdx < 0 || idx < closeIdx)) {
               closeIdx = idx;
               closeLen = tag.size();
            }
         }

         if (closeIdx >= 0) {
            // Accumulate think text before the closing tag
            mThinkText += remaining.left(closeIdx);
            remaining = remaining.mid(closeIdx + closeLen);
            mInThinkBlock = false;
            StopThinkAnimation();
         } else {
            // Entire remainder is think content
            const QString remainder = extractRemainder(remaining);
            if (!remainder.isEmpty()) {
               mThinkText += remaining.left(remaining.size() - remainder.size());
               mThinkTagRemainder = remainder;
            } else {
               mThinkText += remaining;
            }
            remaining.clear();
         }
      } else {
         // Look for any opening tag
         int openIdx = -1;
         int openLen = 0;
         for (const auto& tag : kOpenTags) {
            const int idx = remaining.indexOf(tag, 0, Qt::CaseInsensitive);
            if (idx >= 0 && (openIdx < 0 || idx < openIdx)) {
               openIdx = idx;
               openLen = tag.size();
            }
         }

         if (openIdx >= 0) {
            // Text before the opening tag is visible
            visible += remaining.left(openIdx);
            remaining = remaining.mid(openIdx + openLen);
            mInThinkBlock = true;
            StartThinkAnimation();
         } else {
            // No think tags — entire remainder is visible
            const QString remainder = extractRemainder(remaining);
            if (!remainder.isEmpty()) {
               visible += remaining.left(remaining.size() - remainder.size());
               mThinkTagRemainder = remainder;
            } else {
               visible += remaining;
            }
            remaining.clear();
         }
      }
   }

   return visible;
}

void DockWidget::StartThinkAnimation()
{
   // Stop the typing indicator — think animation takes over the display.
   // This must happen here (not only in OnTaskChunkReceived) because
   // ProcessThinkTags may open AND close a think block in a single chunk;
   // if we relied on post-ProcessThinkTags logic the typing timer could
   // still fire and overwrite the "Thinking Done" block.
   if (mTypingActive) {
      mTypingTick = 0;
      mTypingActive = false;
      if (mTypingTimer) mTypingTimer->stop();
   }

   // Freeze any pending visible text so the think indicator appears inline after it
   if (!mPendingAssistantText.isEmpty()) {
      mFrozenBubbleHtml += RenderMarkdown(mPendingAssistantText);
      mPendingAssistantText.clear();
      mAssistantBubbleHtml = mFrozenBubbleHtml;
   }

   mThinkScrollOffset = 0;
   if (mThinkScrollTimer && !mThinkScrollTimer->isActive()) {
      mThinkScrollTimer->start();
   }
   // Also show in status bar
   StartStatusAnimation(QStringLiteral("Deep thinking"));
}

void DockWidget::StopThinkAnimation()
{
   if (mThinkScrollTimer) {
      mThinkScrollTimer->stop();
   }
   StopStatusAnimation();

   // Save the think text for later expand/collapse
   const int blockIndex = mThinkBlockTexts.size();
   mThinkBlockTexts.append(mThinkText);

   // Freeze a "Deep Thinking Done" block at the current inline position
   mFrozenBubbleHtml += BuildDeepThinkDoneHtml(blockIndex, mThinkBlockTexts, mExpandedThinkBlocks);
   mAssistantBubbleHtml = mFrozenBubbleHtml + RenderMarkdown(mPendingAssistantText);

   // Clear the active think indicator — it's now frozen as a done block
   mThinkIndicatorHtml.clear();
   mThinkText.clear();

   UpdateAssistantMessage();
}

void DockWidget::UpdateThinkScroll()
{
   if (!mStreamingMessage) return;
   if (mThinkText.trimmed().isEmpty()) return;

   const bool animate = mInThinkBlock;

   // Split think text into lines for waterfall display
   QStringList lines;
   for (const QString& raw : mThinkText.split('\n')) {
      const QString trimmed = raw.trimmed();
      if (!trimmed.isEmpty()) {
         lines.append(trimmed);
      }
   }

   if (lines.isEmpty()) {
      lines.append(QStringLiteral("..."));
   }

   // Single-line waterfall: cycle through the most recent lines
   const int totalLines = lines.size();
   const int waterfallWindow = qMin(6, totalLines);
   int lineIndex = totalLines - 1;
   if (waterfallWindow > 0) {
      lineIndex = totalLines - 1 - (mThinkScrollOffset % waterfallWindow);
      lineIndex = qMax(0, lineIndex);
   }
   if (animate) {
      ++mThinkScrollOffset;
   }

   // Truncate long line
   QString line = lines[lineIndex];
   if (line.size() > 80) {
      line = line.left(77) + QStringLiteral("...");
   }
   const QString lineHtml = QStringLiteral(
      "<div style='white-space: nowrap; overflow: hidden; text-overflow: ellipsis; line-height: 1.5;'>%1</div>")
      .arg(line.toHtmlEscaped());

   // Animated dots for the "Thinking" label
   const int dotCount = (mThinkScrollOffset % 4);
   QString dots;
   for (int i = 0; i < dotCount; ++i) dots += '.';

   const QString toggleText = mThinkExpanded
      ? QStringLiteral("hide details")
      : QStringLiteral("show details");
   const QString toggleLink = QStringLiteral(
      "<a href='aichat://think-toggle' style='color:#8b949e; text-decoration:none;'>[%1]</a>")
      .arg(toggleText);

   QString expandedHtml;
   if (mThinkExpanded) {
      QString full = mThinkText.toHtmlEscaped();
      full.replace(QStringLiteral("\n"), QStringLiteral("<br>"));
      expandedHtml = QStringLiteral(
         "<div style='margin-top:6px; white-space:pre-wrap; color:#9aa4bf;"
         " max-height:220px; overflow:auto; border-top:1px dashed #2b2f45; padding-top:6px;'>%1</div>")
         .arg(full);
   }

   // Build the think indicator HTML
   const int smallFont = UiSmallFontSize();
   mThinkIndicatorHtml = QStringLiteral(
      "<!--think-indicator-->"
      "<div style='background: qlineargradient(x1:0,y1:0,x2:0,y2:1,"
      "stop:0 #1a1a2e, stop:1 #16213e);"
      " border: 1px solid #333; border-radius: 8px; padding: 8px 12px;"
      " margin: 4px 0; font-family: Consolas, monospace; font-size: %1px;"
      " color: #7c8db5;'>"
      "<div style='color: #e0a851; font-weight: 600; margin-bottom: 4px;'>"
      "Thinking%2 %3</div>"
      "%4"
      "%5"
      "</div>"
      "<!--/think-indicator-->")
      .arg(smallFont)
      .arg(dots)
      .arg(toggleLink)
      .arg(lineHtml)
      .arg(expandedHtml);

   UpdateAssistantMessage();
}

void DockWidget::UpdateRunningToolSpinner()
{
   if (!mStreamingMessage || !mAwaitingAssistant) return;

   static const QString kStartMarker = QStringLiteral("<!--tool-spinner-->");
   static const QString kEndMarker   = QStringLiteral("<!--/tool-spinner-->");

   if (!mAssistantBubbleHtml.contains(kStartMarker)) return;

   // Build animated dot text (cycles 1–3 dots)
   const int dots = (mStatusTick % 3) + 1;
   QString dotsText;
   for (int i = 0; i < dots; ++i) dotsText += QChar('.');

   // Create a display copy — do NOT modify the stored mAssistantBubbleHtml
   QString displayHtml = mAssistantBubbleHtml;
   const int startIdx = displayHtml.indexOf(kStartMarker);
   const int endIdx   = displayHtml.indexOf(kEndMarker, startIdx);
   if (startIdx < 0 || endIdx < 0) return;

   const int contentStart = startIdx + kStartMarker.size();
   displayHtml.replace(contentStart, endIdx - contentStart,
                       QStringLiteral("working%1").arg(dotsText));

   mStreamingMessage->setHtml(displayHtml);
   UpdateMessageSizes();
}

int DockWidget::UiFontSize() const
{
   int size = PrefData::cDEFAULT_UI_FONT_SIZE;
   if (mPrefObjectPtr) {
      size = mPrefObjectPtr->GetUiFontSize();
   }
   return qBound(10, size, 22);
}

int DockWidget::UiSmallFontSize() const
{
   return qMax(10, UiFontSize() - 2);
}

int DockWidget::UiCodeFontSize() const
{
   return qMax(10, UiFontSize() - 2);
}

QString DockWidget::StatusLabelStyle(const QString& aColor) const
{
   return QStringLiteral(
      "QLabel { color: %1; font-style: italic; padding: 2px 12px;"
      " background-color: #252526; font-size: %2px; }")
      .arg(aColor)
      .arg(UiSmallFontSize());
}

QString DockWidget::BuildBubbleStyle(const QString& aRole) const
{
   const int baseFont = UiFontSize();
   const int errorFont = qMax(11, baseFont - 1);

   if (aRole == "You") {
      return QStringLiteral(
         "QTextBrowser {"
         "  background: qlineargradient(x1:0, y1:0, x2:1, y2:1,"
         "    stop:0 #388bfd, stop:1 #1f6feb);"
         "  color: #ffffff;"
         "  border-radius: 16px;"
         "  border-bottom-right-radius: 4px;"
         "  padding: 12px 16px;"
         "  font-size: %1px;"
         "  font-family: 'Microsoft YaHei UI', 'Microsoft YaHei', 'Segoe UI', sans-serif;"
         "}")
         .arg(baseFont);
   }

   if (aRole == "Error") {
      return QStringLiteral(
         "QTextBrowser {"
         "  background: qlineargradient(x1:0, y1:0, x2:0, y2:1,"
         "    stop:0 #2d1215, stop:1 #1f0d0f);"
         "  color: #ff7b72;"
         "  border-radius: 16px;"
         "  border-bottom-left-radius: 4px;"
         "  padding: 12px 16px;"
         "  font-size: %1px;"
         "  border-left: 3px solid #f85149;"
         "  font-family: 'Microsoft YaHei UI', 'Microsoft YaHei', 'Segoe UI', sans-serif;"
         "}")
         .arg(errorFont);
   }

   return QStringLiteral(
      "QTextBrowser {"
      "  background: qlineargradient(x1:0, y1:0, x2:0, y2:1,"
      "    stop:0 #1c2128, stop:0.5 #161b22, stop:1 #0d1117);"
      "  color: #e6edf3;"
      "  border-radius: 16px;"
      "  border-bottom-left-radius: 4px;"
      "  padding: 14px 18px;"
      "  font-size: %1px;"
      "  border: 1px solid rgba(48,54,61,0.8);"
      "  font-family: 'Microsoft YaHei UI', 'Microsoft YaHei', 'Segoe UI', sans-serif;"
      "}")
      .arg(baseFont);
}

void DockWidget::ApplyUiFontSize()
{
   const int baseFont = UiFontSize();
   const int smallFont = UiSmallFontSize();

   if (mInput) {
      mInput->setStyleSheet(QStringLiteral(
         "QPlainTextEdit {"
         "  background-color: transparent;"
         "  border: none;"
         "  padding: 12px 14px;"
         "  color: #e6edf3;"
         "  font-size: %1px;"
         "  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Helvetica, Arial, sans-serif;"
         "  selection-background-color: #264f78;"
         "  line-height: 1.5;"
         "}")
         .arg(baseFont));
   }

   if (mModelComboBox) {
      mModelComboBox->setStyleSheet(QStringLiteral(
         "QComboBox {"
         "  background: rgba(56,139,253,0.1);"
         "  color: #58a6ff;"
         "  border: 1px solid rgba(56,139,253,0.4);"
         "  border-radius: 6px;"
         "  padding: 4px 8px 4px 10px;"
         "  font-size: %1px;"
         "  font-weight: 500;"
         "  min-width: 100px;"
         "}"
         "QComboBox:hover {"
         "  background: rgba(56,139,253,0.2);"
         "}")
         .arg(smallFont));
   }

   if (mSettingsButton) {
      const int iconSize = qMax(14, baseFont + 4);
      mSettingsButton->setIconSize(QSize(iconSize, iconSize));
      mSettingsButton->setStyleSheet(QStringLiteral(
         "QPushButton {"
         "  background: transparent;"
         "  border: none;"
         "  border-radius: 6px;"
         "}"
         "QPushButton:hover {"
         "  background: rgba(139,148,158,0.15);"
         "}"));
   }

   if (mSendButton) {
      const int sendFont = qMax(11, baseFont - 1);
      mSendButton->setStyleSheet(QStringLiteral(
         "QPushButton {"
         "  background: qlineargradient(x1:0, y1:0, x2:0, y2:1,"
         "    stop:0 #388bfd, stop:1 #1f6feb);"
         "  border: none;"
         "  border-radius: 8px;"
         "  color: white;"
         "  font-size: %1px;"
         "  font-weight: 600;"
         "  letter-spacing: 0.3px;"
         "}"
         "QPushButton:hover {"
         "  background: qlineargradient(x1:0, y1:0, x2:0, y2:1,"
         "    stop:0 #58a6ff, stop:1 #388bfd);"
         "}"
         "QPushButton:pressed {"
         "  background: qlineargradient(x1:0, y1:0, x2:0, y2:1,"
         "    stop:0 #1f6feb, stop:1 #1158c7);"
         "}"
         "QPushButton:disabled {"
         "  background: #21262d;"
         "  color: #484f58;"
         "}")
         .arg(sendFont));
   }

   if (mStatusLabel) {
      mStatusLabel->setStyleSheet(QStringLiteral(
         "QLabel { color: #8b949e; font-style: italic; padding: 4px 16px;"
         " background: qlineargradient(x1:0, y1:0, x2:0, y2:1,"
         "   stop:0 #21262d, stop:1 #161b22);"
         " font-size: %1px; font-weight: 500; letter-spacing: 0.3px; }")
         .arg(smallFont));
   }

   if (mSessionTitle) {
      const int titleFont = qMax(11, baseFont - 1);
      mSessionTitle->setStyleSheet(QStringLiteral(
         "QLabel { color: #c9d1d9; font-size: %1px; font-weight: 600;"
         "  letter-spacing: 0.3px; background: transparent; }")
         .arg(titleFont));
   }

   if (mTokenUsageLabel) {
      const int tokenFont = qMax(9, baseFont - 3);
      mTokenUsageLabel->setStyleSheet(QStringLiteral(
         "QLabel { color: #8b949e; font-size: %1px; padding: 0 6px;"
         " background: transparent; }")
         .arg(tokenFont));
   }

   if (mSessionListBtn) {
      const int iconFont = qMax(12, baseFont + 2);
      mSessionListBtn->setStyleSheet(QStringLiteral(
         "QPushButton {"
         "  background: transparent;"
         "  border: none;"
         "  border-radius: 6px;"
         "  color: #8b949e;"
         "  font-size: %1px;"
         "  font-weight: bold;"
         "}"
         "QPushButton:hover {"
         "  background: rgba(139,148,158,0.15);"
         "  color: #e6edf3;"
         "}")
         .arg(iconFont));
   }

   if (mNewSessionBtn) {
      const int iconFont = qMax(12, baseFont + 2);
      mNewSessionBtn->setStyleSheet(QStringLiteral(
         "QPushButton {"
         "  background: rgba(56,139,253,0.1);"
         "  border: 1px solid rgba(56,139,253,0.4);"
         "  border-radius: 6px;"
         "  color: #58a6ff;"
         "  font-size: %1px;"
         "  font-weight: bold;"
         "}"
         "QPushButton:hover {"
         "  background: rgba(56,139,253,0.25);"
         "  border: 1px solid rgba(56,139,253,0.6);"
         "}")
         .arg(iconFont));
   }

   if (mApproveButton) {
      mApproveButton->setStyleSheet(QStringLiteral(
         "QPushButton {"
         "  background: rgba(52, 211, 153, 0.08);"
         "  color: #86efac;"
         "  border: 1px solid rgba(52, 211, 153, 0.25);"
         "  border-radius: 8px;"
         "  padding: 9px 28px;"
         "  font-weight: 600;"
         "  font-size: %1px;"
         "  letter-spacing: 0.3px;"
         "  min-width: 100px;"
         "  min-height: 34px;"
         "}"
         "QPushButton:hover {"
         "  background: rgba(52, 211, 153, 0.16);"
         "  border-color: rgba(52, 211, 153, 0.4);"
         "  color: #bbf7d0;"
         "}"
         "QPushButton:pressed {"
         "  background: #10b981;"
         "}"
         "QPushButton:focus {"
         "  outline: none;"
         "  border: 2px solid rgba(52,211,153,0.5);"
         "}")
         .arg(smallFont));
   }

   if (mRejectButton) {
      mRejectButton->setStyleSheet(QStringLiteral(
         "QPushButton {"
         "  background: rgba(239, 68, 68, 0.08);"
         "  color: #fca5a5;"
         "  border: 1px solid rgba(239, 68, 68, 0.25);"
         "  border-radius: 8px;"
         "  padding: 9px 28px;"
         "  font-weight: 600;"
         "  font-size: %1px;"
         "  letter-spacing: 0.3px;"
         "  min-width: 100px;"
         "  min-height: 34px;"
         "}"
         "QPushButton:hover {"
         "  background: rgba(239, 68, 68, 0.16);"
         "  border-color: rgba(239, 68, 68, 0.4);"
         "  color: #fecaca;"
         "}"
         "QPushButton:pressed {"
         "  background: rgba(239, 68, 68, 0.24);"
         "}"
         "QPushButton:focus {"
         "  outline: none;"
         "  border: 2px solid rgba(239,68,68,0.4);"
         "}")
         .arg(smallFont));
   }

   if (mMessagesContainer) {
      const auto bubbles = mMessagesContainer->findChildren<QTextBrowser*>();
      for (QTextBrowser* bubble : bubbles) {
         const QString role = bubble->property("bubbleRole").toString();
         bubble->setStyleSheet(BuildBubbleStyle(role));
      }
   }

   UpdateMessageSizes();
}

void DockWidget::StartTypingIndicator()
{
   mTypingTick = 0;
   mTypingActive = true;
   UpdateTypingIndicator();
   if (mTypingTimer) {
      mTypingTimer->start();
   }
}

void DockWidget::StopTypingIndicator()
{
   const bool wasActive = mTypingActive;
   mTypingTick = 0;
   mTypingActive = false;
   if (mTypingTimer) {
      mTypingTimer->stop();
   }
   // Do not insert a non-collapsible "Thinking Done" block
   if (wasActive && mStreamingMessage) {
      mAssistantBubbleHtml = mFrozenBubbleHtml + RenderMarkdown(mPendingAssistantText);
   }
}

void DockWidget::UpdateTypingIndicator()
{
   if (!mAwaitingAssistant || !mStreamingMessage || !mTypingActive) {
      return;
   }

   // If new text has already started arriving this turn, no indicator needed
   if (!mPendingAssistantText.isEmpty()) {
      return;
   }

   const int dots = (mTypingTick % 3) + 1;

   // Show "Thinking..." inline after the frozen content (which includes previous tool results)
   mStreamingMessage->setHtml(mFrozenBubbleHtml + BuildTypingHtml(dots));

   UpdateMessageSizes();
   ++mTypingTick;
}

QString DockWidget::BuildTypingHtml(int aDotCount) const
{
   const int smallFont = UiSmallFontSize();
   QString dotText;
   for (int i = 0; i < aDotCount; ++i) {
      dotText.append('.');
   }

   return QStringLiteral(
      "<div style='color:#8b949e; font-style:italic; font-size:%1px; line-height:1.6;"
      " font-family: Segoe UI, system-ui, sans-serif;'>Thinking%2</div>")
         .arg(smallFont)
         .arg(dotText);
}

QString DockWidget::BuildDeepThinkDoneHtml(int aBlockIndex,
                                           const QStringList& aThinkTexts,
                                           const QSet<int>& aExpandedBlocks) const
{
   const int smallFont = UiSmallFontSize();
   const bool expanded = aExpandedBlocks.contains(aBlockIndex);
   const QString arrow = expanded ? QStringLiteral("&#9660;") : QStringLiteral("&#9654;");
   const QString toggleUrl = QStringLiteral("aichat://think-expand-%1").arg(aBlockIndex);

   QString contentHtml;
   if (expanded && aBlockIndex >= 0 && aBlockIndex < aThinkTexts.size()) {
      QString text = aThinkTexts[aBlockIndex].toHtmlEscaped();
      text.replace(QStringLiteral("\n"), QStringLiteral("<br>"));
      contentHtml = QStringLiteral(
         "<div style='margin-top:4px; white-space:pre-wrap; color:#9aa4bf; font-size:%1px;"
         " max-height:300px; overflow:auto; border-top:1px dashed #3a3a4a; padding-top:4px;"
         " font-family: Consolas, monospace;'>%2</div>")
         .arg(smallFont)
         .arg(text);
   }

   return QStringLiteral(
      "<!--think-block-%1-->"
      "<div style='margin: 2px 0;'>"
      "<a href='%2' style='color:#8b949e; text-decoration:none; font-style:italic;"
      " font-size:%3px; font-family: Segoe UI, system-ui, sans-serif;'>"
      "%4 Thinking Done</a>"
      "%5"
      "</div>"
      "<!--/think-block-%1-->")
      .arg(aBlockIndex)
      .arg(toggleUrl)
      .arg(smallFont)
      .arg(arrow)
      .arg(contentHtml);
}

void DockWidget::RebuildThinkBlock(int aBlockIndex)
{
   const QString startMarker = QStringLiteral("<!--think-block-%1-->").arg(aBlockIndex);
   const QString endMarker   = QStringLiteral("<!--/think-block-%1-->").arg(aBlockIndex);

   int startIdx = mFrozenBubbleHtml.indexOf(startMarker);
   int endIdx   = mFrozenBubbleHtml.indexOf(endMarker, startIdx);
   if (startIdx < 0 || endIdx < 0) return;

   const int regionEnd = endIdx + endMarker.size();
   // Preserve scroll position when toggling think block expand/collapse
   auto* bar = mScrollArea ? mScrollArea->verticalScrollBar() : nullptr;
   const int savedScroll = bar ? bar->value() : 0;

   mFrozenBubbleHtml.replace(startIdx, regionEnd - startIdx,
                             BuildDeepThinkDoneHtml(aBlockIndex, mThinkBlockTexts, mExpandedThinkBlocks));
   mAssistantBubbleHtml = mFrozenBubbleHtml + RenderMarkdown(mPendingAssistantText);

   // Update content without auto-scrolling
   if (mStreamingMessage) {
      mStreamingMessage->setHtml(mAssistantBubbleHtml);
      UpdateMessageSizes();
   }

   // Restore exact scroll position so the view doesn't jump
   if (bar) {
      bar->setValue(savedScroll);
   }
}

void DockWidget::RebuildFinalizedThinkBlock(QTextBrowser* aBubble, int aBlockIndex)
{
   if (!aBubble) return;

   // Retrieve per-bubble stored think data
   QStringList texts = aBubble->property("thinkBlockTexts").toStringList();
   if (aBlockIndex < 0 || aBlockIndex >= texts.size()) return;

   // Retrieve expanded state
   QSet<int> expanded;
   const QVariantList expandedList = aBubble->property("expandedThinkBlocks").toList();
   for (const QVariant& v : expandedList) {
      expanded.insert(v.toInt());
   }

   // Toggle
   if (expanded.contains(aBlockIndex)) {
      expanded.remove(aBlockIndex);
   } else {
      expanded.insert(aBlockIndex);
   }

   // Store updated expanded state back on the bubble
   QVariantList updatedList;
   for (int e : expanded) updatedList.append(e);
   aBubble->setProperty("expandedThinkBlocks", updatedList);

   // Rebuild HTML from stored source
   QString html = aBubble->property("sourceHtml").toString();
   const QString startMarker = QStringLiteral("<!--think-block-%1-->").arg(aBlockIndex);
   const QString endMarker   = QStringLiteral("<!--/think-block-%1-->").arg(aBlockIndex);

   int startIdx = html.indexOf(startMarker);
   int endIdx   = html.indexOf(endMarker, startIdx);
   if (startIdx < 0 || endIdx < 0) return;

   const int regionEnd = endIdx + endMarker.size();

   // Preserve scroll position
   auto* bar = mScrollArea ? mScrollArea->verticalScrollBar() : nullptr;
   const int savedScroll = bar ? bar->value() : 0;

   html.replace(startIdx, regionEnd - startIdx,
                BuildDeepThinkDoneHtml(aBlockIndex, texts, expanded));

   // Update stored source and display
   aBubble->setProperty("sourceHtml", html);
   aBubble->setHtml(html);
   UpdateMessageSizes();

   if (bar) {
      bar->setValue(savedScroll);
   }
}

void DockWidget::AppendDebug(const QString& aMessage)
{
   if (mPrefObjectPtr && !mPrefObjectPtr->IsDebugEnabled()) {
      return;
   }
   EnsureDebugWindow();
   const QString line = QStringLiteral("[%1] %2")
      .arg(QDateTime::currentDateTime().toString(Qt::ISODate), aMessage);
   mDebugOutput->appendPlainText(line);
}

void DockWidget::EnsureDebugWindow()
{
   if (mDebugWindow) {
      return;
   }

   mDebugWindow = new QDialog(this);
   mDebugWindow->setWindowTitle(QStringLiteral("AIChat Debug"));
   mDebugWindow->resize(640, 360);

   auto* layout = new QVBoxLayout(mDebugWindow);
   mDebugOutput = new QPlainTextEdit(mDebugWindow);
   mDebugOutput->setReadOnly(true);
   mDebugOutput->setStyleSheet(
      "QPlainTextEdit { background: #1e1e1e; color: #d4d4d4; font-family: Consolas, monospace;"
      " font-size: 12px; }");
   layout->addWidget(mDebugOutput);
   mDebugWindow->setLayout(layout);
}

// ============================================================================
// Message Helpers
// ============================================================================

void DockWidget::AppendMessage(const QString& aRole, const QString& aContent)
{
   AddMessageWidget(aRole, aContent);
}

void DockWidget::AppendAssistantPlaceholder()
{
   mPendingAssistantText.clear();
   mAssistantBubbleHtml.clear();
   mFrozenBubbleHtml.clear();
   mInThinkBlock = false;
   mThinkText.clear();
   mThinkScrollOffset = 0;
   mThinkExpanded = false;
   mThinkIndicatorHtml.clear();
   mThinkBlockTexts.clear();
   mExpandedThinkBlocks.clear();
   mTypingActive = false;
   mLastStreamIndicatorMs = 0;   // reset throttle so first chunk shows immediately
   mAwaitingAssistant = true;
   mStreamingMessage = AddMessageWidget("AI", "");
   StartTypingIndicator();
}

void DockWidget::UpdateAssistantMessage()
{
   if (!mStreamingMessage) return;

   // Build the full bubble HTML: rendered markdown text + any tool snippets
   QString fullHtml = mAssistantBubbleHtml;

   // Only append think indicator when actively in a <think> block (inline position)
   if (mInThinkBlock && !mThinkIndicatorHtml.isEmpty()) {
      fullHtml += QStringLiteral("<div style='height:6px;'></div>");
      fullHtml += mThinkIndicatorHtml;
   }
   mStreamingMessage->setHtml(fullHtml);
   UpdateMessageSizes();
   ScrollToBottomIfNeeded();
}

void DockWidget::OnBubbleAnchorClicked(const QUrl& aUrl)
{
   if (aUrl.scheme() == QStringLiteral("aichat")) {
      const QString host = aUrl.host();
      if (host == QStringLiteral("think-toggle")) {
         mThinkExpanded = !mThinkExpanded;
         UpdateThinkScroll();
         return;
      }
      if (host.startsWith(QStringLiteral("think-expand-"))) {
         bool ok = false;
         const int idx = host.mid(13).toInt(&ok);
         if (!ok || idx < 0) return;

         auto* bubble = qobject_cast<QTextBrowser*>(sender());
         if (!bubble) return;

         if (bubble == mStreamingMessage) {
            // Current (active or just-finalized) bubble — use live state
            if (idx >= mThinkBlockTexts.size()) return;
            if (mExpandedThinkBlocks.contains(idx)) {
               mExpandedThinkBlocks.remove(idx);
            } else {
               mExpandedThinkBlocks.insert(idx);
            }
            RebuildThinkBlock(idx);
         } else {
            // Old finalized bubble — use per-bubble stored data
            RebuildFinalizedThinkBlock(bubble, idx);
         }
         return;
      }
   }

   QDesktopServices::openUrl(aUrl);
}

void DockWidget::AppendToAssistantBubble(const QString& aHtmlSnippet)
{
   if (!mStreamingMessage) return;
   if (aHtmlSnippet.contains("<!--tool-start-")) {
      // Avoid big gaps between text and tool indicators by trimming trailing breaks.
      static const QRegularExpression trailingBreaks("(?:\\s*<br\\s*/?>\\s*)+$");
      QRegularExpressionMatch match = trailingBreaks.match(mAssistantBubbleHtml);
      if (match.hasMatch()) {
         mAssistantBubbleHtml.chop(match.capturedLength());
      }
   }
   mAssistantBubbleHtml += aHtmlSnippet;
   UpdateAssistantMessage();
}

void DockWidget::FinalizeAssistantBubble()
{
   if (mInThinkBlock) {
      mInThinkBlock = false;
      StopThinkAnimation();
   }
   
   // Remove any leftover streaming progress indicator
   static const QString kStreamStart = QStringLiteral("<!--tool-stream-indicator-->");
   static const QString kStreamEnd   = QStringLiteral("<!--/tool-stream-indicator-->");
   if (mAssistantBubbleHtml.contains(kStreamStart)) {
      const int si = mAssistantBubbleHtml.indexOf(kStreamStart);
      const int ei = mAssistantBubbleHtml.indexOf(kStreamEnd, si);
      if (si >= 0 && ei > si) {
         mAssistantBubbleHtml.remove(si, ei + kStreamEnd.size() - si);
         UpdateAssistantMessage();
      }
   }

   // Store think data on the bubble so old messages can still expand/collapse
   if (mStreamingMessage && !mThinkBlockTexts.isEmpty()) {
      mStreamingMessage->setProperty("thinkBlockTexts", QVariant(mThinkBlockTexts));
      mStreamingMessage->setProperty("sourceHtml", QVariant(mAssistantBubbleHtml));
      mStreamingMessage->setProperty("expandedThinkBlocks", QVariantList());
   }

   mAwaitingAssistant = false;
   // Leave mStreamingMessage pointing to this bubble so that late tool results
   // (like inline review accept/reject) can still append to it.
   // It will be reset on the next AppendAssistantPlaceholder().
}

// ---------------------------------------------------------------------------
// Context divider
// ---------------------------------------------------------------------------
//
// Visual anatomy of the divider (dark-theme, full message-list width):
//
//   ──────────────────  ⊘  上下文已裁剪 · 移除 N 条  ──────────────────
//
// The two expanding lines are plain 1-px QFrames; the center chip is a
// borderline pill label with a tinted foreground colour. Nothing inside
// the divider contains interactive elements — it is purely decorative and
// is never removed once inserted.
//
void DockWidget::InsertContextDivider(const QString& aLabel, const QString& aBadgeColor)
{
   if (!mMessagesLayout || !mMessagesContainer) return;

   // ---- outer container ----
   auto* row = new QWidget(mMessagesContainer);
   row->setStyleSheet(QStringLiteral("background: transparent;"));
   row->setSizePolicy(QSizePolicy::Expanding, QSizePolicy::Fixed);

   auto* hLayout = new QHBoxLayout(row);
   hLayout->setContentsMargins(12, 18, 12, 18);
   hLayout->setSpacing(10);

   // Factory for the two thin separator lines
   auto makeLine = [row]() -> QFrame* {
      auto* line = new QFrame(row);
      line->setFrameShape(QFrame::NoFrame);
      line->setFixedHeight(1);
      line->setStyleSheet(QStringLiteral("background-color: #2d333b;"));
      line->setSizePolicy(QSizePolicy::Expanding, QSizePolicy::Fixed);
      return line;
   };

   // ---- centre badge / pill ----
   const int badgeFont = qMax(9, UiSmallFontSize());
   auto* badge = new QLabel(aLabel, row);
   badge->setContentsMargins(0, 0, 0, 0);
   badge->setAlignment(Qt::AlignCenter);
   badge->setSizePolicy(QSizePolicy::Minimum, QSizePolicy::Fixed);
   badge->setStyleSheet(
      QStringLiteral(
         "QLabel {"
         "  color: %1;"
         "  border: 1px solid %1;"
         "  border-radius: 10px;"
         "  padding: 2px 10px;"
         "  font-size: %2px;"
         "  font-weight: 500;"
         "  letter-spacing: 0.3px;"
         "  background: transparent;"
         "}"
      ).arg(aBadgeColor, QString::number(badgeFont)));

   hLayout->addWidget(makeLine());
   hLayout->addWidget(badge, 0, Qt::AlignVCenter);
   hLayout->addWidget(makeLine());
   row->setLayout(hLayout);

   mMessagesLayout->addWidget(row);
   ScrollToBottomIfNeeded();
}

QString DockWidget::BuildToolInlineHtml(const QString& aToolName, const QString& aStatus,
                                        bool aSuccess, bool aRunning) const
{
   const int toolFont = qMax(9, UiSmallFontSize() - 2);
   const int spinnerFont = qMax(8, toolFont - 1);
   // Map tool names to user-friendly display
   QString icon;
   QString label;
   QString detail = aStatus;

   if (aToolName == QStringLiteral("read_file")) {
      icon = QStringLiteral("[R]");
      label = QStringLiteral("Read file");
   } else if (aToolName == QStringLiteral("list_files")) {
      icon = QStringLiteral("[L]");
      label = QStringLiteral("List files");
   } else if (aToolName == QStringLiteral("search_files")) {
      icon = QStringLiteral("[S]");
      label = QStringLiteral("Search files");
   } else if (aToolName == QStringLiteral("write_to_file")) {
      icon = QStringLiteral("[W]");
      label = QStringLiteral("Write file");
   } else if (aToolName == QStringLiteral("replace_in_file")) {
      icon = QStringLiteral("[E]");
      label = QStringLiteral("Edit file");
   } else if (aToolName == QStringLiteral("delete_file")) {
      icon = QStringLiteral("[D]");
      label = QStringLiteral("Delete file");
   } else if (aToolName == QStringLiteral("insert_before")) {
      icon = QStringLiteral("[B]");
      label = QStringLiteral("Insert before");
   } else if (aToolName == QStringLiteral("insert_after")) {
      icon = QStringLiteral("[A]");
      label = QStringLiteral("Insert after");
   } else if (aToolName == QStringLiteral("execute_command")) {
      icon = QStringLiteral("[>]");
      label = QStringLiteral("Run command");
   } else if (aToolName == QStringLiteral("run_tests")) {
      icon = QStringLiteral("[T]");
      label = QStringLiteral("Run tests");
   } else if (aToolName == QStringLiteral("attempt_completion")) {
      icon = QStringLiteral("[+]");
      label = QStringLiteral("Completed");
   } else if (aToolName == QStringLiteral("set_startup_file")) {
      icon = QStringLiteral("[M]");
      label = QStringLiteral("Set startup file");
   } else if (aToolName == QStringLiteral("normalize_workspace_encoding")) {
      icon = QStringLiteral("[N]");
      label = QStringLiteral("Normalize encoding");
   } else if (aToolName == QStringLiteral("list_code_definition_names")) {
      icon = QStringLiteral("[C]");
      label = QStringLiteral("List definitions");
   } else if (aToolName == QStringLiteral("load_skill")) {
      icon = QStringLiteral("[K]");
      label = QStringLiteral("Load skill");
   } else {
      icon = QStringLiteral("[*]");
      label = aToolName;
   }

   // Clean up display text: extract useful info from userDisplayMessage
   if (detail.isEmpty()) {
      detail = label;
   }

   QString bgColor, borderColor, textColor, iconColor;
   if (aRunning) {
      bgColor     = QStringLiteral("rgba(56,139,253,0.08)");
      borderColor = QStringLiteral("rgba(56,139,253,0.3)");
      textColor   = QStringLiteral("#8b949e");
      iconColor   = QStringLiteral("#58a6ff");
   } else if (aSuccess) {
      bgColor     = QStringLiteral("rgba(46,160,67,0.08)");
      borderColor = QStringLiteral("rgba(46,160,67,0.25)");
      textColor   = QStringLiteral("#8b949e");
      iconColor   = QStringLiteral("#3fb950");
   } else {
      bgColor     = QStringLiteral("rgba(248,81,73,0.1)");
      borderColor = QStringLiteral("rgba(248,81,73,0.3)");
      textColor   = QStringLiteral("#ff7b72");
      iconColor   = QStringLiteral("#f85149");
   }

   QString spinner;
   if (aRunning) {
      spinner = QStringLiteral(
         " <span style='color:#58a6ff;font-size:%1px;'>"
         "<!--tool-spinner-->working...<!--/tool-spinner--></span>")
         .arg(spinnerFont);
   }

   // Use HTML comment markers so we can find & replace running indicators
   const QString startMarker = QStringLiteral("<!--tool-start-%1-->").arg(aToolName);
   const QString endMarker   = QStringLiteral("<!--tool-end-%1-->").arg(aToolName);

   return startMarker
      + QStringLiteral(
           "<div style='margin:4px 0; padding:5px 10px; background:%1; border:1px solid %2;"
           " border-radius:6px; font-size:%3px; color:%4; font-family:-apple-system,BlinkMacSystemFont,Segoe UI,Helvetica,sans-serif;'>"
           "<span style='color:%5;margin-right:8px;'>%6</span>"
           "<span style='font-weight:500;'>%7</span>%8"
           "</div>")
           .arg(bgColor, borderColor)
           .arg(toolFont)
           .arg(textColor)
           .arg(iconColor)
           .arg(icon)
           .arg(detail.toHtmlEscaped())
           .arg(spinner)
      + endMarker;
}

QTextBrowser* DockWidget::AddMessageWidget(const QString& aRole, const QString& aContent)
{
   auto* row = new QWidget(mMessagesContainer);
   row->setStyleSheet("background: transparent;");
   auto* rowLayout = new QHBoxLayout(row);
   rowLayout->setContentsMargins(0, 0, 0, 0);
   rowLayout->setSpacing(12);
   row->setSizePolicy(QSizePolicy::Expanding, QSizePolicy::Preferred);

   auto* bubble = new QTextBrowser(row);
   bubble->setOpenExternalLinks(false);
   bubble->setOpenLinks(false);
   bubble->setFrameShape(QFrame::NoFrame);
   bubble->setHorizontalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
   bubble->setVerticalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
   bubble->setLineWrapMode(QTextEdit::WidgetWidth);
   bubble->setWordWrapMode(QTextOption::WrapAtWordBoundaryOrAnywhere);
   bubble->setSizePolicy(QSizePolicy::Fixed, QSizePolicy::Fixed);
   bubble->setMinimumWidth(0);
   bubble->setSizeAdjustPolicy(QAbstractScrollArea::AdjustToContents);
   bubble->document()->setDocumentMargin(0);
   bubble->setHtml(RenderMarkdown(aContent));
   bubble->setProperty("bubbleRole", aRole);
   connect(bubble, &QTextBrowser::anchorClicked, this, &DockWidget::OnBubbleAnchorClicked);

   // Create avatar label helper
   const int avatarFont = qMax(10, UiFontSize());
   auto createAvatar = [row, avatarFont](const QString& text, const QString& bgColor, const QString& textColor) {
      auto* avatar = new QLabel(row);
      avatar->setFixedSize(32, 32);
      avatar->setAlignment(Qt::AlignCenter);
      avatar->setText(text);
      avatar->setStyleSheet(
         QString("QLabel { background: %1; color: %2; border-radius: 16px;"
                 " font-size: %3px; font-weight: 600; }")
            .arg(bgColor, textColor, QString::number(avatarFont)));
      return avatar;
   };

   if (aRole == "You") {
      // User message — right-aligned with gradient blue bubble
      bubble->setStyleSheet(BuildBubbleStyle(aRole));
      rowLayout->addStretch();
      rowLayout->addWidget(bubble);
      rowLayout->addWidget(createAvatar("U", "#1f6feb", "#ffffff"));
   } else if (aRole == "Error") {
      // Error — left-aligned with red accent
      bubble->setStyleSheet(BuildBubbleStyle(aRole));
      rowLayout->addWidget(createAvatar("!", "#f85149", "#ffffff"));
      rowLayout->addWidget(bubble);
      rowLayout->addStretch();
   } else {
      // AI / default — left-aligned with sleek dark styling
      bubble->setStyleSheet(BuildBubbleStyle(aRole));
      rowLayout->addWidget(createAvatar("AI", "#238636", "#ffffff"));
      rowLayout->addWidget(bubble);
      rowLayout->addStretch();
   }

   row->setLayout(rowLayout);
   mMessagesLayout->addWidget(row);

   // Immediate sizing pass for responsiveness
   UpdateMessageSizes();
   // Deferred sizing pass: the Qt layout engine may not have fully processed the new widget
   // yet (e.g., scrollbar visibility changed, viewport width stale). Schedule a second pass
   // after the event loop processes pending layout events so all sizes are correct.
   QTimer::singleShot(0, this, &DockWidget::UpdateMessageSizes);
   ScrollToBottomIfNeeded();
   return bubble;
}

void DockWidget::UpdateMessageSizes()
{
   if (!mScrollArea || !mMessagesContainer) return;

   const int viewportWidth = mScrollArea->viewport()->width();
   const int reservedWidth = 60; // avatar + spacing buffer
   const int maxBubbleWidth = qMin(800, qMax(280, static_cast<int>(viewportWidth * 0.88) - reservedWidth));

   mMessagesContainer->setMinimumWidth(viewportWidth);

   const auto bubbles = mMessagesContainer->findChildren<QTextBrowser*>();
   for (QTextBrowser* bubble : bubbles) {
      const QString role = bubble->property("bubbleRole").toString();

      // Padding compensation: must match CSS padding in BuildBubbleStyle
      //   You:   padding 12px 16px              → hPad=32, vPad=24
      //   AI:    padding 14px 18px + 1px border  → hPad=38, vPad=30
      //   Error: padding 12px 16px + 3px border  → hPad=35, vPad=24
      int hPad, vPad;
      if (role == "You") {
         hPad = 32; vPad = 24;
      } else if (role == "Error") {
         hPad = 35; vPad = 24;
      } else {
         hPad = 38; vPad = 30;
      }

      // Pass 1: measure ideal width without wrapping
      bubble->document()->setTextWidth(100000.0);
      const int idealWidth = static_cast<int>(bubble->document()->idealWidth());
      const int minWidth = (role == "You") ? 120 : 240;
      int bubbleWidth = qMin(maxBubbleWidth, qMax(minWidth, idealWidth + hPad));

      // AI bubbles always use maximum width so code blocks fill 100%.
      if (role != "You" && role != "Error") {
         bubbleWidth = maxBubbleWidth;
      }

      // Pass 2: wrap to bubble width and compute height
      bubble->setFixedWidth(bubbleWidth);
      bubble->document()->setTextWidth(bubbleWidth - hPad);
      const int docHeight = static_cast<int>(bubble->document()->size().height());
      bubble->setFixedHeight(docHeight + vPad);
   }
}

// ============================================================================
// Event Handling
// ============================================================================

void DockWidget::resizeEvent(QResizeEvent* aEvent)
{
   wkf::DockWidget::resizeEvent(aEvent);
   UpdateMessageSizes();
}

// ---------------------------------------------------------------------------
// Scroll helpers
// ---------------------------------------------------------------------------

bool DockWidget::IsScrolledToBottom() const
{
   if (!mScrollArea || !mScrollArea->verticalScrollBar()) return true;
   auto* bar = mScrollArea->verticalScrollBar();
   // Consider "at bottom" if within a small fraction of the viewport
   return (bar->maximum() - bar->value()) <= AutoScrollEpsilon();
}

int DockWidget::AutoScrollEpsilon() const
{
   if (!mScrollArea || !mScrollArea->verticalScrollBar()) return 0;
   auto* bar = mScrollArea->verticalScrollBar();
   return qMax(6, bar->pageStep() / 10);
}

void DockWidget::ScrollToBottomIfNeeded()
{
   if (!mScrollArea || !mScrollArea->verticalScrollBar()) return;
   if (!mAutoScrollEnabled) return;
   auto* bar = mScrollArea->verticalScrollBar();
   mAutoScrollInProgress = true;
   bar->setValue(bar->maximum());
   mAutoScrollInProgress = false;
}

bool DockWidget::eventFilter(QObject* aObject, QEvent* aEvent)
{
   if (aObject == mScrollArea->viewport() && aEvent->type() == QEvent::Resize) {
      UpdateMessageSizes();
   }

   // Handle Enter key for sending in the multi-line input
   if (aObject == mInput && aEvent->type() == QEvent::KeyPress) {
      auto* keyEvent = static_cast<QKeyEvent*>(aEvent);
      if (keyEvent->key() == Qt::Key_Return || keyEvent->key() == Qt::Key_Enter) {
         if (keyEvent->modifiers() & Qt::ShiftModifier) {
            // Shift+Enter: insert newline
            return false;
         }
         // Enter alone: send
         OnSendClicked();
         return true;
      }
   }

   return wkf::DockWidget::eventFilter(aObject, aEvent);
}

// ============================================================================
// Markdown Rendering (preserved from original)
// ============================================================================

QString DockWidget::RenderInlineMarkdown(const QString& aText) const
{
   const int codeFont = UiCodeFontSize();
   QString html = aText;

   // Bold: **text**
   html.replace(QRegularExpression("\\*\\*(.+?)\\*\\*"), "<b>\\1</b>");

   // Italic: *text*
   html.replace(QRegularExpression("\\*(.+?)\\*"), "<i>\\1</i>");

   // Inline code: `code`
   // QTextBrowser ignores padding/border-radius/border on inline elements,
   // so we use &nbsp; for visual padding around the code text.
   const QString inlineCode = QStringLiteral(
      "<span style='background-color:#2d2d3d;"
      " font-family:Consolas,monospace; font-size:%1px;"
      " color:#e0e0f0;'>&nbsp;\\1&nbsp;</span>")
      .arg(codeFont);
   html.replace(QRegularExpression("`([^`]+)`"), inlineCode);

   // Links: [text](url)
   html.replace(QRegularExpression("\\[([^\\]]+)\\]\\(([^\\)]+)\\)"),
      "<a href='\\2' style='color:#4fa3ff;'>\\1</a>");

   return html;
}

QString DockWidget::RenderMarkdown(const QString& aText) const
{
   const int baseFont = UiFontSize();
   const int codeFont = UiCodeFontSize();
   QString result;
   const QStringList lines = aText.split('\n');
   bool inCodeBlock = false;
   bool inTable = false;
   QString codeBlockContent;
   QString codeBlockLang;
   bool hasContent = false;
   bool lastBlank = false;

   for (int i = 0; i < lines.size(); ++i) {
      QString line = lines[i];

      // ---- Code block fencing ----
      if (line.trimmed().startsWith("```")) {
         if (inCodeBlock) {
            // Close code block: language header bar + code content
            QString langLabel = codeBlockLang.isEmpty()
               ? QStringLiteral("AFSIM")
               : codeBlockLang.toUpper();

            // Build code lines joined by <br> for QTextBrowser compatibility
            // (QTextBrowser adds paragraph spacing around \n in <pre>, causing gaps)
            QStringList codeLines = codeBlockContent.split('\n');
            QString codeHtml;
            for (int ci = 0; ci < codeLines.size(); ++ci) {
               if (ci > 0) codeHtml += QStringLiteral("<br>");
               // Preserve indentation: convert leading spaces to &nbsp;.
               // Also replace all interior spaces so QTextBrowser (which has no
               // white-space:pre support) does not collapse consecutive spaces.
               const QString& raw = codeLines[ci];
               int leadSpaces = 0;
               while (leadSpaces < raw.size() && raw.at(leadSpaces) == ' ') ++leadSpaces;
               QString escaped = raw.mid(leadSpaces).toHtmlEscaped();
               escaped.replace(QStringLiteral(" "), QStringLiteral("&nbsp;"));
               QString prefix;
               for (int si = 0; si < leadSpaces; ++si) prefix += QStringLiteral("&nbsp;");
               codeHtml += prefix + escaped;
            }

            result += QStringLiteral(
               "<table cellspacing='0' cellpadding='0' width='100%%' "
               "style='margin:8px 0 10px 0; border:1px solid #3a3a5c; "
               "border-collapse:collapse;'>"
               // --- Language header bar ---
               "<tr><td style='background:#2a2a4a; padding:7px 14px; "
               "font-family:Consolas,monospace; font-size:%3px; color:#8888cc; "
               "border-bottom:1px solid #3a3a5c; letter-spacing:0.5px;'>%1</td></tr>"
               // --- Code content ---
               "<tr><td style='background:#1a1a2e; padding:12px 14px; "
               "font-family:Consolas,monospace; "
               "font-size:%3px; color:#d4d4d4;'>%2</td></tr>"
               "</table>")
               .arg(langLabel)
               .arg(codeHtml)
               .arg(codeFont);
            codeBlockContent.clear();
            codeBlockLang.clear();
            inCodeBlock = false;
         } else {
            if (inTable) { result += "</table>"; inTable = false; }
            // Remove trailing <br> before code block (avoids blank gap)
            while (result.endsWith(QStringLiteral("<br>"))) {
               result.chop(4);
            }
            // Extract language identifier after ```
            QString fence = line.trimmed();
            codeBlockLang = fence.mid(3).trimmed();  // e.g. "afsim", "cpp", ""
            inCodeBlock = true;
         }
         continue;
      }

      if (inCodeBlock) {
         if (!codeBlockContent.isEmpty()) codeBlockContent += '\n';
         codeBlockContent += line;
         continue;
      }

      // Escape HTML
      line = line.toHtmlEscaped();

      // ---- Horizontal rule ----
      {
         static const QRegularExpression hrRegex("^\\s*([-*_])\\1{2,}\\s*$");
         if (hrRegex.match(line).hasMatch()) {
            if (inTable) { result += "</table>"; inTable = false; }
            result += "<hr style='border:none; border-top:1px solid #555; margin:10px 0;'>";
            continue;
         }
      }

      // ---- Table rows ----
      if (line.trimmed().startsWith('|') && line.trimmed().endsWith('|')) {
         static const QRegularExpression sepRegex("^\\s*\\|[\\s\\-:|]+\\|\\s*$");
         if (sepRegex.match(line).hasMatch()) continue;

         if (!inTable) {
            result += "<table style='border-collapse:collapse; margin:6px 0;' cellpadding='4'>";
            inTable = true;
         }

         QString trimmedLine = line.trimmed();
         if (trimmedLine.startsWith('|')) trimmedLine = trimmedLine.mid(1);
         if (trimmedLine.endsWith('|'))   trimmedLine.chop(1);
         const QStringList cells = trimmedLine.split('|');

         result += "<tr>";
         for (const QString& cell : cells) {
            result += QStringLiteral("<td style='border:1px solid #555; padding:4px 8px;'>%1</td>")
                        .arg(RenderInlineMarkdown(cell.trimmed()));
         }
         result += "</tr>";
         continue;
      } else if (inTable) {
         result += "</table>";
         inTable = false;
      }

      // ---- Headings ----
      {
         static const QRegularExpression headingRegex("^(#{1,6})\\s+(.+)$");
         auto match = headingRegex.match(line);
         if (match.hasMatch()) {
            const int level = match.captured(1).length();
            const QString text = RenderInlineMarkdown(match.captured(2));
            const int fontSize = qMax(baseFont + 2, baseFont + 10 - level * 2);
            result += QStringLiteral(
               "<p style='font-size:%1px; font-weight:bold; margin:10px 0 4px 0;"
               " color:#e0e0e0;'>%2</p>")
               .arg(fontSize).arg(text);
            continue;
         }
      }

      // ---- Unordered list ----
      {
         static const QRegularExpression ulRegex("^\\s*[\\-\\*\\+]\\s+(.+)$");
         auto match = ulRegex.match(line);
         if (match.hasMatch()) {
            result += "<p style='margin:3px 0 3px 20px; line-height:1.7;'>&bull; "
                      + RenderInlineMarkdown(match.captured(1)) + "</p>";
            continue;
         }
      }

      // ---- Ordered list ----
      {
         static const QRegularExpression olRegex("^\\s*(\\d+)\\.\\s+(.+)$");
         auto match = olRegex.match(line);
         if (match.hasMatch()) {
            result += "<p style='margin:3px 0 3px 20px; line-height:1.7;'>"
                      + match.captured(1) + ". "
                      + RenderInlineMarkdown(match.captured(2)) + "</p>";
            continue;
         }
      }

      // ---- Empty line ----
      if (line.trimmed().isEmpty()) {
         if (!hasContent) {
            continue; // drop leading blank lines
         }
         if (lastBlank) {
            continue; // collapse multiple blank lines
         }
         // Don't emit any HTML — just set the flag so the next paragraph
         // gets extra top margin.  Emitting <br> or <div>&nbsp;</div> in
         // QTextBrowser always produces at least one full line of whitespace
         // which looks like an ugly double-gap.
         lastBlank = true;
         continue;
      }

      hasContent = true;

      // ---- Regular paragraph ----
      // If preceded by a blank line, add extra top margin for visual paragraph break.
      const int topMargin = lastBlank ? 10 : 2;
      lastBlank = false;
      result += QStringLiteral("<p style='margin:%1px 0 2px 0; line-height:1.75;'>")
         .arg(topMargin) + RenderInlineMarkdown(line) + "</p>";
   }

   // Close any open blocks (streaming — code fence not yet closed)
   if (inCodeBlock) {
      QString langLabel = codeBlockLang.isEmpty()
         ? QStringLiteral("AFSIM")
         : codeBlockLang.toUpper();

      QStringList codeLines = codeBlockContent.split('\n');
      QString codeHtml;
      for (int ci = 0; ci < codeLines.size(); ++ci) {
         if (ci > 0) codeHtml += QStringLiteral("<br>");
         const QString& raw = codeLines[ci];
         int leadSpaces = 0;
         while (leadSpaces < raw.size() && raw.at(leadSpaces) == ' ') ++leadSpaces;
         QString escaped = raw.mid(leadSpaces).toHtmlEscaped();
         escaped.replace(QStringLiteral(" "), QStringLiteral("&nbsp;"));
         QString prefix;
         for (int si = 0; si < leadSpaces; ++si) prefix += QStringLiteral("&nbsp;");
         codeHtml += prefix + escaped;
      }

      result += QStringLiteral(
         "<table cellspacing='0' cellpadding='0' width='100%%' "
         "style='margin:8px 0 10px 0; border:1px solid #3a3a5c; "
         "border-collapse:collapse;'>"
         "<tr><td style='background:#2a2a4a; padding:7px 14px; "
         "font-family:Consolas,monospace; font-size:%3px; color:#8888cc; "
         "border-bottom:1px solid #3a3a5c; letter-spacing:0.5px;'>%1</td></tr>"
         "<tr><td style='background:#1a1a2e; padding:12px 14px; "
         "font-family:Consolas,monospace; "
         "font-size:%3px; color:#d4d4d4;'>%2</td></tr>"
         "</table>")
         .arg(langLabel)
         .arg(codeHtml)
         .arg(codeFont);
   }
   if (inTable) {
      result += "</table>";
   }

   return result;
}

void DockWidget::OverrideTitleBar(QWidget* /*aWidget*/)
{
   setTitleBarWidget(nullptr);
}

// ============================================================================
// Session Management
// ============================================================================

void DockWidget::OnNewSessionClicked()
{
   // Abort any running task
   if (mIsRunning) {
      if (mService) {
         mService->AbortTask();
      }
      StopStatusAnimation();
      StopTypingIndicator();
      UpdateSendButtonState(false);
   }

   // Create a new session
   if (mService) {
      mService->NewSession();
   }

   // Hide session list if visible
   if (mSessionListPanel && mSessionListPanel->isVisible()) {
      mSessionListPanel->setVisible(false);
   }

   // Re-enable input
   mSendButton->setEnabled(true);
   mInput->setEnabled(true);
   mInput->setFocus();
   mStatusLabel->clear();

   AppendDebug("New session created.");
}

void DockWidget::OnSessionListToggled()
{
   if (!mSessionListPanel) return;

   const bool willShow = !mSessionListPanel->isVisible();
   if (willShow) {
      PopulateSessionList();
   }
   mSessionListPanel->setVisible(willShow);
}

void DockWidget::OnSessionSelected(QListWidgetItem* aItem)
{
   if (!aItem || !mService) return;

   const QString sessionId = aItem->data(Qt::UserRole).toString();
   if (sessionId.isEmpty() || sessionId == mService->CurrentSessionId()) return;

   // Abort running task
   if (mIsRunning) {
      mService->AbortTask();
      StopStatusAnimation();
      StopTypingIndicator();
      UpdateSendButtonState(false);
   }

   // Load the selected session
   mService->LoadSession(sessionId);

   // Hide session list
   mSessionListPanel->setVisible(false);

   // Re-enable input
   mSendButton->setEnabled(true);
   mInput->setEnabled(true);
   mInput->setFocus();
   mStatusLabel->clear();

   AppendDebug(QStringLiteral("Switched to session: %1").arg(sessionId));
}

void DockWidget::OnSessionDeleteRequested(const QString& aSessionId)
{
   if (!mService) return;

   mService->DeleteSession(aSessionId);

   // Refresh the list
   PopulateSessionList();
}

void DockWidget::ClearConversationUi()
{
   // Remove all widgets from the messages layout
   while (mMessagesLayout->count() > 0)
   {
      QLayoutItem* item = mMessagesLayout->takeAt(0);
      if (item->widget()) {
         delete item->widget();
      }
      delete item;
   }

   // Re-add the initial stretch
   mMessagesLayout->addStretch(0);

   // Reset streaming state
   mStreamingMessage    = nullptr;
   mAwaitingAssistant   = false;
   mPendingAssistantText.clear();
   mAssistantBubbleHtml.clear();
   mFrozenBubbleHtml.clear();
   mThinkBlockTexts.clear();
   mExpandedThinkBlocks.clear();
   mInThinkBlock         = false;
   mThinkText.clear();
   mThinkTagRemainder.clear();
   mThinkIndicatorHtml.clear();
   mMessageCount        = 0;

   UpdateTitle();
}

void DockWidget::RestoreSessionMessages(const QList<ChatMessage>& aHistory)
{
   // Regex to strip <think>/<thinking> blocks (including unclosed ones)
   static const QRegularExpression thinkBlock(
      QStringLiteral("<think(?:ing)?>.*?</think(?:ing)?>"),
      QRegularExpression::DotMatchesEverythingOption | QRegularExpression::CaseInsensitiveOption);
   static const QRegularExpression unclosedThink(
      QStringLiteral("<think(?:ing)?>.*"),
      QRegularExpression::DotMatchesEverythingOption | QRegularExpression::CaseInsensitiveOption);

   for (int i = 0; i < aHistory.size(); ++i)
   {
      const auto& msg = aHistory[i];

      if (msg.mRole == MessageRole::User)
      {
         AppendMessage("You", msg.mContent);
         mMessageCount++;
      }
      else if (msg.mRole == MessageRole::Assistant && !msg.mContent.isEmpty())
      {
         // Determine if this is the last assistant message in the current "turn",
         // i.e., no subsequent assistant message exists before the next user
         // message or end of history.  Only display that final response to avoid
         // showing intermediate tool-call planning messages.
         bool isFinalAssistantInTurn = true;
         for (int j = i + 1; j < aHistory.size(); ++j)
         {
            if (aHistory[j].mRole == MessageRole::User)
               break;  // reached the next user turn — this is the last assistant
            if (aHistory[j].mRole == MessageRole::Assistant)
            {
               isFinalAssistantInTurn = false;
               break;  // another assistant message follows — skip this one
            }
         }

         if (!isFinalAssistantInTurn)
            continue;

         // Strip <think>/<thinking> tags so only the visible response is shown
         QString displayContent = msg.mContent;
         displayContent.remove(thinkBlock);
         displayContent.remove(unclosedThink);
         displayContent = displayContent.trimmed();

         if (!displayContent.isEmpty())
         {
            AppendMessage("AI", displayContent);
            mMessageCount++;
         }
      }
   }
   UpdateTitle();
}

void DockWidget::PopulateSessionList()
{
   if (!mSessionListWidget || !mService) return;

   mSessionListWidget->clear();

   const auto sessions = mService->ListSessions();
   const QString currentId = mService->CurrentSessionId();

   for (const auto& session : sessions)
   {
      // Skip current session — it's already visible in the chat area
      if (session.id == currentId) continue;

      // Skip sessions with no messages
      if (session.messageCount <= 0) continue;

      const QString dateStr = session.updatedAt.toString("yyyy-MM-dd hh:mm");
      const QString label   = QStringLiteral("%1\n%2  \u00B7  %3 msgs")
                                 .arg(session.title, dateStr)
                                 .arg(session.messageCount);

      auto* item = new QListWidgetItem(label, mSessionListWidget);
      item->setData(Qt::UserRole, session.id);
      item->setToolTip(QStringLiteral("Click to restore this session\nRight-click to delete"));

      // Enable multi-line display by setting appropriate size hint
      QSize hint = item->sizeHint();
      hint.setHeight(48);
      item->setSizeHint(hint);
   }

   if (mSessionListWidget->count() == 0) {
      auto* item = new QListWidgetItem(QStringLiteral("No saved sessions"), mSessionListWidget);
      item->setFlags(Qt::NoItemFlags);
      item->setForeground(QColor("#8b949e"));
   }
}

void DockWidget::UpdateSessionTitle(const QString& aTitle)
{
   if (mSessionTitle) {
      mSessionTitle->setText(aTitle);
   }
}

} // namespace AiChat
