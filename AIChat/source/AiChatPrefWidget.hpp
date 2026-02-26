// -----------------------------------------------------------------------------
// File: AiChatPrefWidget.hpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#ifndef AICHAT_PREF_WIDGET_HPP
#define AICHAT_PREF_WIDGET_HPP

#include "WkfPrefWidget.hpp"
#include "AiChatPrefObject.hpp"

class QLineEdit;
class QSpinBox;
class QCheckBox;
class QComboBox;
class QPlainTextEdit;
class QPushButton;
class QNetworkAccessManager;
class QLabel;

namespace AiChat
{

//! The widget displaying and allowing modifications of the AI Chat preferences
class PrefWidget : public wkf::PrefWidgetT<PrefObject>
{
   Q_OBJECT
public:
   //! Constructs a PrefWidget
   //! @param aParent is the parent QWidget
   explicit PrefWidget(QWidget* aParent = nullptr);
   //! Destructs a PrefWidget
   ~PrefWidget() noexcept override = default;

private:
   //! Reads the preference data from the given PrefData
   void ReadPreferenceData(const PrefData& aPrefData) override;
   //! Writes the preference data from the PrefWidget to the given PrefData
   void WritePreferenceData(PrefData& aPrefData) override;

   //! React to dynamic model list updates
   void OnAvailableModelsChanged(const QStringList& aModels);

   //! Fetch available models from API
   void FetchModels();

   //! Base URL input
   QLineEdit* mBaseUrlEdit;
   //! API Key input
   QLineEdit* mApiKeyEdit;
   //! Model selection
   QComboBox* mModelCombo;
   //! Request timeout
   QSpinBox* mTimeoutSpin;
   //! Auto-apply checkbox
   QCheckBox* mAutoApplyCheck;
   //! Auto-approve read (local)
   QCheckBox* mAutoApproveReadLocalCheck;
   //! Auto-approve read (external)
   QCheckBox* mAutoApproveReadExternalCheck;
   //! Auto-approve write (local)
   QCheckBox* mAutoApproveWriteLocalCheck;
   //! Auto-approve safe commands
   QCheckBox* mAutoApproveCommandSafeCheck;
   //! Auto-approve all commands
   QCheckBox* mAutoApproveCommandAllCheck;
   //! Allow external reads
   QCheckBox* mAllowExternalReadCheck;
   //! Debug enabled checkbox
   QCheckBox* mDebugCheck;
   //! UI font size spin box
   QSpinBox* mUiFontSizeSpin;
   //! Custom instructions text area
   QPlainTextEdit* mCustomInstructionsEdit;
   //! Max agentic loop iterations
   QSpinBox* mMaxIterationsSpin;
   //! Context window size combo
   QComboBox* mContextWindowCombo;
   //! Auto-condense enabled checkbox
   QCheckBox* mAutoCondenseCheck;
   //! Search file extensions input
   QLineEdit* mSearchExtensionsEdit;
   //! Ignore patterns input
   QPlainTextEdit* mIgnorePatternsEdit;
   //! Refresh models button
   QPushButton* mRefreshModelsBtn;
   //! Open debug window button
   QPushButton* mOpenDebugBtn;
   //! Reload skills button
   QPushButton* mReloadSkillsBtn;
   //! Network manager for fetching models
   QNetworkAccessManager* mNetworkManager;
   //! Status label for toast messages
   QLabel* mStatusLabel;
};

} // namespace AiChat

#endif // AICHAT_PREF_WIDGET_HPP
