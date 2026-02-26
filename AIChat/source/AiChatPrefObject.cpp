// -----------------------------------------------------------------------------
// File: AiChatPrefObject.cpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#include "AiChatPrefObject.hpp"

#include <QSettings>

namespace AiChat
{

PrefObject::PrefObject(QObject* aParent)
   : wkf::PrefObjectT<PrefData>(aParent, cNAME)
{
}

PrefData PrefObject::ReadSettings(QSettings& aSettings) const
{
   PrefData pData;
   aSettings.beginGroup(cNAME);
   pData.mBaseUrl            = aSettings.value("BaseUrl", PrefData::cDEFAULT_BASE_URL).toString();
   pData.mApiKey             = aSettings.value("ApiKey", QString()).toString();
   pData.mModel              = aSettings.value("Model", PrefData::cDEFAULT_MODEL).toString();
   pData.mTimeoutMs          = aSettings.value("TimeoutMs", PrefData::cDEFAULT_TIMEOUT_MS).toInt();
   pData.mAutoApply          = aSettings.value("AutoApply", false).toBool();
   pData.mAutoApproveReadLocal    = aSettings.value("AutoApproveReadLocal", true).toBool();
   pData.mAutoApproveReadExternal = aSettings.value("AutoApproveReadExternal", false).toBool();
   pData.mAutoApproveWriteLocal   = aSettings.value("AutoApproveWriteLocal", false).toBool();
   pData.mAutoApproveCommandSafe  = aSettings.value("AutoApproveCommandSafe", false).toBool();
   pData.mAutoApproveCommandAll   = aSettings.value("AutoApproveCommandAll", false).toBool();
   pData.mAllowExternalRead       = aSettings.value("AllowExternalRead", false).toBool();
   pData.mCustomInstructions = aSettings.value("CustomInstructions", QString()).toString();
   pData.mMaxIterations      = aSettings.value("MaxIterations", PrefData::cDEFAULT_MAX_ITERATIONS).toInt();
   pData.mDebugEnabled       = aSettings.value("DebugEnabled", PrefData::cDEFAULT_DEBUG_ENABLED).toBool();
   pData.mSearchExtensions   = aSettings.value("SearchExtensions",
                                               PrefData::cDEFAULT_SEARCH_EXTENSIONS).toString();
   pData.mIgnorePatterns     = aSettings.value("IgnorePatterns",
                                               PrefData::cDEFAULT_IGNORE_PATTERNS).toString();
   pData.mUiFontSize         = aSettings.value("UiFontSize",
                                               PrefData::cDEFAULT_UI_FONT_SIZE).toInt();
   pData.mContextWindowSize   = aSettings.value("ContextWindowSize",
                                               PrefData::cDEFAULT_CONTEXT_WINDOW_SIZE).toInt();
   pData.mAutoCondenseEnabled = aSettings.value("AutoCondenseEnabled",
                                               PrefData::cDEFAULT_AUTO_CONDENSE_ENABLED).toBool();
   pData.mAutoCondenseThreshold = aSettings.value("AutoCondenseThreshold",
                                               PrefData::cDEFAULT_AUTO_CONDENSE_THRESHOLD).toDouble();
   aSettings.endGroup();
   return pData;
}

void PrefObject::SaveSettingsP(QSettings& aSettings) const
{
   aSettings.beginGroup(cNAME);
   aSettings.setValue("BaseUrl", mCurrentPrefs.mBaseUrl);
   aSettings.setValue("ApiKey", mCurrentPrefs.mApiKey);
   aSettings.setValue("Model", mCurrentPrefs.mModel);
   aSettings.setValue("TimeoutMs", mCurrentPrefs.mTimeoutMs);
   aSettings.setValue("AutoApply", mCurrentPrefs.mAutoApply);
   aSettings.setValue("AutoApproveReadLocal", mCurrentPrefs.mAutoApproveReadLocal);
   aSettings.setValue("AutoApproveReadExternal", mCurrentPrefs.mAutoApproveReadExternal);
   aSettings.setValue("AutoApproveWriteLocal", mCurrentPrefs.mAutoApproveWriteLocal);
   aSettings.setValue("AutoApproveCommandSafe", mCurrentPrefs.mAutoApproveCommandSafe);
   aSettings.setValue("AutoApproveCommandAll", mCurrentPrefs.mAutoApproveCommandAll);
   aSettings.setValue("AllowExternalRead", mCurrentPrefs.mAllowExternalRead);
   aSettings.setValue("CustomInstructions", mCurrentPrefs.mCustomInstructions);
   aSettings.setValue("MaxIterations", mCurrentPrefs.mMaxIterations);
   aSettings.setValue("DebugEnabled", mCurrentPrefs.mDebugEnabled);
   aSettings.setValue("SearchExtensions", mCurrentPrefs.mSearchExtensions);
   aSettings.setValue("IgnorePatterns", mCurrentPrefs.mIgnorePatterns);
   aSettings.setValue("UiFontSize", mCurrentPrefs.mUiFontSize);
   aSettings.setValue("ContextWindowSize", mCurrentPrefs.mContextWindowSize);
   aSettings.setValue("AutoCondenseEnabled", mCurrentPrefs.mAutoCondenseEnabled);
   aSettings.setValue("AutoCondenseThreshold", mCurrentPrefs.mAutoCondenseThreshold);
   aSettings.endGroup();
}

void PrefObject::SetAvailableModels(const QStringList& aModels)
{
   if (mAvailableModels != aModels) {
      mAvailableModels = aModels;
      emit AvailableModelsChanged(aModels);
   }
}

void PrefObject::Apply()
{
   emit ConfigurationChanged();
}

} // namespace AiChat
