// -----------------------------------------------------------------------------
// File: WriteFileToolHandler.hpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#ifndef AICHAT_WRITE_FILE_TOOL_HANDLER_HPP
#define AICHAT_WRITE_FILE_TOOL_HANDLER_HPP

#include "../tools/IToolHandler.hpp"

namespace AiChat
{

class PathAccessManager;

/// Tool handler for writing/creating file contents.
/// Maps to Cline's "write_to_file" tool.
class WriteFileToolHandler : public IToolHandler
{
public:
   explicit WriteFileToolHandler(PathAccessManager* aPathAccess = nullptr);
   ToolDefinition GetDefinition() const override;
   bool           ValidateParams(const QJsonObject& aParams, QString& aError) const override;
   ToolResult     Execute(const QJsonObject& aParams) override;
   bool           RequiresApproval() const override { return true; }
   QString        GenerateDiff(const QJsonObject& aParams) const override;

private:
   PathAccessManager* mPathAccess{nullptr};
};

} // namespace AiChat

#endif // AICHAT_WRITE_FILE_TOOL_HANDLER_HPP
