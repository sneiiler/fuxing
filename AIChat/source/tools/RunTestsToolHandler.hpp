// -----------------------------------------------------------------------------
// File: RunTestsToolHandler.hpp
// -----------------------------------------------------------------------------

#ifndef AICHAT_RUN_TESTS_TOOL_HANDLER_HPP
#define AICHAT_RUN_TESTS_TOOL_HANDLER_HPP

#include "../tools/IToolHandler.hpp"

namespace AiChat
{

class ProcessRunner;
class PathAccessManager;

/// Tool handler for running scenario validation tests via mission.exe.
class RunTestsToolHandler : public IToolHandler
{
public:
   explicit RunTestsToolHandler(ProcessRunner* aRunner,
                                PathAccessManager* aPathAccess = nullptr);

   ToolDefinition GetDefinition() const override;
   bool           ValidateParams(const QJsonObject& aParams, QString& aError) const override;
   ToolResult     Execute(const QJsonObject& aParams) override;
   bool           RequiresApproval() const override { return true; }
   QString        GenerateDiff(const QJsonObject& aParams) const override;

private:
   ProcessRunner* mRunner{nullptr};
   PathAccessManager* mPathAccess{nullptr};
};

} // namespace AiChat

#endif // AICHAT_RUN_TESTS_TOOL_HANDLER_HPP
