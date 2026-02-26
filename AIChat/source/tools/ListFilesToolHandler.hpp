// -----------------------------------------------------------------------------
// File: ListFilesToolHandler.hpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#ifndef AICHAT_LIST_FILES_TOOL_HANDLER_HPP
#define AICHAT_LIST_FILES_TOOL_HANDLER_HPP

#include "../tools/IToolHandler.hpp"

namespace AiChat
{

class PathAccessManager;

/// Tool handler for listing files in a directory.
/// Maps to Cline's "list_files" tool.
class ListFilesToolHandler : public IToolHandler, public IAsyncToolHandler
{
public:
   explicit ListFilesToolHandler(PathAccessManager* aPathAccess = nullptr);
   ToolDefinition GetDefinition() const override;
   bool           ValidateParams(const QJsonObject& aParams, QString& aError) const override;
   ToolResult     Execute(const QJsonObject& aParams) override;
   void           ExecuteAsync(const QJsonObject& aParams,
                               std::function<void(ToolResult)> aOnComplete) override;
   bool           RequiresApproval() const override { return false; }
   QString        GenerateDiff(const QJsonObject& aParams) const override;

private:
   PathAccessManager* mPathAccess{nullptr};
};

} // namespace AiChat

#endif // AICHAT_LIST_FILES_TOOL_HANDLER_HPP
