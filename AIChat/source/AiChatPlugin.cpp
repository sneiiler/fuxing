// -----------------------------------------------------------------------------
// File: AiChatPlugin.cpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#include "AiChatPlugin.hpp"

// Utility Includes
#include "UtQtMemory.hpp"

// WKF Includes
#include "WkfMainWindow.hpp"

// AI Chat Includes
#include "AiChatDockWidget.hpp"

WKF_PLUGIN_DEFINE_SYMBOLS(AiChat::Plugin,
                          "AI Chat",
                          "AI-assisted authoring and simulation control.",
                          "wizard");

namespace AiChat
{
Plugin::Plugin(const QString& aPluginName, const size_t aUniqueId)
   : wizard::Plugin(aPluginName, aUniqueId)
   , mPrefWidget(ut::qt::make_qt_ptr<PrefWidget>())
{
   auto* mainWindow = wkfEnv.GetMainWindow();
   auto* dock       = new DockWidget(mPrefWidget->GetPreferenceObject(), mainWindow);
   mainWindow->AddDockWidgetToViewMenu(dock);
}

QList<wkf::PrefWidget*> Plugin::GetPreferencesWidgets() const
{
   return QList<wkf::PrefWidget*>{mPrefWidget.data()};
}

} // namespace AiChat
