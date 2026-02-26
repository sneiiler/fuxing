// -----------------------------------------------------------------------------
// File: ExecuteCommandToolHandler.hpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#ifndef AICHAT_EXECUTE_COMMAND_TOOL_HANDLER_HPP
#define AICHAT_EXECUTE_COMMAND_TOOL_HANDLER_HPP

#include "../tools/IToolHandler.hpp"
#include "../bridge/AiChatProcessRunner.hpp"

#include <QEventLoop>

namespace AiChat
{

class PathAccessManager;

/// Tool handler for executing shell commands.
/// Maps to Cline's "execute_command" tool.
/// This handler is synchronous (waits for command completion with a timeout).
class ExecuteCommandToolHandler : public IToolHandler, public IAsyncToolHandler
{
public:
   explicit ExecuteCommandToolHandler(ProcessRunner* aRunner = nullptr,
                                     PathAccessManager* aPathAccess = nullptr);

   ToolDefinition GetDefinition() const override;
   bool           ValidateParams(const QJsonObject& aParams, QString& aError) const override;
   ToolResult     Execute(const QJsonObject& aParams) override;
   void           ExecuteAsync(const QJsonObject& aParams,
                               std::function<void(ToolResult)> aOnComplete) override;
   QString        GenerateDiff(const QJsonObject& aParams) const override;
   bool           RequiresApproval() const override { return true; }

private:
   ProcessRunner* mRunner{nullptr}; ///< Borrowed pointer, not owned
   PathAccessManager* mPathAccess{nullptr};
};

} // namespace AiChat

#endif // AICHAT_EXECUTE_COMMAND_TOOL_HANDLER_HPP
