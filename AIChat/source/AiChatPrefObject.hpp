// -----------------------------------------------------------------------------
// File: AiChatPrefObject.hpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#ifndef AICHAT_PREF_OBJECT_HPP
#define AICHAT_PREF_OBJECT_HPP

#include <QString>
#include <QStringList>
#include "WkfPrefObject.hpp"

namespace AiChat
{

//! Structure storing the AI Chat preference data
struct PrefData
{
   //! Default base URL for the AI API (Dashscope compatible endpoint)
   static constexpr const char* cDEFAULT_BASE_URL = "https://dashscope.aliyuncs.com/compatible-mode/v1";
   //! Default model name
   static constexpr const char* cDEFAULT_MODEL = "qwen3-max";
   //! Default api key
   static constexpr const char* cDEFAULT_API_KEY = "sk-20753a4ae42a4106a2c7204dacde49d4";
   //! Default request timeout in milliseconds
   static constexpr int cDEFAULT_TIMEOUT_MS = 180000;
   //! Default max agentic loop iterations
   static constexpr int cDEFAULT_MAX_ITERATIONS = 25;
   //! Default debug enabled
   static constexpr bool cDEFAULT_DEBUG_ENABLED = false;
   //! Default search file extensions
   static constexpr const char* cDEFAULT_SEARCH_EXTENSIONS =
      "txt,md,ag,wsf,py,cpp,hpp,c,log";
   //! Default ignore patterns (newline or comma separated)
   static constexpr const char* cDEFAULT_IGNORE_PATTERNS = "";
   //! Default UI font size (px)
   static constexpr int cDEFAULT_UI_FONT_SIZE = 14;
   //! Default context window size (tokens)
   static constexpr int cDEFAULT_CONTEXT_WINDOW_SIZE = 128000;
   //! Default auto-condense enabled
   static constexpr bool cDEFAULT_AUTO_CONDENSE_ENABLED = true;
   //! Default auto-condense threshold (usage ratio)
   static constexpr double cDEFAULT_AUTO_CONDENSE_THRESHOLD = 0.8;

   //! The base URL for the AI API
   QString mBaseUrl{cDEFAULT_BASE_URL};
   //! The API key (stored securely)
   QString mApiKey{cDEFAULT_API_KEY};
   //! The model to use
   QString mModel{cDEFAULT_MODEL};
   //! Request timeout in milliseconds
   int mTimeoutMs{cDEFAULT_TIMEOUT_MS};
   //! Whether to automatically apply changes suggested by AI
   bool mAutoApply{false};
   //! Auto-approve read/list/search inside workspace roots
   bool mAutoApproveReadLocal{true};
   //! Auto-approve read/list/search outside workspace roots
   bool mAutoApproveReadExternal{false};
   //! Auto-approve write/replace inside workspace roots
   bool mAutoApproveWriteLocal{false};
   //! Auto-approve safe commands
   bool mAutoApproveCommandSafe{false};
   //! Auto-approve all commands (including risky)
   bool mAutoApproveCommandAll{false};
   //! Allow external reads outside workspace roots
   bool mAllowExternalRead{false};
   //! Custom instructions appended to the system prompt
   QString mCustomInstructions;
   //! Maximum iterations in the agentic loop
   int mMaxIterations{cDEFAULT_MAX_ITERATIONS};
   //! Whether debug logging is enabled
   bool mDebugEnabled{cDEFAULT_DEBUG_ENABLED};
   //! Comma/space-separated list of file extensions for search_files
   QString mSearchExtensions{cDEFAULT_SEARCH_EXTENSIONS};
   //! Ignore patterns for .aichatignore-style filtering
   QString mIgnorePatterns{cDEFAULT_IGNORE_PATTERNS};
   //! UI font size (px)
   int mUiFontSize{cDEFAULT_UI_FONT_SIZE};
   //! Context window size (tokens)
   int mContextWindowSize{cDEFAULT_CONTEXT_WINDOW_SIZE};
   //! Whether to enable LLM auto-condense (experimental)
   bool mAutoCondenseEnabled{cDEFAULT_AUTO_CONDENSE_ENABLED};
   //! Auto-condense trigger threshold (usage ratio 0.0-1.0)
   double mAutoCondenseThreshold{cDEFAULT_AUTO_CONDENSE_THRESHOLD};
};

//! The object managing the AI Chat preference data
class PrefObject : public wkf::PrefObjectT<PrefData>
{
   Q_OBJECT
public:
   //! The name of the preference object
   static constexpr const char* cNAME = "AIChat";

   //! Constructs a PrefObject
   //! @param aParent is the parent QObject
   explicit PrefObject(QObject* aParent = nullptr);
   //! Destructs a PrefObject
   ~PrefObject() override = default;

   //! Get the base URL
   QString GetBaseUrl() const noexcept { return mCurrentPrefs.mBaseUrl; }
   //! Get the API key
   QString GetApiKey() const noexcept { return mCurrentPrefs.mApiKey; }
   //! Get the model name
   QString GetModel() const noexcept { return mCurrentPrefs.mModel; }
   //! Get the timeout in milliseconds
   int GetTimeoutMs() const noexcept { return mCurrentPrefs.mTimeoutMs; }
   //! Get whether auto-apply is enabled
   bool GetAutoApply() const noexcept { return mCurrentPrefs.mAutoApply; }
   //! Get auto-approve read (local)
   bool GetAutoApproveReadLocal() const noexcept { return mCurrentPrefs.mAutoApproveReadLocal; }
   //! Get auto-approve read (external)
   bool GetAutoApproveReadExternal() const noexcept { return mCurrentPrefs.mAutoApproveReadExternal; }
   //! Get auto-approve write (local)
   bool GetAutoApproveWriteLocal() const noexcept { return mCurrentPrefs.mAutoApproveWriteLocal; }
   //! Get auto-approve safe commands
   bool GetAutoApproveCommandSafe() const noexcept { return mCurrentPrefs.mAutoApproveCommandSafe; }
   //! Get auto-approve all commands
   bool GetAutoApproveCommandAll() const noexcept { return mCurrentPrefs.mAutoApproveCommandAll; }
   //! Get allow external read
   bool GetAllowExternalRead() const noexcept { return mCurrentPrefs.mAllowExternalRead; }
   //! Alias for GetAutoApply (used by DockWidget approval logic)
   bool AutoApply() const noexcept { return mCurrentPrefs.mAutoApply; }
   //! Get custom instructions
   QString GetCustomInstructions() const noexcept { return mCurrentPrefs.mCustomInstructions; }
   //! Get max agentic loop iterations
   int GetMaxIterations() const noexcept { return mCurrentPrefs.mMaxIterations; }
   //! Get whether debug logging is enabled
   bool IsDebugEnabled() const noexcept { return mCurrentPrefs.mDebugEnabled; }
   //! Get search file extensions
   QString GetSearchExtensions() const noexcept { return mCurrentPrefs.mSearchExtensions; }
   //! Get ignore patterns
   QString GetIgnorePatterns() const noexcept { return mCurrentPrefs.mIgnorePatterns; }
   //! Get UI font size
   int GetUiFontSize() const noexcept { return mCurrentPrefs.mUiFontSize; }
   //! Get context window size
   int GetContextWindowSize() const noexcept { return mCurrentPrefs.mContextWindowSize; }
   //! Get auto-condense enabled
   bool GetAutoCondenseEnabled() const noexcept { return mCurrentPrefs.mAutoCondenseEnabled; }
   //! Get auto-condense threshold
   double GetAutoCondenseThreshold() const noexcept { return mCurrentPrefs.mAutoCondenseThreshold; }

   //! Set the base URL
   void SetBaseUrl(const QString& aUrl) noexcept { mCurrentPrefs.mBaseUrl = aUrl; }
   //! Set the API key
   void SetApiKey(const QString& aKey) noexcept { mCurrentPrefs.mApiKey = aKey; }
   //! Set the model name
   void SetModel(const QString& aModel) noexcept { mCurrentPrefs.mModel = aModel; }
   //! Set the timeout
   void SetTimeoutMs(int aTimeout) noexcept { mCurrentPrefs.mTimeoutMs = aTimeout; }
   //! Set auto-apply
   void SetAutoApply(bool aAutoApply) noexcept { mCurrentPrefs.mAutoApply = aAutoApply; }
   //! Set auto-approve read (local)
   void SetAutoApproveReadLocal(bool aValue) noexcept { mCurrentPrefs.mAutoApproveReadLocal = aValue; }
   //! Set auto-approve read (external)
   void SetAutoApproveReadExternal(bool aValue) noexcept { mCurrentPrefs.mAutoApproveReadExternal = aValue; }
   //! Set auto-approve write (local)
   void SetAutoApproveWriteLocal(bool aValue) noexcept { mCurrentPrefs.mAutoApproveWriteLocal = aValue; }
   //! Set auto-approve safe commands
   void SetAutoApproveCommandSafe(bool aValue) noexcept { mCurrentPrefs.mAutoApproveCommandSafe = aValue; }
   //! Set auto-approve all commands
   void SetAutoApproveCommandAll(bool aValue) noexcept { mCurrentPrefs.mAutoApproveCommandAll = aValue; }
   //! Set allow external read
   void SetAllowExternalRead(bool aValue) noexcept { mCurrentPrefs.mAllowExternalRead = aValue; }
   //! Set custom instructions
   void SetCustomInstructions(const QString& aInst) noexcept { mCurrentPrefs.mCustomInstructions = aInst; }
   //! Set max iterations
   void SetMaxIterations(int aMax) noexcept { mCurrentPrefs.mMaxIterations = aMax; }
   //! Set debug enabled
   void SetDebugEnabled(bool aEnabled) noexcept { mCurrentPrefs.mDebugEnabled = aEnabled; }
   //! Set search file extensions
   void SetSearchExtensions(const QString& aExts) noexcept { mCurrentPrefs.mSearchExtensions = aExts; }
   //! Set ignore patterns
   void SetIgnorePatterns(const QString& aPatterns) noexcept { mCurrentPrefs.mIgnorePatterns = aPatterns; }
   //! Set UI font size
   void SetUiFontSize(int aSize) noexcept { mCurrentPrefs.mUiFontSize = aSize; }
   //! Set context window size
   void SetContextWindowSize(int aSize) noexcept { mCurrentPrefs.mContextWindowSize = aSize; }
   //! Set auto-condense enabled
   void SetAutoCondenseEnabled(bool aEnabled) noexcept { mCurrentPrefs.mAutoCondenseEnabled = aEnabled; }
   //! Set auto-condense threshold
   void SetAutoCondenseThreshold(double aThreshold) noexcept { mCurrentPrefs.mAutoCondenseThreshold = aThreshold; }

   //! Check if base URL is configured (API key is optional)
   bool IsConfigured() const noexcept { return !mCurrentPrefs.mBaseUrl.isEmpty(); }

   //! Dynamic model list fetched from API
   QStringList GetAvailableModels() const noexcept { return mAvailableModels; }
   //! Set available models (called after fetching from API)
   void SetAvailableModels(const QStringList& aModels);

   //! Apply the current preferences
   void Apply() override;

   //! Read preferences from settings
   PrefData ReadSettings(QSettings& aSettings) const override;
   //! Write preferences to settings
   void SaveSettingsP(QSettings& aSettings) const override;

signals:
   //! Emitted when the configuration changes
   void ConfigurationChanged();
   //! Emitted when available models list is updated
   void AvailableModelsChanged(const QStringList& aModels);
   //! Emitted when user requests to open debug window
   void OpenDebugWindowRequested();
   //! Emitted when user requests to reload skills
   void ReloadSkillsRequested();

private:
   //! Dynamic list of available models from API
   QStringList mAvailableModels;
};

} // namespace AiChat

Q_DECLARE_METATYPE(AiChat::PrefData)

#endif // AICHAT_PREF_OBJECT_HPP
