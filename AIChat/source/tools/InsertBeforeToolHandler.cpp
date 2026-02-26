// -----------------------------------------------------------------------------
// File: InsertBeforeToolHandler.cpp
// -----------------------------------------------------------------------------

#include "InsertBeforeToolHandler.hpp"

#include "../bridge/AiChatEditorBridge.hpp"
#include "../core/AiChatPathAccessManager.hpp"
#include "../AiChatEncodingUtils.hpp"

namespace AiChat
{

InsertBeforeToolHandler::InsertBeforeToolHandler(PathAccessManager* aPathAccess)
   : mPathAccess(aPathAccess)
{
}

ToolDefinition InsertBeforeToolHandler::GetDefinition() const
{
   ToolDefinition def;
   def.name        = QStringLiteral("insert_before");
   def.description = QStringLiteral(
      "Insert content immediately before the first occurrence of a pattern in a file.");
   def.parameters = {
      {QStringLiteral("pattern"), QStringLiteral("string"),
       QStringLiteral("The text pattern to match in the target file."), true},
      {QStringLiteral("new_content"), QStringLiteral("string"),
       QStringLiteral("The content to insert before the matched pattern."), true},
      {QStringLiteral("path"), QStringLiteral("string"),
       QStringLiteral("Optional path to target file. If omitted, uses the currently open file."), false}
   };
   return def;
}

bool InsertBeforeToolHandler::ValidateParams(const QJsonObject& aParams, QString& aError) const
{
   const QString pattern = aParams.value(QStringLiteral("pattern")).toString();
   const QString content = aParams.value(QStringLiteral("new_content")).toString();

   if (pattern.isEmpty())
   {
      aError = QStringLiteral("Missing required parameter: pattern");
      return false;
   }
   if (!aParams.contains(QStringLiteral("new_content")))
   {
      aError = QStringLiteral("Missing required parameter: new_content");
      return false;
   }

   const QString path = aParams.value(QStringLiteral("path")).toString().trimmed();
   if (!path.isEmpty() && mPathAccess)
   {
      const auto res = mPathAccess->ResolveForWrite(path);
      if (!res.ok) {
         aError = res.error;
         return false;
      }
   }

   const QString resolvedHint = path.isEmpty() ? QString() : path;
   if (EncodingUtils::RequiresAsciiOnly(resolvedHint))
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

ToolResult InsertBeforeToolHandler::Execute(const QJsonObject& aParams)
{
   const QString pattern = aParams.value(QStringLiteral("pattern")).toString();
   const QString newContent = aParams.value(QStringLiteral("new_content")).toString();

   QString filePath = aParams.value(QStringLiteral("path")).toString().trimmed();
   if (filePath.isEmpty())
   {
      filePath = EditorBridge::GetCurrentFilePath();
      if (filePath.isEmpty())
      {
         return {false, QStringLiteral("Error: No path provided and no active file is open."), {}, false};
      }
   }

   QString resolvedPath = filePath;
   if (mPathAccess)
   {
      const auto res = mPathAccess->ResolveForWrite(filePath);
      if (!res.ok)
      {
         return {false, QStringLiteral("Error: %1").arg(res.error), {}, false};
      }
      resolvedPath = res.resolvedPath;
   }

   QString original = EditorBridge::ReadFile(resolvedPath);
   if (original.isNull())
   {
      return {false,
              QStringLiteral("Error: Could not read file '%1'.").arg(filePath),
              {}, false};
   }

   const int idx = original.indexOf(pattern);
   if (idx < 0)
   {
      return {false,
              QStringLiteral("Error: Pattern not found in file '%1'.").arg(filePath),
              {}, false};
   }

   // Save original content for inline review before modification
   const QString preChangeContent = original;
   original.insert(idx, newContent);

   if (EncodingUtils::RequiresAsciiOnly(resolvedPath))
   {
      QChar badChar;
      int badIndex = -1;
      if (EncodingUtils::FindFirstNonAscii(original, badChar, badIndex))
      {
         return {false,
                 EncodingUtils::FormatNonAsciiError(badChar, badIndex),
                 {}, false};
      }
   }

   if (!EditorBridge::WriteFile(resolvedPath, original))
   {
      return {false,
              QStringLiteral("Error: Failed to write file '%1'.").arg(filePath),
              {}, false};
   }

   ToolResult result;
   result.success = true;
   result.content = QStringLiteral("Successfully inserted content before pattern in %1").arg(filePath);
   result.userDisplayMessage = QStringLiteral("Inserted before pattern in: %1").arg(filePath);
   result.preChangeContent = preChangeContent;
   result.modifiedFilePath = resolvedPath;
   return result;
}

QString InsertBeforeToolHandler::GenerateDiff(const QJsonObject& aParams) const
{
   const QString path = aParams.value(QStringLiteral("path")).toString().trimmed();
   const QString pattern = aParams.value(QStringLiteral("pattern")).toString();
   return QStringLiteral("Insert before pattern in %1\nPattern: %2")
      .arg(path.isEmpty() ? QStringLiteral("<current file>") : path,
           pattern.left(200));
}

} // namespace AiChat
