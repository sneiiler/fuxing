// -----------------------------------------------------------------------------
// File: ReplaceInFileToolHandler.cpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#include "ReplaceInFileToolHandler.hpp"
#include "AiChatFileEditUtils.hpp"
#include "../bridge/AiChatEditorBridge.hpp"
#include "../core/AiChatPathAccessManager.hpp"
#include "../AiChatEncodingUtils.hpp"

#include <QFileInfo>

namespace AiChat
{

ReplaceInFileToolHandler::ReplaceInFileToolHandler(PathAccessManager* aPathAccess)
   : mPathAccess(aPathAccess)
{
}

ToolDefinition ReplaceInFileToolHandler::GetDefinition() const
{
   ToolDefinition def;
   def.name        = QStringLiteral("replace_in_file");
   def.description = QStringLiteral(
      "Make targeted edits to an existing file using SEARCH/REPLACE blocks. Each block "
      "finds the exact text in the file and replaces it. Use this for surgical changes "
      "instead of rewriting entire files.\n\n"
      "The 'diff' parameter should contain one or more SEARCH/REPLACE blocks in this format:\n"
      "<<<<<<< SEARCH\nexact text to find\n=======\nreplacement text\n>>>>>>> REPLACE\n\n"
      "Rules:\n"
      "- SEARCH content must match exactly (including whitespace and indentation).\n"
      "- Only include the minimum lines needed to uniquely identify the location.\n"
      "- **ALWAYS batch multiple changes into ONE call** with multiple SEARCH/REPLACE "
      "blocks listed in file order. NEVER make multiple replace_in_file calls to the "
      "same file when a single call with multiple blocks would suffice.\n"
      "- For repetitive/pattern-based changes (e.g., renaming all 'walker-' to "
      "'satellite-'), include ALL occurrences as separate blocks in a SINGLE diff.");
   def.parameters = {
      {"path", "string",
         "The path of the file to modify (relative to the workspace root, or absolute). "
         "Use @workspace:path to target a specific root.", true},
      {"diff", "string",
       "One or more SEARCH/REPLACE blocks describing the changes", true}};
   return def;
}

bool ReplaceInFileToolHandler::ValidateParams(const QJsonObject& aParams, QString& aError) const
{
   if (!aParams.contains("path") || aParams["path"].toString().trimmed().isEmpty()) {
      aError = QStringLiteral("Missing required parameter: path");
      return false;
   }
   if (!aParams.contains("diff") || aParams["diff"].toString().trimmed().isEmpty()) {
      aError = QStringLiteral("Missing required parameter: diff");
      return false;
   }

   // Verify that the diff contains at least one SEARCH/REPLACE block
   const QString diff = aParams["diff"].toString();
   if (!diff.contains(QStringLiteral("<<<<<<< SEARCH")) ||
       !diff.contains(QStringLiteral(">>>>>>> REPLACE"))) {
      aError = QStringLiteral(
         "Invalid diff format. Expected SEARCH/REPLACE blocks:\n"
         "<<<<<<< SEARCH\n...\n=======\n...\n>>>>>>> REPLACE");
      return false;
   }

   if (mPathAccess) {
      const QString filePath = aParams["path"].toString().trimmed();
      const auto res = mPathAccess->ResolveForWrite(filePath);
      if (!res.ok) {
         aError = res.error;
         return false;
      }
   }

   return true;
}

ToolResult ReplaceInFileToolHandler::Execute(const QJsonObject& aParams)
{
   const QString filePath = aParams["path"].toString().trimmed();
   const QString diff     = aParams["diff"].toString();

   QString resolvedPath = filePath;
   if (mPathAccess) {
      const auto res = mPathAccess->ResolveForWrite(filePath);
      if (!res.ok) {
         return {false,
                 QStringLiteral("Error: %1").arg(res.error),
                 {}, false};
      }
      resolvedPath = res.resolvedPath;
   }

   // Parse the SEARCH/REPLACE blocks
   QString parseError;
   auto blocks = ParseSearchReplaceBlocks(diff, parseError);
   if (blocks.isEmpty()) {
      return {false,
              QStringLiteral("Error: %1").arg(parseError),
              {}, false};
   }

   // Sandbox check — reject paths outside the project
   const QString resolved = resolvedPath.isEmpty()
      ? EditorBridge::ResolvePath(filePath)
      : resolvedPath;
   if (resolved.isEmpty()) {
      return {false,
              QStringLiteral("Error: Path '%1' is outside the project directory. "
                             "All file operations must stay within the project boundary.")
                 .arg(filePath),
              {}, false};
   }

   // Read the original file content
   QString content = EditorBridge::ReadFile(resolved);
   if (content.isNull()) {
      return {false,
              QStringLiteral("Error: Could not read file '%1'. The file may not exist.").arg(filePath),
              {}, false};
   }

   // Save original content for inline review before modification
   const QString preChangeContent = content;

   // Apply each SEARCH/REPLACE block sequentially
   QString modifiedContent;
   QString applyError;
   int appliedCount = 0;
   if (!ApplySearchReplaceBlocks(content, blocks, modifiedContent, applyError, appliedCount)) {
      return {false,
              QStringLiteral("Error: %1").arg(applyError),
              {}, false};
   }

   if (EncodingUtils::RequiresAsciiOnly(filePath))
   {
      QChar badChar;
      int badIndex = -1;
      if (EncodingUtils::FindFirstNonAscii(modifiedContent, badChar, badIndex))
      {
         return {false,
                 EncodingUtils::FormatNonAsciiError(badChar, badIndex),
                 {}, false};
      }
   }

   // Write the modified content back
   bool ok = EditorBridge::WriteFile(resolved, modifiedContent);
   if (!ok) {
      return {false,
              QStringLiteral("Error: Failed to write modified content to '%1'").arg(filePath),
              {}, false};
   }

   ToolResult result;
   result.success = true;
   result.content = QStringLiteral("Applied %1/%2 block(s) in %3")
      .arg(appliedCount).arg(blocks.size()).arg(filePath);
   result.userDisplayMessage = QStringLiteral("Edited file: %1").arg(filePath);
   result.preChangeContent = preChangeContent;
   result.modifiedFilePath = resolved;
   return result;
}

QString ReplaceInFileToolHandler::GenerateDiff(const QJsonObject& aParams) const
{
   const QString filePath = aParams["path"].toString().trimmed();
   const QString diff     = aParams["diff"].toString();

   // The diff parameter itself serves as the preview
   return QStringLiteral("File: %1\n%2").arg(filePath, diff);
}

} // namespace AiChat
