// -----------------------------------------------------------------------------
// File: IToolHandler.hpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#ifndef AICHAT_ITOOL_HANDLER_HPP
#define AICHAT_ITOOL_HANDLER_HPP

#include "AiChatToolTypes.hpp"

#include <functional>

namespace AiChat
{

/// Abstract interface that every tool handler must implement.
class IToolHandler
{
public:
   virtual ~IToolHandler() = default;

   /// Return the tool definition (name, description, parameters)
   virtual ToolDefinition GetDefinition() const = 0;

   /// Validate the argument JSON before execution.
   /// @param aParams  The parsed arguments from the LLM
   /// @param aError   Populated with a human-readable error message on failure
   /// @return true if parameters are valid
   virtual bool ValidateParams(const QJsonObject& aParams, QString& aError) const = 0;

   /// Execute the tool with the given arguments.
   /// @param aParams  The parsed arguments from the LLM
   /// @return a ToolResult with the output to feed back to the LLM
   virtual ToolResult Execute(const QJsonObject& aParams) = 0;

   /// Whether this tool modifies the workspace and therefore needs user approval.
   virtual bool RequiresApproval() const { return false; }

   /// For write-tools: generate a human-readable diff preview.
   /// Default implementation returns an empty string.
   virtual QString GenerateDiff(const QJsonObject& /*aParams*/) const { return {}; }
};

/// Optional async interface for tools that should not block the UI thread.
class IAsyncToolHandler
{
public:
   virtual ~IAsyncToolHandler() = default;

   /// Execute the tool asynchronously and invoke the callback when done.
   virtual void ExecuteAsync(const QJsonObject& aParams,
                             std::function<void(ToolResult)> aOnComplete) = 0;
};

} // namespace AiChat

#endif // AICHAT_ITOOL_HANDLER_HPP
