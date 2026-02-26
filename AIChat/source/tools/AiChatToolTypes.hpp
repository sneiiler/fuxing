// -----------------------------------------------------------------------------
// File: AiChatToolTypes.hpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#ifndef AICHAT_TOOL_TYPES_HPP
#define AICHAT_TOOL_TYPES_HPP

#include <QJsonArray>
#include <QJsonObject>
#include <QList>
#include <QString>

namespace AiChat
{

/// A single parameter in a tool definition
struct ToolParameter
{
   QString name;
   QString type;         ///< "string", "integer", "boolean"
   QString description;
   bool    required{true};
};

/// Full definition of a tool (maps to OpenAI function-calling schema)
struct ToolDefinition
{
   QString              name;
   QString              description;
   QList<ToolParameter> parameters;

   /// Convert to the OpenAI-compatible "tools" array element JSON
   QJsonObject ToFunctionSchema() const
   {
      // Build "properties" and "required" from our parameter list
      QJsonObject properties;
      QJsonArray  requiredArr;

      for (const auto& p : parameters)
      {
         QJsonObject prop;
         prop["type"]        = p.type;
         prop["description"] = p.description;
         properties[p.name]  = prop;

         if (p.required)
         {
            requiredArr.append(p.name);
         }
      }

      QJsonObject params;
      params["type"]       = "object";
      params["properties"] = properties;
      if (!requiredArr.isEmpty())
      {
         params["required"] = requiredArr;
      }

      QJsonObject function;
      function["name"]        = name;
      function["description"] = description;
      function["parameters"]  = params;

      QJsonObject tool;
      tool["type"]     = "function";
      tool["function"] = function;
      return tool;
   }
};

/// Result returned by a tool handler after execution
struct ToolResult
{
   bool    success{true};
   QString content;              ///< Text content to feed back to the LLM
   QString userDisplayMessage;   ///< Optional: message shown in the UI chat
   bool    requiresApproval{false}; ///< If true, execution is deferred until user approves
   QString preChangeContent;     ///< Original file content before modification (for post-hoc inline review)
   QString modifiedFilePath;     ///< Resolved path of the modified file (for post-hoc inline review)
};

/// A tool call parsed from the LLM response
struct ToolCall
{
   QString     id;              ///< tool_call id from the API
   QString     functionName;
   QJsonObject arguments;       ///< Parsed JSON arguments
};

} // namespace AiChat

#endif // AICHAT_TOOL_TYPES_HPP
