// -----------------------------------------------------------------------------
// File: SearchFilesToolHandler.hpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#ifndef AICHAT_SEARCH_FILES_TOOL_HANDLER_HPP
#define AICHAT_SEARCH_FILES_TOOL_HANDLER_HPP

#include "../tools/IToolHandler.hpp"

namespace AiChat
{

class PrefObject;
class PathAccessManager;

/// Tool handler for searching text within project files.
/// Maps to Cline's "search_files" tool.
class SearchFilesToolHandler : public IToolHandler, public IAsyncToolHandler
{
public:
   explicit SearchFilesToolHandler(PrefObject* aPrefObject, PathAccessManager* aPathAccess = nullptr);
   ToolDefinition GetDefinition() const override;
   bool           ValidateParams(const QJsonObject& aParams, QString& aError) const override;
   ToolResult     Execute(const QJsonObject& aParams) override;
   void           ExecuteAsync(const QJsonObject& aParams,
                               std::function<void(ToolResult)> aOnComplete) override;
   bool           RequiresApproval() const override { return false; }
   QString        GenerateDiff(const QJsonObject& aParams) const override;

private:
   PrefObject* mPrefObjectPtr;
   PathAccessManager* mPathAccess{nullptr};
};

} // namespace AiChat

#endif // AICHAT_SEARCH_FILES_TOOL_HANDLER_HPP
