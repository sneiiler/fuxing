// -----------------------------------------------------------------------------
// File: ReplaceInFileToolHandler.hpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#ifndef AICHAT_REPLACE_IN_FILE_TOOL_HANDLER_HPP
#define AICHAT_REPLACE_IN_FILE_TOOL_HANDLER_HPP

#include "../tools/IToolHandler.hpp"

namespace AiChat
{

class PathAccessManager;

/// Tool handler for search-and-replace in a file.
/// Maps to Cline's "replace_in_file" tool — replaces one or more SEARCH/REPLACE blocks.
class ReplaceInFileToolHandler : public IToolHandler
{
public:
   explicit ReplaceInFileToolHandler(PathAccessManager* aPathAccess = nullptr);
   ToolDefinition GetDefinition() const override;
   bool           ValidateParams(const QJsonObject& aParams, QString& aError) const override;
   ToolResult     Execute(const QJsonObject& aParams) override;
   bool           RequiresApproval() const override { return true; }
   QString        GenerateDiff(const QJsonObject& aParams) const override;

private:
   PathAccessManager* mPathAccess{nullptr};
};

} // namespace AiChat

#endif // AICHAT_REPLACE_IN_FILE_TOOL_HANDLER_HPP
