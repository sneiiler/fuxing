// -----------------------------------------------------------------------------
// File: AskQuestionToolHandler.hpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#ifndef AICHAT_ASK_QUESTION_TOOL_HANDLER_HPP
#define AICHAT_ASK_QUESTION_TOOL_HANDLER_HPP

#include "../tools/IToolHandler.hpp"

#include <QObject>

namespace AiChat
{

/// Tool handler for asking the user a question.
///
/// This tool enables the LLM to ask the user for clarification during a task.
/// It integrates with the approval mechanism: the question is shown via the
/// approval panel, and the user's typed answer is returned as the tool result
/// through the ResolveApproval feedback channel.
class AskQuestionToolHandler : public QObject, public IToolHandler
{
   Q_OBJECT
public:
   explicit AskQuestionToolHandler(QObject* aParent = nullptr);

   ToolDefinition GetDefinition() const override;
   bool           ValidateParams(const QJsonObject& aParams, QString& aError) const override;
   ToolResult     Execute(const QJsonObject& aParams) override;
   bool           RequiresApproval() const override { return true; }
   QString        GenerateDiff(const QJsonObject& aParams) const override;

signals:
   /// Emitted when the LLM wants to ask the user a question.
   void QuestionAsked(const QString& aQuestion);
};

} // namespace AiChat

#endif // AICHAT_ASK_QUESTION_TOOL_HANDLER_HPP
