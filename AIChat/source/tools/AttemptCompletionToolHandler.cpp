// -----------------------------------------------------------------------------
// File: AttemptCompletionToolHandler.cpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#include "AttemptCompletionToolHandler.hpp"

namespace AiChat
{

ToolDefinition AttemptCompletionToolHandler::GetDefinition() const
{
   ToolDefinition def;
   def.name        = QStringLiteral("attempt_completion");
   def.description = QStringLiteral(
      "Signal that you have completed the user's task. "
      "Present the result of your work to the user. "
      "Optionally provide a command for the user to run to verify the result. "
      "IMPORTANT: This tool should only be called when the task is fully complete. "
      "Do NOT use this if there are remaining steps or if you are uncertain about the result.");
   def.parameters = {
      {"result", "string",
       "A concise summary of the completed work and any important information for the user",
       true},
      {"command", "string",
       "An optional command for the user to run to verify the result (e.g., a build or test command)",
       false}};
   return def;
}

bool AttemptCompletionToolHandler::ValidateParams(const QJsonObject& aParams, QString& aError) const
{
   if (!aParams.contains("result") || aParams["result"].toString().trimmed().isEmpty()) {
      aError = QStringLiteral("Missing required parameter: result");
      return false;
   }
   return true;
}

ToolResult AttemptCompletionToolHandler::Execute(const QJsonObject& aParams)
{
   const QString result  = aParams["result"].toString().trimmed();
   const QString command = aParams.value("command").toString().trimmed();

   // Return the result directly — the Task state machine recognizes
   // attempt_completion and transitions to Completed state.
   // The result text is shown as the tool indicator detail in the chat bubble.
   QString content = result;

   if (!command.isEmpty()) {
      content += QStringLiteral("\n\nSuggested verification command:\n```\n%1\n```").arg(command);
   }

   return {true, content, result, false};
}

} // namespace AiChat
