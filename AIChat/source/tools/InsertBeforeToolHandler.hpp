// -----------------------------------------------------------------------------
// File: InsertBeforeToolHandler.hpp
// -----------------------------------------------------------------------------

#ifndef AICHAT_INSERT_BEFORE_TOOL_HANDLER_HPP
#define AICHAT_INSERT_BEFORE_TOOL_HANDLER_HPP

#include "../tools/IToolHandler.hpp"

namespace AiChat
{

class PathAccessManager;

class InsertBeforeToolHandler : public IToolHandler
{
public:
   explicit InsertBeforeToolHandler(PathAccessManager* aPathAccess = nullptr);
   ToolDefinition GetDefinition() const override;
   bool           ValidateParams(const QJsonObject& aParams, QString& aError) const override;
   ToolResult     Execute(const QJsonObject& aParams) override;
   bool           RequiresApproval() const override { return true; }
   QString        GenerateDiff(const QJsonObject& aParams) const override;

private:
   PathAccessManager* mPathAccess{nullptr};
};

} // namespace AiChat

#endif // AICHAT_INSERT_BEFORE_TOOL_HANDLER_HPP
