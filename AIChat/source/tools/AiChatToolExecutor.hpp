// -----------------------------------------------------------------------------
// File: AiChatToolExecutor.hpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#ifndef AICHAT_TOOL_EXECUTOR_HPP
#define AICHAT_TOOL_EXECUTOR_HPP

#include <QMap>
#include <QObject>
#include "AiChatToolTypes.hpp"

namespace AiChat
{
class IToolHandler;
class ToolRegistry;
class AutoApprovePolicy;

/// Executes tool calls received from the LLM, with approval gating for write tools.
class ToolExecutor : public QObject
{
   Q_OBJECT
public:
   explicit ToolExecutor(ToolRegistry* aRegistry, AutoApprovePolicy* aPolicy = nullptr,
                         QObject* aParent = nullptr);
   ~ToolExecutor() override = default;

   /// Execute a tool call. If the tool requires approval, it emits ApprovalRequired
   /// and waits until ResolveApproval() is called.  Otherwise it executes immediately.
   /// @return the result (may be deferred if approval is needed)
   ToolResult Execute(const ToolCall& aToolCall);

   /// Resume execution after the user approves or rejects.
   void ResolveApproval(const QString& aToolCallId, bool aApproved, const QString& aFeedback = {});

   /// Resolve approval with a precomputed result (skips handler execution).
   void ResolveApprovalWithResult(const QString& aToolCallId, bool aApproved, const ToolResult& aResult);

   /// Clear any pending approvals (used when aborting a task).
   void ClearPendingApprovals();

signals:
   /// Emitted when a tool begins execution
   void ToolStarted(const QString& aToolCallId, const QString& aToolName);

   /// Emitted when a tool completes
   void ToolFinished(const QString& aToolCallId, const ToolResult& aResult);

   /// Emitted when a tool needs user approval before proceeding
   void ApprovalRequired(const QString& aToolCallId, const QString& aToolName,
                         const QString& aDiffPreview, const QJsonObject& aParams);

   /// Emitted after approval is resolved (approved or rejected)
   void ApprovalResolved(const QString& aToolCallId, bool aApproved, const ToolResult& aResult);

private:
   ToolRegistry* mRegistryPtr;
   AutoApprovePolicy* mApprovalPolicy{nullptr};

   /// Pending approval state
   struct PendingApproval
   {
      ToolCall    toolCall;
      IToolHandler* handlerPtr{nullptr};
   };
   QMap<QString, PendingApproval> mPendingApprovals;
};

} // namespace AiChat

#endif // AICHAT_TOOL_EXECUTOR_HPP
