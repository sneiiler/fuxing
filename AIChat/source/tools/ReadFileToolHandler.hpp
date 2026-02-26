// -----------------------------------------------------------------------------
// File: ReadFileToolHandler.hpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#ifndef AICHAT_READ_FILE_TOOL_HANDLER_HPP
#define AICHAT_READ_FILE_TOOL_HANDLER_HPP

#include "../tools/IToolHandler.hpp"

namespace AiChat
{

class PathAccessManager;

/// Tool handler for reading file contents.
/// Maps to Cline's "read_file" tool.
class ReadFileToolHandler : public IToolHandler
{
public:
   explicit ReadFileToolHandler(PathAccessManager* aPathAccess = nullptr);
   ToolDefinition GetDefinition() const override;
   bool           ValidateParams(const QJsonObject& aParams, QString& aError) const override;
   ToolResult     Execute(const QJsonObject& aParams) override;
   bool           RequiresApproval() const override { return false; }
   QString        GenerateDiff(const QJsonObject& aParams) const override;

private:
   PathAccessManager* mPathAccess{nullptr};
};

} // namespace AiChat

#endif // AICHAT_READ_FILE_TOOL_HANDLER_HPP
