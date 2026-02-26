// -----------------------------------------------------------------------------
// File: AskQuestionToolHandler.cpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#include "AskQuestionToolHandler.hpp"

namespace AiChat
{

AskQuestionToolHandler::AskQuestionToolHandler(QObject* aParent)
   : QObject(aParent)
{
}

ToolDefinition AskQuestionToolHandler::GetDefinition() const
{
   ToolDefinition def;
   def.name        = QStringLiteral("ask_question");
   def.description = QStringLiteral(
      "Ask the user a question to gather additional information needed to complete the task. "
      "Use this when you encounter ambiguity, need clarification, or require user input to proceed. "
      "Only ask one question at a time and make it clear and specific.");
   def.parameters = {
      {"question", "string", "The question to ask the user", true}};
   return def;
}

bool AskQuestionToolHandler::ValidateParams(const QJsonObject& aParams, QString& aError) const
{
   if (!aParams.contains("question") || aParams["question"].toString().trimmed().isEmpty()) {
      aError = QStringLiteral("Missing required parameter: question");
      return false;
   }
   return true;
}

QString AskQuestionToolHandler::GenerateDiff(const QJsonObject& aParams) const
{
   return aParams["question"].toString().trimmed();
}

ToolResult AskQuestionToolHandler::Execute(const QJsonObject& aParams)
{
   // This is called by ToolExecutor::ResolveApproval when the user approves.
   // For ask_question, the executor special-cases this tool and
   // returns the user's answer directly, so this method should not normally
   // be reached. If it IS called, return the question as a fallback.
   const QString question = aParams["question"].toString().trimmed();
   emit QuestionAsked(question);

   return {true,
           QStringLiteral("[No answer provided for question: %1]").arg(question),
           question,
           false};
}

} // namespace AiChat
