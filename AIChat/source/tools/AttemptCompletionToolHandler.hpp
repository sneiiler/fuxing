// -----------------------------------------------------------------------------
// File: AttemptCompletionToolHandler.hpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#ifndef AICHAT_ATTEMPT_COMPLETION_TOOL_HANDLER_HPP
#define AICHAT_ATTEMPT_COMPLETION_TOOL_HANDLER_HPP

#include "../tools/IToolHandler.hpp"

namespace AiChat
{

/// Tool handler for signaling task completion.
/// Maps to Cline's "attempt_completion" tool.
///
/// When the LLM believes it has finished the task, it calls this tool
/// to present the result to the user and optionally suggest a follow-up command.
class AttemptCompletionToolHandler : public IToolHandler
{
public:
   ToolDefinition GetDefinition() const override;
   bool           ValidateParams(const QJsonObject& aParams, QString& aError) const override;
   ToolResult     Execute(const QJsonObject& aParams) override;
   bool           RequiresApproval() const override { return false; }
};

} // namespace AiChat

#endif // AICHAT_ATTEMPT_COMPLETION_TOOL_HANDLER_HPP
