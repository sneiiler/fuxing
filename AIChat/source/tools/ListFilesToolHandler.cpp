// -----------------------------------------------------------------------------
// File: ListFilesToolHandler.cpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#include "ListFilesToolHandler.hpp"
#include "../bridge/AiChatEditorBridge.hpp"
#include "../bridge/AiChatProjectBridge.hpp"
#include "../core/AiChatPathAccessManager.hpp"

#include <QCoreApplication>
#include <QObject>
#include <QDir>
#include <QFileInfo>
#include <QMetaObject>
#include <QRegularExpression>
#include <QThread>

namespace AiChat
{

namespace
{

QStringList ParseNameFilters(const QString& aPattern)
{
   if (aPattern.trimmed().isEmpty()) {
      return {};
   }

   QStringList filters;
   const QStringList tokens = aPattern.split(QRegularExpression(QStringLiteral("[\\s,;]+")),
                                             Qt::SkipEmptyParts);
   for (const auto& raw : tokens) {
      const QString token = raw.trimmed();
      if (!token.isEmpty()) {
         filters.append(token);
      }
   }
   filters.removeDuplicates();
   return filters;
}

ToolResult BuildListResult(const QStringList& aFiles,
                           const QString& aPath,
                           const QString& aPattern)
{
   if (aFiles.isEmpty()) {
      return {true,
              QStringLiteral("No files found in '%1'%2")
                 .arg(aPath, aPattern.isEmpty() ? QString() : QStringLiteral(" matching '%1'").arg(aPattern)),
              {}, false};
   }

   const int maxDisplay = 500;
   bool      truncated  = false;
   QStringList files = aFiles;
   if (files.size() > maxDisplay) {
      truncated = true;
      files     = files.mid(0, maxDisplay);
   }

   QString result = files.join('\n');
   if (truncated) {
      result += QStringLiteral("\n\n... (truncated, %1 files total)").arg(aFiles.size());
   }

   return {true, result,
           QStringLiteral("Listed %1 files in %2").arg(aFiles.size()).arg(aPath), false};
}

   void DispatchAsync(const std::function<void(ToolResult)>& aOnComplete,
                      const ToolResult& aResult)
   {
      if (auto* app = QCoreApplication::instance()) {
         QMetaObject::invokeMethod(app, [aOnComplete, aResult]() { aOnComplete(aResult); },
                                   Qt::QueuedConnection);
      } else {
         aOnComplete(aResult);
      }
   }

   bool ResolveListDirectory(PathAccessManager* aPathAccess,
                             const QString& aRawPath,
                             QString& aAbsDir,
                             QString& aError)
   {
      QString path = aRawPath.trimmed();
      if (path == QStringLiteral(".")) {
         path.clear();
      }

      if (path.isEmpty()) {
         const QString currentFile = EditorBridge::GetCurrentFilePath();
         if (!currentFile.isEmpty()) {
            path = QFileInfo(currentFile).absolutePath();
         }
      }

      if (aPathAccess) {
         const auto res = aPathAccess->ResolveForList(path.isEmpty() ? QStringLiteral(".") : path);
         if (!res.ok) {
            aError = QStringLiteral("Error: %1").arg(res.error);
            return false;
         }
         aAbsDir = res.resolvedPath;
      } else {
         const QString trimmed = path.trimmed();
         aAbsDir = trimmed.isEmpty() || trimmed == QStringLiteral(".")
            ? ProjectBridge::GetWorkspaceRoot()
            : ProjectBridge::ResolveReadPath(trimmed);
      }

      if (aAbsDir.isEmpty()) {
         aError = QStringLiteral("Error: Could not resolve path '%1'.").arg(path);
         return false;
      }

      return true;
   }
} // namespace

ListFilesToolHandler::ListFilesToolHandler(PathAccessManager* aPathAccess)
   : mPathAccess(aPathAccess)
{
}

ToolDefinition ListFilesToolHandler::GetDefinition() const
{
   ToolDefinition def;
   def.name        = QStringLiteral("list_files");
   def.description = QStringLiteral(
      "List files and directories within a given directory. "
      "Use this to explore the project structure and understand the codebase layout. "
      "By default only the top level is listed. Set recursive to true for a full tree. "
      "Results are relative to the workspace root.");
   def.parameters = {
      {"path", "string",
          "The directory path to list (relative to the workspace root, or absolute). "
          "Use @workspace:path to target a specific root. Use '.' or empty for the primary root.",
         false},
      {"recursive", "boolean",
       "If true, list files recursively. Default is false.", false},
      {"pattern", "string",
       "Optional glob pattern to filter files (e.g., '*.wsf', '*.cpp')", false}};
   return def;
}

bool ListFilesToolHandler::ValidateParams(const QJsonObject& aParams, QString& aError) const
{
   Q_UNUSED(aParams);
   Q_UNUSED(aError);
   return true;
}

ToolResult ListFilesToolHandler::Execute(const QJsonObject& aParams)
{
   const QString path = aParams["path"].toString();
   const bool    recursive = aParams.value("recursive").toBool(false);
   const QString pattern   = aParams.value("pattern").toString();
   QString absDir;
   QString error;
   if (!ResolveListDirectory(mPathAccess, path, absDir, error)) {
      return {false, error, {}, false};
   }

   const QStringList filters = ParseNameFilters(pattern);
   const QStringList files = ProjectBridge::ListFilesAbs(absDir, recursive, filters);
   return BuildListResult(files, absDir, pattern);
}

void ListFilesToolHandler::ExecuteAsync(const QJsonObject& aParams,
                                        std::function<void(ToolResult)> aOnComplete)
{
   const QString path = aParams["path"].toString();
   const bool    recursive = aParams.value("recursive").toBool(false);
   const QString pattern   = aParams.value("pattern").toString();
   QString absDir;
   QString error;
   if (!ResolveListDirectory(mPathAccess, path, absDir, error)) {
      DispatchAsync(aOnComplete, {false, error, {}, false});
      return;
   }
   const QStringList filters = ParseNameFilters(pattern);

   QThread* worker = QThread::create([absDir, recursive, filters, pattern, aOnComplete]() {
      const QStringList files = ProjectBridge::ListFilesAbs(absDir, recursive, filters);
      const ToolResult result = BuildListResult(files, absDir, pattern);
      DispatchAsync(aOnComplete, result);
   });
   QObject::connect(worker, &QThread::finished, worker, &QObject::deleteLater);
   worker->start();
}

QString ListFilesToolHandler::GenerateDiff(const QJsonObject& aParams) const
{
   const QString path = aParams.value(QStringLiteral("path")).toString().trimmed();
   const bool recursive = aParams.value(QStringLiteral("recursive")).toBool(false);
   const QString pattern = aParams.value(QStringLiteral("pattern")).toString().trimmed();

   QStringList lines;
   lines << QStringLiteral("Path: %1").arg(path.isEmpty() ? QStringLiteral(".") : path);
   lines << QStringLiteral("Recursive: %1").arg(recursive ? QStringLiteral("true") : QStringLiteral("false"));
   if (!pattern.isEmpty()) {
      lines << QStringLiteral("Pattern: %1").arg(pattern);
   }
   return lines.join('\n');
}

} // namespace AiChat
