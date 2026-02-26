// -----------------------------------------------------------------------------
// File: SetStartupFileToolHandler.cpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#include "SetStartupFileToolHandler.hpp"
#include "../bridge/AiChatProjectBridge.hpp"
#include "../bridge/AiChatEditorBridge.hpp"
#include "../core/AiChatPathAccessManager.hpp"

#include <QFileInfo>

namespace AiChat
{

SetStartupFileToolHandler::SetStartupFileToolHandler(PathAccessManager* aPathAccess)
   : mPathAccess(aPathAccess)
{
}

ToolDefinition SetStartupFileToolHandler::GetDefinition() const
{
   ToolDefinition def;
   def.name = QStringLiteral("set_startup_file");
   def.description = QStringLiteral("Set the project's startup file to a specific scenario file.");
   def.parameters = {
      {QStringLiteral("path"), QStringLiteral("string"),
         QStringLiteral("Path to the startup file (relative to the workspace root, or absolute). Use @workspace:path for multi-root."), true}
   };
   return def;
}

bool SetStartupFileToolHandler::ValidateParams(const QJsonObject& aParams, QString& aError) const
{
   if (!aParams.contains(QStringLiteral("path")) || !aParams.value(QStringLiteral("path")).isString())
   {
      aError = QStringLiteral("Missing or invalid 'path' parameter.");
      return false;
   }

   const QString path = aParams.value(QStringLiteral("path")).toString().trimmed();
   if (path.isEmpty())
   {
      aError = QStringLiteral("Startup file path is empty.");
      return false;
   }

   QString resolved = EditorBridge::ResolvePath(path);
   if (mPathAccess) {
      const auto res = mPathAccess->ResolveForWrite(path);
      if (!res.ok) {
         aError = res.error;
         return false;
      }
      resolved = res.resolvedPath;
   }

   if (!QFileInfo::exists(resolved))
   {
      aError = QStringLiteral("Startup file does not exist: %1").arg(path);
      return false;
   }

   return true;
}

ToolResult SetStartupFileToolHandler::Execute(const QJsonObject& aParams)
{
   const QString path = aParams.value(QStringLiteral("path")).toString().trimmed();

   QString resolvedPath = path;
   if (mPathAccess) {
      const auto res = mPathAccess->ResolveForWrite(path);
      if (!res.ok) {
         return {false, QStringLiteral("Error: %1").arg(res.error), {}, false};
      }
      resolvedPath = res.resolvedPath;
   }

   QString error;
   if (!ProjectBridge::SetStartupFiles(QStringList{resolvedPath}, error))
   {
      return {false, QStringLiteral("Error: %1").arg(error), {}, false};
   }

   return {true,
           QStringLiteral("Startup file set to %1").arg(resolvedPath),
           QStringLiteral("Startup file: %1").arg(resolvedPath),
           false};
}

QString SetStartupFileToolHandler::GenerateDiff(const QJsonObject& aParams) const
{
   const QString path = aParams.value(QStringLiteral("path")).toString().trimmed();
   return QStringLiteral("Startup file -> %1").arg(path);
}

} // namespace AiChat
