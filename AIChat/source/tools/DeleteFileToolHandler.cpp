// -----------------------------------------------------------------------------
// File: DeleteFileToolHandler.cpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-24
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#include "DeleteFileToolHandler.hpp"

#include "../core/AiChatPathAccessManager.hpp"

#include "ProjectWorkspace.hpp"

#include <QFile>
#include <QFileInfo>

namespace AiChat
{

DeleteFileToolHandler::DeleteFileToolHandler(PathAccessManager* aPathAccess)
   : mPathAccess(aPathAccess)
{
}

ToolDefinition DeleteFileToolHandler::GetDefinition() const
{
   ToolDefinition def;
   def.name        = QStringLiteral("delete_file");
   def.description = QStringLiteral(
      "Delete an existing file within the workspace root. "
      "Use this only when file removal is explicitly required.");
   def.parameters = {
      {QStringLiteral("path"), QStringLiteral("string"),
         QStringLiteral("Path to the file to delete (relative to the workspace root, or absolute). "
                        "Use @workspace:path for multi-root."), true}
   };
   return def;
}

bool DeleteFileToolHandler::ValidateParams(const QJsonObject& aParams, QString& aError) const
{
   if (!aParams.contains(QStringLiteral("path")) || !aParams.value(QStringLiteral("path")).isString())
   {
      aError = QStringLiteral("Missing or invalid 'path' parameter.");
      return false;
   }

   const QString path = aParams.value(QStringLiteral("path")).toString().trimmed();
   if (path.isEmpty())
   {
      aError = QStringLiteral("Delete path is empty.");
      return false;
   }

   if (!mPathAccess)
   {
      aError = QStringLiteral("Path access manager not available.");
      return false;
   }

   const auto res = mPathAccess->ResolveForWrite(path);
   if (!res.ok)
   {
      aError = res.error;
      return false;
   }

   QFileInfo info(res.resolvedPath);
   if (!info.exists())
   {
      aError = QStringLiteral("File does not exist: %1").arg(path);
      return false;
   }

   if (info.isDir())
   {
      aError = QStringLiteral("Path is a directory, not a file: %1").arg(path);
      return false;
   }

   return true;
}

ToolResult DeleteFileToolHandler::Execute(const QJsonObject& aParams)
{
   const QString path = aParams.value(QStringLiteral("path")).toString().trimmed();

   if (!mPathAccess)
   {
      return {false, QStringLiteral("Error: Path access manager not available."), {}, false};
   }

   const auto res = mPathAccess->ResolveForWrite(path);
   if (!res.ok)
   {
      return {false, QStringLiteral("Error: %1").arg(res.error), {}, false};
   }

   QFileInfo info(res.resolvedPath);
   if (!info.exists())
   {
      return {false, QStringLiteral("Error: File does not exist: %1").arg(path), {}, false};
   }
   if (info.isDir())
   {
      return {false, QStringLiteral("Error: '%1' is a directory. delete_file only removes files.").arg(path), {}, false};
   }

   QFile file(res.resolvedPath);
   if (!file.remove())
   {
      return {false, QStringLiteral("Error: Failed to delete file '%1'.").arg(path), {}, false};
   }

   auto* workspace = wizard::ProjectWorkspace::Instance();
   if (workspace)
   {
      workspace->ScheduleCheckingFilesForModification();
   }

   return {true,
           QStringLiteral("Successfully deleted file %1").arg(path),
           QStringLiteral("Deleted file: %1").arg(path),
           false};
}

QString DeleteFileToolHandler::GenerateDiff(const QJsonObject& aParams) const
{
   const QString path = aParams.value(QStringLiteral("path")).toString().trimmed();
   return QStringLiteral("Delete file -> %1").arg(path);
}

} // namespace AiChat
