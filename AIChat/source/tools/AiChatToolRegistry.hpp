// -----------------------------------------------------------------------------
// File: AiChatToolRegistry.hpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#ifndef AICHAT_TOOL_REGISTRY_HPP
#define AICHAT_TOOL_REGISTRY_HPP

#include <QJsonArray>
#include <QString>
#include <QStringList>
#include <map>
#include <memory>

#include "IToolHandler.hpp"

namespace AiChat
{

/// Central registry that owns all tool handlers and provides look-up by name.
class ToolRegistry
{
public:
   ToolRegistry() = default;

   /// Register a tool handler.  Ownership is transferred to the registry.
   void Register(std::unique_ptr<IToolHandler> aHandler)
   {
      const QString name = aHandler->GetDefinition().name;
      mHandlers[name]    = std::move(aHandler);
   }

   /// Look up a handler by tool name.  Returns nullptr if not found.
   IToolHandler* GetHandler(const QString& aName) const
   {
      auto it = mHandlers.find(aName);
      return (it != mHandlers.end()) ? it->second.get() : nullptr;
   }

   /// Build the JSON array suitable for the OpenAI "tools" request field.
   QJsonArray GetToolDefinitionsJson() const
   {
      QJsonArray arr;
      for (const auto& pair : mHandlers)
      {
         arr.append(pair.second->GetDefinition().ToFunctionSchema());
      }
      return arr;
   }

   /// Get a list of all registered tool names
   QStringList GetToolNames() const
   {
      QStringList names;
      for (const auto& pair : mHandlers) { names.append(pair.first); }
      return names;
   }

   /// Get count of registered tools
   int Count() const { return static_cast<int>(mHandlers.size()); }

private:
   std::map<QString, std::unique_ptr<IToolHandler>> mHandlers;
};

} // namespace AiChat

#endif // AICHAT_TOOL_REGISTRY_HPP
