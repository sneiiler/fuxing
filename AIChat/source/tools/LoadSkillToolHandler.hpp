// -----------------------------------------------------------------------------
// File: LoadSkillToolHandler.hpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.1.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#ifndef AICHAT_LOAD_SKILL_TOOL_HANDLER_HPP
#define AICHAT_LOAD_SKILL_TOOL_HANDLER_HPP

#include "../tools/IToolHandler.hpp"

namespace AiChat
{
class SkillManager;

/// Tool handler for loading a skill by name.
/// Performs on-demand skill rediscovery (matching Cline's lazy discovery pattern)
/// and returns the full skill instructions for the AI to follow.
class LoadSkillToolHandler : public IToolHandler
{
public:
   explicit LoadSkillToolHandler(SkillManager* aSkillManager);

   ToolDefinition GetDefinition() const override;
   bool           ValidateParams(const QJsonObject& aParams, QString& aError) const override;
   ToolResult     Execute(const QJsonObject& aParams) override;
   bool           RequiresApproval() const override { return false; }

private:
   SkillManager* mSkillManagerPtr;  ///< non-const to allow rediscovery & lazy loading
};

} // namespace AiChat

#endif // AICHAT_LOAD_SKILL_TOOL_HANDLER_HPP
