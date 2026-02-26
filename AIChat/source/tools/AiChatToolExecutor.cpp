// -----------------------------------------------------------------------------
// File: AiChatToolExecutor.cpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#include "AiChatToolExecutor.hpp"
#include "AiChatToolRegistry.hpp"
#include "../core/AiChatAutoApprovePolicy.hpp"

#include <QMetaObject>
#include <QStringList>

namespace AiChat
{

namespace
{
QString CompressToolContent(const QString& aToolName, const QString& aContent)
{
   const int totalChars = aContent.size();
   const int totalLines = aContent.isEmpty() ? 0 : aContent.count('\n') + 1;
   const int previewLinesMax = 12;
   const int previewLineCharsMax = 200;
   const int rawCharsMax = 4000;

   if (totalChars <= 1200 && totalLines <= 20)
   {
      return aContent;
   }

   QStringList lines = aContent.split('\n');

   // --- Head preview (first N lines) ---
   QStringList headPreview;
   const int headCount = qMin(previewLinesMax, lines.size());
   headPreview.reserve(headCount);
   for (int i = 0; i < headCount; ++i)
   {
      QString line = lines[i];
      if (line.size() > previewLineCharsMax) {
         line = line.left(previewLineCharsMax) + QStringLiteral("...");
      }
      headPreview.append(line);
   }

   // --- Tail preview (last N lines) — critical for command output where
   //     success/error messages appear at the end. ---
   const int tailLinesMax = 15;
   QStringList tailPreview;
   const int tailStart = qMax(headCount, lines.size() - tailLinesMax);
   if (tailStart < lines.size())
   {
      for (int i = tailStart; i < lines.size(); ++i)
      {
         QString line = lines[i];
         if (line.size() > previewLineCharsMax) {
            line = line.left(previewLineCharsMax) + QStringLiteral("...");
         }
         tailPreview.append(line);
      }
   }

   // --- RAW section: keep head + tail when truncated ---
   const int rawHalfLen = rawCharsMax / 2;
   QString raw;
   bool rawTruncated = false;
   if (aContent.size() <= rawCharsMax) {
      raw = aContent;
   } else {
      rawTruncated = true;
      raw = aContent.left(rawHalfLen)
            + QStringLiteral("\n\n... [%1 characters omitted] ...\n\n").arg(aContent.size() - rawCharsMax)
            + aContent.right(rawHalfLen);
   }

   const int totalPreviewLines = headCount + tailPreview.size();
   QString summary = QStringLiteral("Summary: tool=%1 chars=%2 lines=%3 preview_lines=%4")
                        .arg(aToolName)
                        .arg(totalChars)
                        .arg(totalLines)
                        .arg(totalPreviewLines);
   if (rawTruncated) {
      summary += QStringLiteral(" raw_truncated=true");
   }

   QString previewText = headPreview.join('\n');
   if (!tailPreview.isEmpty() && tailStart > headCount) {
      previewText += QStringLiteral("\n...\n") + tailPreview.join('\n');
   }

   return QStringLiteral("=== TOOL OUTPUT SUMMARY ===\n%1\n\n=== PREVIEW (HEAD + TAIL) ===\n%2\n\n=== RAW ===\n%3")
      .arg(summary)
      .arg(previewText)
      .arg(raw);
}

void ExecuteHandlerAsync(ToolExecutor* aExecutor,
                         const ToolCall& aToolCall,
                         IToolHandler* aHandler,
                         bool aEmitApprovalResolved)
{
   // Tools whose output must NOT be compressed:
   //  - attempt_completion: final result shown to the user
   //  - load_skill: full skill content is the LLM's primary reference;
   //                truncating it causes hallucinated answers
   const bool skipCompress =
      aToolCall.functionName == QStringLiteral("attempt_completion") ||
      aToolCall.functionName == QStringLiteral("load_skill") ||
      aToolCall.functionName == QStringLiteral("run_tests");

   auto* asyncHandler = dynamic_cast<IAsyncToolHandler*>(aHandler);
   if (!asyncHandler)
   {
      ToolResult result = aHandler->Execute(aToolCall.arguments);
      if (!skipCompress) {
         result.content = CompressToolContent(aToolCall.functionName, result.content);
      }
      emit aExecutor->ToolFinished(aToolCall.id, result);
      return;
   }

   asyncHandler->ExecuteAsync(aToolCall.arguments,
      [aExecutor, aToolCall, aEmitApprovalResolved, skipCompress](ToolResult aResult) {
         if (!skipCompress) {
            aResult.content = CompressToolContent(aToolCall.functionName, aResult.content);
         }
         QMetaObject::invokeMethod(aExecutor, [aExecutor, aToolCall, aResult, aEmitApprovalResolved]() {
            emit aExecutor->ToolFinished(aToolCall.id, aResult);
            if (aEmitApprovalResolved) {
               emit aExecutor->ApprovalResolved(aToolCall.id, true, aResult);
            }
         }, Qt::QueuedConnection);
      });
}
} // namespace

ToolExecutor::ToolExecutor(ToolRegistry* aRegistry, AutoApprovePolicy* aPolicy,
                           QObject* aParent)
   : QObject(aParent)
   , mRegistryPtr(aRegistry)
   , mApprovalPolicy(aPolicy)
{
}

ToolResult ToolExecutor::Execute(const ToolCall& aToolCall)
{
   IToolHandler* handler = mRegistryPtr->GetHandler(aToolCall.functionName);
   if (!handler)
   {
      ToolResult result;
      result.success = false;
      result.content = QString("Unknown tool: %1").arg(aToolCall.functionName);
      emit ToolFinished(aToolCall.id, result);
      return result;
   }

   // Validate parameters
   QString validationError;
   if (!handler->ValidateParams(aToolCall.arguments, validationError))
   {
      ToolResult result;
      result.success = false;
      result.content = QString("Invalid parameters for %1: %2").arg(aToolCall.functionName, validationError);
      emit ToolFinished(aToolCall.id, result);
      return result;
   }

   emit ToolStarted(aToolCall.id, aToolCall.functionName);

   // Only ask_question requires user interaction (approval/input).
   // All other tools execute immediately without blocking (Cline-style auto-execute).
   if (aToolCall.functionName == QStringLiteral("ask_question"))
   {
      QString diffPreview = handler->GenerateDiff(aToolCall.arguments);

      PendingApproval pending;
      pending.toolCall   = aToolCall;
      pending.handlerPtr = handler;
      mPendingApprovals[aToolCall.id] = pending;

      emit ApprovalRequired(aToolCall.id, aToolCall.functionName, diffPreview, aToolCall.arguments);

      // Return a placeholder — the real result comes after ResolveApproval()
      ToolResult deferred;
      deferred.success          = true;
      deferred.requiresApproval = true;
      deferred.content          = "[Waiting for user input]";
      return deferred;
   }

   // Execute immediately (all tools except ask_question)
   ExecuteHandlerAsync(this, aToolCall, handler, false);
   ToolResult placeholder;
   placeholder.success = true;
   placeholder.content = QStringLiteral("[Tool execution in progress]");
   return placeholder;
}

void ToolExecutor::ResolveApproval(const QString& aToolCallId, bool aApproved, const QString& aFeedback)
{
   auto it = mPendingApprovals.find(aToolCallId);
   if (it == mPendingApprovals.end())
   {
      return;
   }

   PendingApproval pending = it.value();
   mPendingApprovals.erase(it);

   ToolResult result;

   if (aApproved)
   {
        // Special handling for ask_question: the user's answer
      // comes through the feedback parameter, not through Execute().
        if (pending.toolCall.functionName == QStringLiteral("ask_question") &&
          !aFeedback.isEmpty())
      {
         result.success = true;
         result.content = QStringLiteral("User's answer: %1").arg(aFeedback);
      }
      else
      {
         ExecuteHandlerAsync(this, pending.toolCall, pending.handlerPtr, true);
         return;
      }
   }
   else
   {
      result.success = false;
      result.content = QString("User rejected the operation.");
      if (!aFeedback.isEmpty())
      {
         result.content += QString(" Feedback: %1").arg(aFeedback);
      }
   }

   emit ToolFinished(pending.toolCall.id, result);
   emit ApprovalResolved(aToolCallId, aApproved, result);
}

void ToolExecutor::ResolveApprovalWithResult(const QString& aToolCallId, bool aApproved, const ToolResult& aResult)
{
   auto it = mPendingApprovals.find(aToolCallId);
   if (it == mPendingApprovals.end())
   {
      return;
   }

   mPendingApprovals.erase(it);

   emit ToolFinished(aToolCallId, aResult);
   emit ApprovalResolved(aToolCallId, aApproved, aResult);
}

void ToolExecutor::ClearPendingApprovals()
{
   mPendingApprovals.clear();
}

} // namespace AiChat
