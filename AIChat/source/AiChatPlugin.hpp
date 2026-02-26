// -----------------------------------------------------------------------------
// File: AiChatPlugin.hpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#ifndef AICHAT_PLUGIN_HPP
#define AICHAT_PLUGIN_HPP

#include "UtQtUiPointer.hpp"
#include "core/Plugin.hpp"

// Must include full definitions for ut::qt::UiPointer
#include "AiChatPrefWidget.hpp"

namespace AiChat
{
class DockWidget;

class Plugin : public wizard::Plugin
{
public:
   Plugin(const QString& aPluginName, size_t aUniqueId);
   ~Plugin() override = default;

   //! Get the list of wkf::PrefWidgets
   QList<wkf::PrefWidget*> GetPreferencesWidgets() const override;

   //! Get the PrefObject from the PrefWidget
   PrefObject* GetPrefObject() const noexcept { return mPrefWidget->GetPreferenceObject(); }

private:
   ut::qt::UiPointer<PrefWidget> mPrefWidget;
};

} // namespace AiChat

#endif // AICHAT_PLUGIN_HPP
