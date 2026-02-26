// -----------------------------------------------------------------------------
// File: SearchFilesToolHandler.cpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#include "SearchFilesToolHandler.hpp"
#include "../bridge/AiChatEditorBridge.hpp"
#include "../bridge/AiChatProjectBridge.hpp"
#include "../core/AiChatPathAccessManager.hpp"
#include "../core/AiChatSkillManager.hpp"
#include "AiChatPrefObject.hpp"

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
QStringList BuildFileGlobList(const QString& aExtensions)
{
   QStringList globs;
   const QStringList tokens = aExtensions.split(QRegularExpression(QStringLiteral("[\\s,;]+")),
                                                Qt::SkipEmptyParts);
   for (const auto& raw : tokens) {
      const QString token = raw.trimmed();
      if (token.isEmpty()) {
         continue;
      }

      QString glob = token;
      if (glob.startsWith('.')) {
         glob = QStringLiteral("*") + glob;
      } else if (!glob.contains('*') && !glob.contains('?') && !glob.contains('.')) {
         glob = QStringLiteral("*.") + glob;
      }
      globs.append(glob);
   }
   globs.removeDuplicates();
   return globs;
}

int ParseMaxResults(const QJsonObject& aParams, const char* aKey)
{
   const QJsonValue value = aParams.value(aKey);
   return value.isUndefined() ? 100 : qMax(1, qRound(value.toVariant().toDouble()));
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

/// Check whether \a aRawPath uses the @skills prefix and, if so, resolve it
/// to the corresponding absolute directory.  Returns true when the prefix was
/// found (even if the resulting directory does not exist).
bool ResolveSkillsPath(const QString& aRawPath, QString& aAbsDir)
{
   const QString trimmed = aRawPath.trimmed();
   if (!trimmed.startsWith(QStringLiteral("@skills"), Qt::CaseInsensitive)) {
      return false;
   }

   const QString skillsDir = SkillManager::GetGlobalSkillsDirectory();

   // Bare "@skills"
   if (trimmed.length() <= 7) {
      aAbsDir = skillsDir;
      return true;
   }

   const QChar sep = trimmed.at(7);
   if (sep == ':' || sep == '/' || sep == '\\') {
      const QString sub = trimmed.mid(8).trimmed();
      aAbsDir = (sub.isEmpty() || sub == QStringLiteral("."))
                   ? skillsDir
                   : QDir::cleanPath(skillsDir + '/' + sub);
      return true;
   }

   return false;
}

/// Return true if \a aAbsDir is inside (or equal to) the global skills
/// directory.
bool IsInsideSkillsDirectory(const QString& aAbsDir)
{
   const QString skillsDir = SkillManager::GetGlobalSkillsDirectory();
   if (skillsDir.isEmpty() || aAbsDir.isEmpty()) {
      return false;
   }
#ifdef Q_OS_WIN
   const Qt::CaseSensitivity cs = Qt::CaseInsensitive;
#else
   const Qt::CaseSensitivity cs = Qt::CaseSensitive;
#endif
   const QString rootSlash = skillsDir.endsWith('/') ? skillsDir : skillsDir + '/';
   return aAbsDir.startsWith(rootSlash, cs)
          || aAbsDir.compare(skillsDir, cs) == 0;
}

bool ResolveSearchDirectory(PathAccessManager* aPathAccess,
                            const QString& aRawPath,
                            QString& aAbsDir,
                            QString& aError)
{
   QString path = aRawPath.trimmed();

   // ---- @skills shortcut ---------------------------------------------------
   if (ResolveSkillsPath(path, aAbsDir)) {
      const QFileInfo info(aAbsDir);
      if (!info.exists() || !info.isDir()) {
         aError = QStringLiteral("Error: Skills directory does not exist: %1")
                     .arg(aAbsDir);
         return false;
      }
      return true;   // bypass normal access checks — skills are always allowed
   }

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
      const auto res = aPathAccess->ResolveForSearch(path.isEmpty() ? QStringLiteral(".") : path);
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

ToolResult BuildSearchResult(const QList<SearchResult>& aResults,
                             const QString& aPattern)
{
   if (aResults.isEmpty()) {
      return {true,
              QStringLiteral("No matches found for '%1'").arg(aPattern),
              {}, false};
   }

   QString output;
   output.reserve(aResults.size() * 120);
   constexpr int kMaxOutputChars = 20000;

   QString lastFile;
   for (const auto& r : aResults) {
      if (r.filePath != lastFile) {
         if (!output.isEmpty()) {
            output += '\n';
         }
         output += QStringLiteral("=== %1 ===\n").arg(r.filePath);
         lastFile = r.filePath;
      }
      output += QStringLiteral("%1:%2: %3\n")
                   .arg(r.filePath)
                   .arg(r.lineNumber)
                   .arg(r.lineContent.trimmed());
      if (output.size() >= kMaxOutputChars) {
         output += QStringLiteral("\n[Truncated output to %1 chars]\n").arg(kMaxOutputChars);
         break;
      }
   }

   return {true,
           output,
           QStringLiteral("Found %1 match(es) for '%2'").arg(aResults.size()).arg(aPattern),
           false};
}

} // namespace
SearchFilesToolHandler::SearchFilesToolHandler(PrefObject* aPrefObject, PathAccessManager* aPathAccess)
   : mPrefObjectPtr(aPrefObject)
   , mPathAccess(aPathAccess)
{
}

ToolDefinition SearchFilesToolHandler::GetDefinition() const
{
   ToolDefinition def;
   def.name        = QStringLiteral("search_files");
   def.description = QStringLiteral(
      "Search for a text pattern across files in the workspace. "
      "Returns matching lines with file paths and line numbers. "
      "Use this to find code references, function definitions, or specific text patterns.");
   def.parameters = {
      {"pattern", "string", "The text pattern to search for", true},
      {"path", "string",
         "The directory to search in (relative to workspace root, or absolute). "
         "Use @workspace:path to target a specific root. "
         "Use @skills to search the global skills directory (~/.aichat/skills/). "
         "Use @skills:name to search a specific skill's sub-directory (e.g. @skills:communication). "
         "Use '.' or empty for the primary root.",
       false},
      {"file_pattern", "string",
       "Optional glob pattern to filter files (e.g., '*.txt')", false},
      {"max_results", "integer",
       "Maximum number of results to return. Default is 100.", false}};
   return def;
}

bool SearchFilesToolHandler::ValidateParams(const QJsonObject& aParams, QString& aError) const
{
   if (!aParams.contains("pattern") || aParams["pattern"].toString().trimmed().isEmpty()) {
      aError = QStringLiteral("Missing required parameter: pattern");
      return false;
   }
   return true;
}

ToolResult SearchFilesToolHandler::Execute(const QJsonObject& aParams)
{
   const QString pattern     = aParams["pattern"].toString().trimmed();
   const QString path        = aParams.value("path").toString();
   QString       filePattern = aParams.value("file_pattern").toString();
   const int     maxResults  = ParseMaxResults(aParams, "max_results");

   if (filePattern.trimmed().isEmpty() && mPrefObjectPtr) {
      filePattern = mPrefObjectPtr->GetSearchExtensions();
   }

   QString absDir;
   QString error;
   if (!ResolveSearchDirectory(mPathAccess, path, absDir, error)) {
      return {false, error, {}, false};
   }

   QStringList fileGlobs = BuildFileGlobList(filePattern);

   // When searching inside the skills directory, always include *.md so that
   // skill documentation (SKILL.md, references/*.md) is never missed.
   if (IsInsideSkillsDirectory(absDir) && !fileGlobs.contains(QStringLiteral("*.md"))) {
      fileGlobs.append(QStringLiteral("*.md"));
   }

   QList<SearchResult> results =
      ProjectBridge::SearchInFilesAbs(pattern, absDir, fileGlobs, maxResults);
   return BuildSearchResult(results, pattern);
}

void SearchFilesToolHandler::ExecuteAsync(const QJsonObject& aParams,
                                          std::function<void(ToolResult)> aOnComplete)
{
   const QString pattern     = aParams["pattern"].toString().trimmed();
   const QString path        = aParams.value("path").toString();
   QString       filePattern = aParams.value("file_pattern").toString();
   const int     maxResults  = ParseMaxResults(aParams, "max_results");

   if (filePattern.trimmed().isEmpty() && mPrefObjectPtr) {
      filePattern = mPrefObjectPtr->GetSearchExtensions();
   }

   QString absDir;
   QString error;
   if (!ResolveSearchDirectory(mPathAccess, path, absDir, error)) {
      DispatchAsync(aOnComplete, {false, error, {}, false});
      return;
   }
   QStringList fileGlobs = BuildFileGlobList(filePattern);

   // When searching inside the skills directory, always include *.md so that
   // skill documentation (SKILL.md, references/*.md) is never missed.
   if (IsInsideSkillsDirectory(absDir) && !fileGlobs.contains(QStringLiteral("*.md"))) {
      fileGlobs.append(QStringLiteral("*.md"));
   }

   QThread* worker = QThread::create([pattern, absDir, fileGlobs, maxResults, aOnComplete]() {
      QList<SearchResult> results =
         ProjectBridge::SearchInFilesAbs(pattern, absDir, fileGlobs, maxResults);
      const ToolResult result = BuildSearchResult(results, pattern);
      DispatchAsync(aOnComplete, result);
   });
   QObject::connect(worker, &QThread::finished, worker, &QObject::deleteLater);
   worker->start();
}

QString SearchFilesToolHandler::GenerateDiff(const QJsonObject& aParams) const
{
   const QString pattern = aParams.value(QStringLiteral("pattern")).toString().trimmed();
   const QString path = aParams.value(QStringLiteral("path")).toString().trimmed();
   const QString filePattern = aParams.value(QStringLiteral("file_pattern")).toString().trimmed();
   const int maxResults = ParseMaxResults(aParams, "max_results");

   QStringList lines;
   lines << QStringLiteral("Pattern: %1").arg(pattern);
   lines << QStringLiteral("Path: %1").arg(path.isEmpty() ? QStringLiteral(".") : path);
   if (!filePattern.isEmpty()) {
      lines << QStringLiteral("File Pattern: %1").arg(filePattern);
   }
   lines << QStringLiteral("Max Results: %1").arg(maxResults);
   return lines.join('\n');
}

} // namespace AiChat
