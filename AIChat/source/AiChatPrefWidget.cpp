// -----------------------------------------------------------------------------
// File: AiChatPrefWidget.cpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#include "AiChatPrefWidget.hpp"

#include <QCheckBox>
#include <QComboBox>
#include <QFormLayout>
#include <QGroupBox>
#include <QHBoxLayout>
#include <QJsonArray>
#include <QJsonDocument>
#include <QJsonObject>
#include <QLabel>
#include <QLineEdit>
#include <QNetworkAccessManager>
#include <QNetworkReply>
#include <QNetworkRequest>
#include <QPlainTextEdit>
#include <QPushButton>
#include <QSpinBox>
#include <QTimer>
#include <QVBoxLayout>

namespace AiChat
{

PrefWidget::PrefWidget(QWidget* aParent)
   : PrefWidgetT<PrefObject>(aParent)
   , mBaseUrlEdit(nullptr)
   , mApiKeyEdit(nullptr)
   , mModelCombo(nullptr)
   , mTimeoutSpin(nullptr)
   , mAutoApplyCheck(nullptr)
   , mAutoApproveReadLocalCheck(nullptr)
   , mAutoApproveReadExternalCheck(nullptr)
   , mAutoApproveWriteLocalCheck(nullptr)
   , mAutoApproveCommandSafeCheck(nullptr)
   , mAutoApproveCommandAllCheck(nullptr)
   , mAllowExternalReadCheck(nullptr)
   , mDebugCheck(nullptr)
   , mUiFontSizeSpin(nullptr)
   , mCustomInstructionsEdit(nullptr)
   , mMaxIterationsSpin(nullptr)
   , mContextWindowCombo(nullptr)
   , mAutoCondenseCheck(nullptr)
   , mSearchExtensionsEdit(nullptr)
   , mIgnorePatternsEdit(nullptr)
   , mRefreshModelsBtn(nullptr)
   , mOpenDebugBtn(nullptr)
   , mReloadSkillsBtn(nullptr)
   , mNetworkManager(new QNetworkAccessManager(this))
   , mStatusLabel(nullptr)
{
   setWindowTitle("AI Chat");

   auto* mainLayout = new QVBoxLayout(this);

   // API Configuration Group
   auto* apiGroup  = new QGroupBox("AI API Configuration", this);
   auto* apiLayout = new QFormLayout(apiGroup);

   // Base URL
   mBaseUrlEdit = new QLineEdit(apiGroup);
   mBaseUrlEdit->setPlaceholderText("https://dashscope.aliyuncs.com/compatible-mode/v1");
   apiLayout->addRow("Base URL:", mBaseUrlEdit);

   // API Key
   mApiKeyEdit = new QLineEdit(apiGroup);
//    mApiKeyEdit->setEchoMode(QLineEdit::Password);
   mApiKeyEdit->setPlaceholderText("Enter your API key here...");
   apiLayout->addRow("API Key:", mApiKeyEdit);

   // Model selection with refresh button
   auto* modelRow = new QWidget(apiGroup);
   auto* modelLayout = new QHBoxLayout(modelRow);
   modelLayout->setContentsMargins(0, 0, 0, 0);
   modelLayout->setSpacing(6);
   
   mModelCombo = new QComboBox(modelRow);
   mModelCombo->setEditable(true);
   if (mPrefObjectPtr && !mPrefObjectPtr->GetAvailableModels().isEmpty()) {
      mModelCombo->addItems(mPrefObjectPtr->GetAvailableModels());
   } else if (mPrefObjectPtr && !mPrefObjectPtr->GetBaseUrl().isEmpty()) {
      const QString currentModel = mPrefObjectPtr->GetModel();
      if (!currentModel.isEmpty()) {
         mModelCombo->addItem(currentModel);
      }
   }
   mModelCombo->setSizePolicy(QSizePolicy::Expanding, QSizePolicy::Fixed);
   modelLayout->addWidget(mModelCombo);
   
   mRefreshModelsBtn = new QPushButton("Refresh", modelRow);
   mRefreshModelsBtn->setToolTip("Fetch available models from API");
   mRefreshModelsBtn->setFixedWidth(70);
      connect(mRefreshModelsBtn, QOverload<bool>::of(&QPushButton::clicked),
         this, [this](bool) { FetchModels(); });
   modelLayout->addWidget(mRefreshModelsBtn);

   if (mPrefObjectPtr) {
      connect(mPrefObjectPtr.get(), &PrefObject::AvailableModelsChanged,
              this, [this](const QStringList& models) { OnAvailableModelsChanged(models); });
   }
   
   apiLayout->addRow("Model:", modelRow);

   // Timeout
   mTimeoutSpin = new QSpinBox(apiGroup);
   mTimeoutSpin->setRange(5000, 300000);
   mTimeoutSpin->setSingleStep(5000);
   mTimeoutSpin->setSuffix(" ms");
   mTimeoutSpin->setValue(180000);
   apiLayout->addRow("Timeout:", mTimeoutSpin);

   apiGroup->setLayout(apiLayout);
   mainLayout->addWidget(apiGroup);

   // Smart Coding Group
   auto* smartGroup  = new QGroupBox("Smart Coding Settings", this);
   auto* smartLayout = new QFormLayout(smartGroup);

   // Max iterations
   mMaxIterationsSpin = new QSpinBox(smartGroup);
   mMaxIterationsSpin->setRange(1, 100);
   mMaxIterationsSpin->setSingleStep(5);
   mMaxIterationsSpin->setValue(PrefData::cDEFAULT_MAX_ITERATIONS);
   mMaxIterationsSpin->setToolTip(
      "Maximum number of LLM ↔ tool call iterations per task.\n"
      "Higher values allow more complex tasks but consume more API tokens.");
   smartLayout->addRow("Max Iterations:", mMaxIterationsSpin);

   // Context window size
   mContextWindowCombo = new QComboBox(smartGroup);
   mContextWindowCombo->addItem("32K",   32000);
   mContextWindowCombo->addItem("64K",   64000);
   mContextWindowCombo->addItem("128K",  128000);
   mContextWindowCombo->addItem("200K",  200000);
   mContextWindowCombo->addItem("1M",    1000000);
   mContextWindowCombo->setCurrentIndex(2); // default 128K
   mContextWindowCombo->setToolTip(
      "Model context window size in tokens.\n"
      "Used to compute the safety buffer for context truncation.");
   smartLayout->addRow("Context Window:", mContextWindowCombo);

   // Auto-condense (LLM auto-summary)
   mAutoCondenseCheck = new QCheckBox("Enable auto-condense (experimental)", smartGroup);
   mAutoCondenseCheck->setToolTip(
      "When enabled, uses the LLM to generate a structured summary\n"
      "of the conversation when context usage exceeds the threshold,\n"
      "preserving key information while freeing context space.\n"
      "This consumes additional API tokens.");
   mAutoCondenseCheck->setChecked(false);
   smartLayout->addRow(mAutoCondenseCheck);

   // Search file extensions
   mSearchExtensionsEdit = new QLineEdit(smartGroup);
   mSearchExtensionsEdit->setPlaceholderText("txt, ag, wsf, py, cpp, hpp, c, log");
   mSearchExtensionsEdit->setToolTip(
      "Comma/space-separated file extensions used by search_files.\n"
      "Example: txt, wsf, py, cpp, hpp, c, log");
   smartLayout->addRow("Search Extensions:", mSearchExtensionsEdit);

   // Custom instructions
   mCustomInstructionsEdit = new QPlainTextEdit(smartGroup);
   mCustomInstructionsEdit->setPlaceholderText(
      "Add custom instructions that will be appended to the system prompt.\n"
      "For example: 'Always write comments in Chinese', 'Prefer WSF_PLATFORM over "
      "platform_type', etc.");
   mCustomInstructionsEdit->setMaximumHeight(100);
   smartLayout->addRow("Custom Instructions:", mCustomInstructionsEdit);

   smartGroup->setLayout(smartLayout);
   mainLayout->addWidget(smartGroup);

   // Access and Approval Group
   auto* accessGroup = new QGroupBox("Access and Approval", this);
   auto* accessLayout = new QFormLayout(accessGroup);

   mAllowExternalReadCheck = new QCheckBox("Allow read/list/search outside workspace root", accessGroup);
   mAllowExternalReadCheck->setToolTip(
      "External access is disabled in strict workspace mode.");
   mAllowExternalReadCheck->setEnabled(false);
   accessLayout->addRow(mAllowExternalReadCheck);

   mAutoApproveReadLocalCheck = new QCheckBox("Auto-approve read/list/search (local)", accessGroup);
   mAutoApproveReadLocalCheck->setToolTip(
      "Automatically approve read_file, list_files, and search_files within the workspace root.");
   accessLayout->addRow(mAutoApproveReadLocalCheck);

   mAutoApproveReadExternalCheck = new QCheckBox("Auto-approve read/list/search (external)", accessGroup);
   mAutoApproveReadExternalCheck->setToolTip(
      "External access is disabled in strict workspace mode.");
   mAutoApproveReadExternalCheck->setEnabled(false);
   accessLayout->addRow(mAutoApproveReadExternalCheck);

   mAutoApproveWriteLocalCheck = new QCheckBox("Auto-approve write/replace (local)", accessGroup);
   mAutoApproveWriteLocalCheck->setToolTip(
      "Automatically approve write_to_file, replace_in_file, and set_startup_file within the workspace root.");
   accessLayout->addRow(mAutoApproveWriteLocalCheck);

   mAutoApproveCommandSafeCheck = new QCheckBox("Auto-approve safe commands", accessGroup);
   mAutoApproveCommandSafeCheck->setToolTip(
      "Automatically approve commands considered safe (read-only list/search commands).");
   accessLayout->addRow(mAutoApproveCommandSafeCheck);

   mAutoApproveCommandAllCheck = new QCheckBox("Auto-approve all commands", accessGroup);
   mAutoApproveCommandAllCheck->setToolTip(
      "Automatically approve any command, including risky operations. Use with caution.");
   accessLayout->addRow(mAutoApproveCommandAllCheck);

   mIgnorePatternsEdit = new QPlainTextEdit(accessGroup);
   mIgnorePatternsEdit->setPlaceholderText(
      "Ignore patterns (one per line or comma-separated).\n"
      "Supports *, ?, and **. Prefix with ! to unignore.");
   mIgnorePatternsEdit->setMaximumHeight(80);
   accessLayout->addRow("Ignore Patterns:", mIgnorePatternsEdit);

   accessGroup->setLayout(accessLayout);
   mainLayout->addWidget(accessGroup);

   // Behavior Group
   auto* behaviorGroup  = new QGroupBox("Behavior", this);
   auto* behaviorLayout = new QVBoxLayout(behaviorGroup);

   // UI font size
   auto* fontRow = new QWidget(behaviorGroup);
   auto* fontLayout = new QHBoxLayout(fontRow);
   fontLayout->setContentsMargins(0, 0, 0, 0);
   fontLayout->setSpacing(8);
   auto* fontLabel = new QLabel("UI Font Size:", fontRow);
   mUiFontSizeSpin = new QSpinBox(fontRow);
   mUiFontSizeSpin->setRange(10, 22);
   mUiFontSizeSpin->setSingleStep(1);
   mUiFontSizeSpin->setSuffix(" px");
   mUiFontSizeSpin->setValue(PrefData::cDEFAULT_UI_FONT_SIZE);
   mUiFontSizeSpin->setToolTip("Adjust the AI Chat UI font size.");
   fontLayout->addWidget(fontLabel);
   fontLayout->addWidget(mUiFontSizeSpin);
   fontLayout->addStretch();
   behaviorLayout->addWidget(fontRow);

   mDebugCheck = new QCheckBox("Enable debug log for AI Chat", behaviorGroup);
   mDebugCheck->setToolTip("When enabled, debug messages are logged during AI operations.");
   behaviorLayout->addWidget(mDebugCheck);

   mOpenDebugBtn = new QPushButton("Open Debug Window", behaviorGroup);
   mOpenDebugBtn->setToolTip("Open the debug log window to view AI Chat debug messages.");
   connect(mOpenDebugBtn, &QPushButton::clicked, this, [this]() {
      if (mPrefObjectPtr) {
         // Enable debug and emit signal to open debug window
         mPrefObjectPtr->SetDebugEnabled(true);
         mDebugCheck->setChecked(true);
         emit mPrefObjectPtr->OpenDebugWindowRequested();
      }
   });
   behaviorLayout->addWidget(mOpenDebugBtn);

   mReloadSkillsBtn = new QPushButton("Reload Skills", behaviorGroup);
   mReloadSkillsBtn->setToolTip("Reload skills from the project skill folders.");
   connect(mReloadSkillsBtn, &QPushButton::clicked, this, [this]() {
      if (mPrefObjectPtr) {
         emit mPrefObjectPtr->ReloadSkillsRequested();
      }
   });
   behaviorLayout->addWidget(mReloadSkillsBtn);

   mAutoApplyCheck = new QCheckBox(
      "Automatically approve all tool operations (use with caution)", behaviorGroup);
   mAutoApplyCheck->setToolTip(
      "When enabled, file writes, edits, and command execution will be\n"
      "applied without asking for confirmation. Not recommended for\n"
      "untrusted models or critical project files.");
   behaviorLayout->addWidget(mAutoApplyCheck);

   auto* warningLabel = new QLabel(
      "<i>Note: When auto-approve is disabled, write operations and command execution "
      "will require your explicit approval before proceeding.</i>",
      behaviorGroup);
   warningLabel->setWordWrap(true);
   behaviorLayout->addWidget(warningLabel);

   behaviorGroup->setLayout(behaviorLayout);
   mainLayout->addWidget(behaviorGroup);

   // Status label for toast messages
   mStatusLabel = new QLabel(this);
   mStatusLabel->setStyleSheet(
      "QLabel { color: #8b949e; font-style: italic; padding: 4px; }");
   mStatusLabel->setAlignment(Qt::AlignCenter);
   mainLayout->addWidget(mStatusLabel);

   // Spacer
   mainLayout->addStretch();

   setLayout(mainLayout);
}

void PrefWidget::ReadPreferenceData(const PrefData& aPrefData)
{
   mBaseUrlEdit->setText(aPrefData.mBaseUrl);
   mApiKeyEdit->setText(aPrefData.mApiKey);
   
   int modelIndex = mModelCombo->findText(aPrefData.mModel);
   if (modelIndex >= 0) {
      mModelCombo->setCurrentIndex(modelIndex);
   } else {
      mModelCombo->setCurrentText(aPrefData.mModel);
   }
   
   mTimeoutSpin->setValue(aPrefData.mTimeoutMs);
   mAutoApplyCheck->setChecked(aPrefData.mAutoApply);
   mAutoApproveReadLocalCheck->setChecked(aPrefData.mAutoApproveReadLocal);
   mAutoApproveReadExternalCheck->setChecked(aPrefData.mAutoApproveReadExternal);
   mAutoApproveWriteLocalCheck->setChecked(aPrefData.mAutoApproveWriteLocal);
   mAutoApproveCommandSafeCheck->setChecked(aPrefData.mAutoApproveCommandSafe);
   mAutoApproveCommandAllCheck->setChecked(aPrefData.mAutoApproveCommandAll);
   mAllowExternalReadCheck->setChecked(aPrefData.mAllowExternalRead);
   mDebugCheck->setChecked(aPrefData.mDebugEnabled);
   mUiFontSizeSpin->setValue(aPrefData.mUiFontSize);
   mCustomInstructionsEdit->setPlainText(aPrefData.mCustomInstructions);
   mMaxIterationsSpin->setValue(aPrefData.mMaxIterations);
   mSearchExtensionsEdit->setText(aPrefData.mSearchExtensions);
   mIgnorePatternsEdit->setPlainText(aPrefData.mIgnorePatterns);

   // Context window
   int cwIndex = mContextWindowCombo->findData(aPrefData.mContextWindowSize);
   if (cwIndex >= 0) {
      mContextWindowCombo->setCurrentIndex(cwIndex);
   }
   mAutoCondenseCheck->setChecked(aPrefData.mAutoCondenseEnabled);
}

void PrefWidget::WritePreferenceData(PrefData& aPrefData)
{
   aPrefData.mBaseUrl            = mBaseUrlEdit->text();
   aPrefData.mApiKey             = mApiKeyEdit->text();
   aPrefData.mModel              = mModelCombo->currentText();
   aPrefData.mTimeoutMs          = mTimeoutSpin->value();
   aPrefData.mAutoApply          = mAutoApplyCheck->isChecked();
   aPrefData.mAutoApproveReadLocal = mAutoApproveReadLocalCheck->isChecked();
   aPrefData.mAutoApproveReadExternal = mAutoApproveReadExternalCheck->isChecked();
   aPrefData.mAutoApproveWriteLocal = mAutoApproveWriteLocalCheck->isChecked();
   aPrefData.mAutoApproveCommandSafe = mAutoApproveCommandSafeCheck->isChecked();
   aPrefData.mAutoApproveCommandAll = mAutoApproveCommandAllCheck->isChecked();
   aPrefData.mAllowExternalRead = mAllowExternalReadCheck->isChecked();
   aPrefData.mDebugEnabled       = mDebugCheck->isChecked();
   aPrefData.mUiFontSize         = mUiFontSizeSpin->value();
   aPrefData.mCustomInstructions = mCustomInstructionsEdit->toPlainText();
   aPrefData.mMaxIterations      = mMaxIterationsSpin->value();
   aPrefData.mSearchExtensions   = mSearchExtensionsEdit->text();
   aPrefData.mIgnorePatterns     = mIgnorePatternsEdit->toPlainText();
   aPrefData.mContextWindowSize  = mContextWindowCombo->currentData().toInt();
   aPrefData.mAutoCondenseEnabled = mAutoCondenseCheck->isChecked();
}

void PrefWidget::OnAvailableModelsChanged(const QStringList& aModels)
{
   if (aModels.isEmpty() || !mModelCombo) return;
   const QString current = mModelCombo->currentText();
   mModelCombo->blockSignals(true);
   mModelCombo->clear();
   mModelCombo->addItems(aModels);
   const int idx = mModelCombo->findText(current);
   if (idx >= 0) {
      mModelCombo->setCurrentIndex(idx);
   } else {
      mModelCombo->setCurrentText(current);
   }
   mModelCombo->blockSignals(false);
}

void PrefWidget::FetchModels()
{
   QString baseUrl = mBaseUrlEdit->text().trimmed();
   if (baseUrl.isEmpty()) {
      mStatusLabel->setStyleSheet("QLabel { color: #f85149; font-style: italic; padding: 4px; }");
      mStatusLabel->setText("Please enter a Base URL first.");
      QTimer::singleShot(3000, this, [this]() { mStatusLabel->clear(); });
      return;
   }

   if (!baseUrl.endsWith('/')) {
      baseUrl += '/';
   }
   
   QUrl url(baseUrl + "models");
   if (!url.isValid()) {
      mStatusLabel->setStyleSheet("QLabel { color: #f85149; font-style: italic; padding: 4px; }");
      mStatusLabel->setText("Invalid URL format.");
      QTimer::singleShot(3000, this, [this]() { mStatusLabel->clear(); });
      return;
   }

   mRefreshModelsBtn->setEnabled(false);
   mRefreshModelsBtn->setText("...");
   mStatusLabel->setStyleSheet("QLabel { color: #58a6ff; font-style: italic; padding: 4px; }");
   mStatusLabel->setText("Fetching models...");

   QNetworkRequest request(url);
   request.setHeader(QNetworkRequest::ContentTypeHeader, "application/json");
   
   // Add API key if available
   const QString apiKey = mApiKeyEdit->text().trimmed();
   if (!apiKey.isEmpty()) {
      request.setRawHeader("Authorization", QString("Bearer %1").arg(apiKey).toUtf8());
   }

   QNetworkReply* reply = mNetworkManager->get(request);
   
   connect(reply, &QNetworkReply::finished, this, [this, reply]() {
      reply->deleteLater();
      mRefreshModelsBtn->setEnabled(true);
      mRefreshModelsBtn->setText("Refresh");
      
      if (reply->error() != QNetworkReply::NoError) {
         mStatusLabel->setStyleSheet("QLabel { color: #f85149; font-style: italic; padding: 4px; }");
         mStatusLabel->setText(QString("Failed: %1").arg(reply->errorString()));
         QTimer::singleShot(5000, this, [this]() { mStatusLabel->clear(); });
         return;
      }

      const QByteArray data = reply->readAll();
      QJsonParseError parseError;
      QJsonDocument doc = QJsonDocument::fromJson(data, &parseError);
      if (parseError.error != QJsonParseError::NoError) {
         mStatusLabel->setStyleSheet("QLabel { color: #f85149; font-style: italic; padding: 4px; }");
         mStatusLabel->setText("Failed to parse response.");
         QTimer::singleShot(3000, this, [this]() { mStatusLabel->clear(); });
         return;
      }

      QStringList models;
      const QJsonObject root = doc.object();
      const QJsonArray dataArray = root.value("data").toArray();
      
      for (const QJsonValue& val : dataArray) {
         const QJsonObject modelObj = val.toObject();
         const QString modelId = modelObj.value("id").toString();
         if (!modelId.isEmpty()) {
            models.append(modelId);
         }
      }

      if (!models.isEmpty()) {
         models.sort();
         
         // Preserve current selection
         const QString current = mModelCombo->currentText();
         
         mModelCombo->clear();
         mModelCombo->addItems(models);
         
         // Restore selection
         int idx = mModelCombo->findText(current);
         if (idx >= 0) {
            mModelCombo->setCurrentIndex(idx);
         } else {
            mModelCombo->setCurrentText(current);
         }
         
         // Update PrefObject with new models list
         if (mPrefObjectPtr) {
            mPrefObjectPtr->SetAvailableModels(models);
         }
         
         mStatusLabel->setStyleSheet("QLabel { color: #3fb950; font-style: italic; padding: 4px; }");
         mStatusLabel->setText(QString("Loaded %1 models.").arg(models.size()));
         QTimer::singleShot(3000, this, [this]() { mStatusLabel->clear(); });
      } else {
         mStatusLabel->setStyleSheet("QLabel { color: #d29922; font-style: italic; padding: 4px; }");
         mStatusLabel->setText("No models found.");
         QTimer::singleShot(3000, this, [this]() { mStatusLabel->clear(); });
      }
   });
}

} // namespace AiChat
