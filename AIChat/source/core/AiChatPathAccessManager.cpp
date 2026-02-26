// -----------------------------------------------------------------------------
// File: AiChatPathAccessManager.cpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#include "AiChatPathAccessManager.hpp"

#include "AiChatPrefObject.hpp"
#include "../bridge/AiChatEditorBridge.hpp"
#include "../bridge/AiChatProjectBridge.hpp"

#include <QDebug>
#include <QDir>
#include <QFile>
#include <QFileInfo>
#include <QTextStream>

namespace AiChat
{

namespace
{
QStringList SplitPatterns(const QString& aPatterns)
{
   if (aPatterns.trimmed().isEmpty()) {
      return {};
   }

   QString normalized = aPatterns;
   normalized.replace(';', '\n');
   normalized.replace(',', '\n');

   QStringList lines = normalized.split(QRegularExpression(QStringLiteral("[\\r\\n]+")),
                                        Qt::SkipEmptyParts);
   for (QString& line : lines) {
      line = line.trimmed();
   }
   lines.removeAll(QString());
   return lines;
}

bool IsPathInsideRoot(const QString& aPath, const QString& aRoot)
{
   if (aRoot.isEmpty()) {
      return false;
   }

#ifdef Q_OS_WIN
   const Qt::CaseSensitivity cs = Qt::CaseInsensitive;
#else
   const Qt::CaseSensitivity cs = Qt::CaseSensitive;
#endif

   const QString root = QDir::cleanPath(aRoot);
   const QString rootSlash = root.endsWith('/') ? root : root + '/';
   return aPath.startsWith(rootSlash, cs) || aPath.compare(root, cs) == 0;
}
}

PathAccessManager::PathAccessManager(PrefObject* aPrefs)
   : mPrefs(aPrefs)
{
   Refresh();
}

void PathAccessManager::Refresh()
{
   LoadWorkspaceRoots();
   LoadIgnoreRules();
}

void PathAccessManager::LoadWorkspaceRoots()
{
   mRoots.clear();

   const QString rootPath = NormalizePath(ProjectBridge::GetWorkspaceRoot());
   if (rootPath.isEmpty()) {
      return;
   }

   WorkspaceRoot root;
   root.path = rootPath;
   root.isPrimary = true;
   root.name = QFileInfo(rootPath).fileName();
   if (root.name.isEmpty()) {
      root.name = QStringLiteral("workspace");
   }
   mRoots.append(root);
}

void PathAccessManager::LoadIgnoreRules()
{
   mIgnoreRulesByRoot.clear();
   QStringList extraPatterns;
   if (mPrefs) {
      extraPatterns = SplitPatterns(mPrefs->GetIgnorePatterns());
   }

   for (const auto& root : mRoots) {
      LoadIgnoreRulesForRoot(root, extraPatterns);
   }
}

void PathAccessManager::LoadIgnoreRulesForRoot(const WorkspaceRoot& aRoot,
                                               const QStringList& aExtraPatterns)
{
   QList<IgnoreRule> rules;
   const QStringList ignoreFiles = {
      QDir(aRoot.path).filePath(QStringLiteral(".aichatignore")),
      QDir(aRoot.path).filePath(QStringLiteral(".clineignore"))
   };

   QStringList lines = aExtraPatterns;

   for (const auto& filePath : ignoreFiles) {
      QFile file(filePath);
      if (!file.exists()) {
         continue;
      }
      if (!file.open(QIODevice::ReadOnly | QIODevice::Text)) {
         continue;
      }
      QTextStream in(&file);
      while (!in.atEnd()) {
         QString line = in.readLine().trimmed();
         if (line.isEmpty() || line.startsWith('#')) {
            continue;
         }
         lines.append(line);
      }
   }

   for (const QString& raw : lines) {
      QString line = raw.trimmed();
      if (line.isEmpty()) {
         continue;
      }
      bool negated = false;
      if (line.startsWith('!')) {
         negated = true;
         line = line.mid(1).trimmed();
      }
      if (line.isEmpty()) {
         continue;
      }

      const QString regexStr = GlobToRegex(line);
      IgnoreRule rule;
      rule.regex = QRegularExpression(regexStr);
      rule.negated = negated;
      rule.pattern = raw;
      rules.append(rule);
   }

   mIgnoreRulesByRoot.insert(aRoot.path, rules);
}

QString PathAccessManager::NormalizePath(const QString& aPath)
{
   return QDir::cleanPath(QDir::fromNativeSeparators(aPath));
}

QString PathAccessManager::GlobToRegex(const QString& aGlob)
{
   QString glob = NormalizePath(aGlob);
   if (glob.endsWith('/')) {
      glob += "**";
   }

   const bool hasSlash = glob.contains('/');

   QString rx;
   rx.reserve(glob.size() * 2);
   for (int i = 0; i < glob.size(); ++i) {
      const QChar ch = glob.at(i);
      if (ch == '*') {
         if (i + 1 < glob.size() && glob.at(i + 1) == '*') {
            rx += QStringLiteral(".*");
            ++i;
         } else {
            rx += QStringLiteral("[^/]*");
         }
      } else if (ch == '?') {
         rx += QStringLiteral("[^/]");
      } else if (QStringLiteral(".()[]{}+$^|\\").contains(ch)) {
         rx += '\\';
         rx += ch;
      } else {
         rx += ch;
      }
   }

   if (!hasSlash) {
      rx = QStringLiteral("(.*/)?") + rx;
   }

   return QStringLiteral("^%1$").arg(rx);
}

PathResolution PathAccessManager::Resolve(const QString& aPath, bool aAllowExternal) const
{
   // Dynamic workspace root refresh: the root may change as files are opened.
   // Always check if the current workspace root matches what we cached.
   {
      const QString currentRoot = NormalizePath(ProjectBridge::GetWorkspaceRoot());
      if (mRoots.isEmpty() || (!currentRoot.isEmpty() && PrimaryRoot() != currentRoot)) {
         const_cast<PathAccessManager*>(this)->Refresh();
      }
   }

   PathResolution res;
   res.inputPath = aPath;

   if (aPath.trimmed().isEmpty()) {
      res.error = QStringLiteral("Path is empty.");
      return res;
   }

   QString rawPath = aPath.trimmed();

   // Strip @workspace prefix if present
   // Supports: "@workspace:path", "@workspace/path", "@workspace" (bare)
   if (rawPath.startsWith('@')) {
      const int colonIdx = rawPath.indexOf(':');
      if (colonIdx > 0) {
         // @workspace:src/file.cpp → src/file.cpp
         rawPath = rawPath.mid(colonIdx + 1).trimmed();
      } else {
         // @workspace or @workspace/path/to/file (no colon)
         int sepIdx = rawPath.indexOf('/');
         if (sepIdx < 0) {
            sepIdx = rawPath.indexOf('\\');
         }
         if (sepIdx > 0) {
            rawPath = rawPath.mid(sepIdx + 1).trimmed();
         } else {
            // Bare @workspace → resolve to workspace root
            rawPath.clear();
         }
      }
      if (rawPath.isEmpty()) {
         rawPath = QStringLiteral(".");
      }
   }

   QFileInfo info(rawPath);
   if (info.isAbsolute()) {
      const QString absPath = NormalizePath(rawPath);
      res.resolvedPath = absPath;
      res.displayPath = rawPath;

      for (const auto& root : mRoots) {
         if (IsPathInsideRoot(absPath, root.path)) {
            res.workspaceName = root.name;
            res.workspaceRoot = root.path;
            res.relPath = NormalizePath(QDir(root.path).relativeFilePath(absPath));
            res.isExternal = false;
            res.ok = true;
            return res;
         }
      }

      res.isExternal = true;
      res.ok = aAllowExternal;
      if (!res.ok) {
         res.error = QStringLiteral("Path is outside the workspace root.");
      }
      return res;
   }

   WorkspaceRoot baseRoot;
   bool hasRoot = false;
   if (!mRoots.isEmpty()) {
      baseRoot = mRoots.front();
      hasRoot = true;
   }

   if (!hasRoot || baseRoot.path.isEmpty()) {
      QStringList rootInfo;
      for (const auto& root : mRoots) {
         rootInfo << QStringLiteral("%1=%2").arg(root.name, root.path);
      }
      qWarning() << "[AIChat::PathAccess] No workspace root available."
               << "primary=" << PrimaryRoot()
               << "roots=" << rootInfo.join(QStringLiteral("; "));
      res.error = QStringLiteral("No workspace root available.");
      return res;
   }

   const QString combined = NormalizePath(baseRoot.path + "/" + rawPath);
   res.resolvedPath = combined;
   res.displayPath = rawPath;
   res.workspaceRoot = baseRoot.path;
   res.workspaceName = baseRoot.name;
   res.relPath = NormalizePath(QDir(baseRoot.path).relativeFilePath(combined));
   res.isExternal = false;
   res.ok = true;
   return res;
}

bool PathAccessManager::IsIgnored(const PathResolution& aRes, QString& aPattern) const
{
   aPattern.clear();
   if (!aRes.ok || aRes.isExternal || aRes.workspaceRoot.isEmpty()) {
      return false;
   }

   const auto rulesIt = mIgnoreRulesByRoot.constFind(aRes.workspaceRoot);
   if (rulesIt == mIgnoreRulesByRoot.constEnd()) {
      return false;
   }

   const QString relPath = NormalizePath(aRes.relPath);

   bool ignored = false;
   QString matched;
   for (const auto& rule : rulesIt.value()) {
      if (rule.regex.match(relPath).hasMatch()) {
         ignored = !rule.negated;
         matched = rule.pattern;
      }
   }

   if (ignored) {
      aPattern = matched;
   }
   return ignored;
}

void PathAccessManager::AddAllowedExternalPath(const QString& aPath)
{
   const QString cleaned = NormalizePath(aPath);
   if (!cleaned.isEmpty() && !mAllowedExternalPaths.contains(cleaned)) {
      mAllowedExternalPaths.append(cleaned);
   }
}

PathResolution PathAccessManager::ResolveForRead(const QString& aPath) const
{
   PathResolution res = Resolve(aPath, false);

   // Allow read access up to 2 directory levels above the workspace root
   if (!res.ok && res.isExternal) {
      const QString primary = PrimaryRoot();
      if (!primary.isEmpty() && !res.resolvedPath.isEmpty()) {
         QDir dir(primary);
         for (int i = 0; i < 2; ++i) {
            if (!dir.cdUp()) break;
         }
         const QString ancestorRoot = NormalizePath(dir.absolutePath());
         if (IsPathInsideRoot(res.resolvedPath, ancestorRoot)) {
            res.ok = true;
            res.error.clear();
         }
      }
   }

   // Allow read access to explicitly registered external paths (e.g. skills dir)
   if (!res.ok && res.isExternal && !res.resolvedPath.isEmpty()) {
      for (const auto& allowed : mAllowedExternalPaths) {
         if (IsPathInsideRoot(res.resolvedPath, allowed)) {
            res.ok = true;
            res.error.clear();
            break;
         }
      }
   }

   if (!res.ok) {
      return res;
   }

   if (!res.isExternal) {
      QString pattern;
      if (IsIgnored(res, pattern)) {
         res.ok = false;
         res.error = QStringLiteral("Path is blocked by ignore rules: %1").arg(pattern);
      }
   }

   return res;
}

PathResolution PathAccessManager::ResolveForWrite(const QString& aPath) const
{
   PathResolution res = Resolve(aPath, false);
   if (!res.ok) {
      return res;
   }

   if (res.isExternal) {
      res.ok = false;
      res.error = QStringLiteral("Writes outside the workspace root are not allowed.");
      return res;
   }

   QString pattern;
   if (IsIgnored(res, pattern)) {
      res.ok = false;
      res.error = QStringLiteral("Path is blocked by ignore rules: %1").arg(pattern);
   }

   return res;
}

PathResolution PathAccessManager::ResolveForList(const QString& aPath) const
{
   return ResolveForRead(aPath);
}

PathResolution PathAccessManager::ResolveForSearch(const QString& aPath) const
{
   return ResolveForRead(aPath);
}

PathAccessManager::CommandResolution PathAccessManager::ResolveCommand(const QString& aCommand,
                                                                       const QString& aWorkingDir) const
{
   CommandResolution res;
   res.command = aCommand.trimmed();

   if (res.command.isEmpty()) {
      res.error = QStringLiteral("Command is empty.");
      return res;
   }

   const QString workingDir = aWorkingDir.trimmed();
   if (!workingDir.isEmpty()) {
      PathResolution dirRes = ResolveForRead(workingDir);
      if (!dirRes.ok) {
         res.error = dirRes.error;
         return res;
      }
      res.workingDir = dirRes.resolvedPath;
      res.displayDir = dirRes.displayPath;
      res.ok = true;
      return res;
   }

   const QString primary = PrimaryRoot();
   if (primary.isEmpty()) {
      // Try refreshing in case workspace root became available
      const_cast<PathAccessManager*>(this)->Refresh();
   }
   const QString resolvedPrimary = PrimaryRoot();
   if (resolvedPrimary.isEmpty()) {
      QStringList rootInfo;
      for (const auto& root : mRoots) {
         rootInfo << QStringLiteral("%1=%2").arg(root.name, root.path);
      }
      qWarning() << "[AIChat::PathAccess] No workspace root available for command."
               << "primary=" << PrimaryRoot()
               << "roots=" << rootInfo.join(QStringLiteral("; "));
      res.error = QStringLiteral("No workspace root available for command.");
      return res;
   }

   res.workingDir = resolvedPrimary;
   res.displayDir = resolvedPrimary;
   res.ok = true;
   return res;
}

QStringList PathAccessManager::WorkspaceNames() const
{
   QStringList names;
   for (const auto& root : mRoots) {
      names.append(root.name);
   }
   return names;
}

QString PathAccessManager::PrimaryRoot() const
{
   for (const auto& root : mRoots) {
      if (root.isPrimary) {
         return root.path;
      }
   }
   return mRoots.isEmpty() ? QString() : mRoots.front().path;
}

} // namespace AiChat
