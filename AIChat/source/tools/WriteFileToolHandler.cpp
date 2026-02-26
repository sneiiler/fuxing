// -----------------------------------------------------------------------------
// File: WriteFileToolHandler.cpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#include "WriteFileToolHandler.hpp"
#include "../bridge/AiChatEditorBridge.hpp"
#include "../core/AiChatPathAccessManager.hpp"
#include "../AiChatEncodingUtils.hpp"

#include <QDir>
#include <QFileInfo>

namespace AiChat
{

namespace
{
bool IsNewScenarioWsfFile(const QString& aResolvedPath)
{
   QFileInfo fi(aResolvedPath);
   if (fi.suffix().toLower() != QStringLiteral("wsf") || fi.exists()) {
      return false;
   }
   const QString normalized = QDir::fromNativeSeparators(aResolvedPath).toLower();
   return normalized.contains(QStringLiteral("/scenario/"));
}
} // namespace

WriteFileToolHandler::WriteFileToolHandler(PathAccessManager* aPathAccess)
   : mPathAccess(aPathAccess)
{
}

ToolDefinition WriteFileToolHandler::GetDefinition() const
{
   ToolDefinition def;
   def.name        = QStringLiteral("write_to_file");
   def.description = QStringLiteral(
      "Write content to a file at the specified path. If the file exists, it will be "
      "overwritten. If it doesn't exist, it will be created (along with any necessary "
      "directories). Always provide the COMPLETE intended content of the file.");
   def.parameters = {
      {"path", "string",
         "The path of the file to write to (relative to the workspace root, or absolute). "
         "Use @workspace:path to target a specific root.", true},
      {"content", "string", "The full content to write to the file", true}};
   return def;
}

bool WriteFileToolHandler::ValidateParams(const QJsonObject& aParams, QString& aError) const
{
   if (!aParams.contains("path") || aParams["path"].toString().trimmed().isEmpty()) {
      aError = QStringLiteral("Missing required parameter: path");
      return false;
   }
   if (!aParams.contains("content")) {
      aError = QStringLiteral("Missing required parameter: content");
      return false;
   }

   const QString filePath = aParams["path"].toString().trimmed();
   const QString content  = aParams["content"].toString();
   QString resolved;
   if (mPathAccess) {
      const auto res = mPathAccess->ResolveForWrite(filePath);
      if (!res.ok) {
         aError = res.error;
         return false;
      }
      resolved = res.resolvedPath;
   } else {
      resolved = EditorBridge::ResolvePath(filePath);
   }

   if (IsNewScenarioWsfFile(resolved)) {
      aError = QStringLiteral("Scenario files must use the .txt extension. Please use a .txt file name.");
      return false;
   }

   if (EncodingUtils::RequiresAsciiOnly(resolved))
   {
      QChar badChar;
      int badIndex = -1;
      if (EncodingUtils::FindFirstNonAscii(content, badChar, badIndex))
      {
         aError = EncodingUtils::FormatNonAsciiError(badChar, badIndex);
         return false;
      }
   }
   return true;
}

ToolResult WriteFileToolHandler::Execute(const QJsonObject& aParams)
{
   const QString filePath = aParams["path"].toString().trimmed();
   const QString content  = aParams["content"].toString();

   QString resolved;
   if (mPathAccess) {
      const auto res = mPathAccess->ResolveForWrite(filePath);
      if (!res.ok) {
         return {false,
                 QStringLiteral("Error: %1").arg(res.error),
                 {}, false};
      }
      resolved = res.resolvedPath;
   }

   // Ensure parent directories exist
   if (resolved.isEmpty()) {
      resolved = EditorBridge::ResolvePath(filePath);
   }
   if (resolved.isEmpty()) {
      return {false,
              QStringLiteral("Error: Path '%1' is outside the project directory. "
                             "All file operations must stay within the project boundary.")
                 .arg(filePath),
              {}, false};
   }
   QFileInfo     fi(resolved);
   QDir          dir = fi.dir();
   if (!dir.exists()) {
      dir.mkpath(".");
   }

   // Capture original content before writing (for post-hoc inline review)
   QString preChangeContent = EditorBridge::ReadFile(resolved);
   if (preChangeContent.isNull()) preChangeContent = QString(); // new file → empty

   bool ok = EditorBridge::WriteFile(resolved, content);
   if (!ok) {
      return {false,
              QStringLiteral("Error: Failed to write file '%1'").arg(filePath),
              {}, false};
   }

   // Post-write verification: ensure the file actually contains data
   QFileInfo writtenInfo(resolved);
   if (!writtenInfo.exists() || writtenInfo.size() == 0) {
      return {false,
              QStringLiteral("Error: File '%1' appears empty after write (expected %2 bytes). "
                             "The write may have been lost due to a buffering issue.")
                 .arg(filePath)
                 .arg(content.size()),
              {}, false};
   }

   ToolResult result;
   result.success = true;
   result.content = QStringLiteral("Successfully wrote to %1 (%2 bytes written)")
                       .arg(filePath).arg(writtenInfo.size());
   result.userDisplayMessage = QStringLiteral("Wrote file: %1").arg(filePath);
   result.preChangeContent = preChangeContent;
   result.modifiedFilePath = resolved;
   return result;
}

QString WriteFileToolHandler::GenerateDiff(const QJsonObject& aParams) const
{
   const QString filePath = aParams["path"].toString().trimmed();
   const QString newContent = aParams["content"].toString();

   QString resolved = filePath;
   if (mPathAccess) {
      const auto res = mPathAccess->ResolveForWrite(filePath);
      if (res.ok && !res.resolvedPath.isEmpty()) {
         resolved = res.resolvedPath;
      }
   }

   // Read existing content for diff
   QString existingContent = EditorBridge::ReadFile(resolved);

   if (existingContent.isNull()) {
      // New file — show a creation preview
      QStringList lines = newContent.split('\n');
      QString     diff;
      diff += QStringLiteral("--- /dev/null\n+++ %1\n").arg(filePath);
      for (const auto& line : lines) {
         diff += QStringLiteral("+ %1\n").arg(line);
      }
      return diff;
   }

   // Simple line-by-line diff for existing files
   QStringList oldLines = existingContent.split('\n');
   QStringList newLines = newContent.split('\n');

   QString diff;
   diff += QStringLiteral("--- %1\n+++ %1 (modified)\n").arg(filePath);

   int maxLines = qMax(oldLines.size(), newLines.size());
   for (int i = 0; i < maxLines; ++i) {
      QString oldLine = (i < oldLines.size()) ? oldLines[i] : QString();
      QString newLine = (i < newLines.size()) ? newLines[i] : QString();
      if (oldLine != newLine) {
         if (i < oldLines.size()) {
            diff += QStringLiteral("- %1\n").arg(oldLine);
         }
         if (i < newLines.size()) {
            diff += QStringLiteral("+ %1\n").arg(newLine);
         }
      } else {
         diff += QStringLiteral("  %1\n").arg(oldLine);
      }
   }

   return diff;
}

} // namespace AiChat
