// -----------------------------------------------------------------------------
// File: AiChatPathAccessManager.hpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#ifndef AICHAT_PATH_ACCESS_MANAGER_HPP
#define AICHAT_PATH_ACCESS_MANAGER_HPP

#include <QMap>
#include <QRegularExpression>
#include <QString>
#include <QStringList>

namespace AiChat
{

class PrefObject;

struct WorkspaceRoot
{
   QString name;
   QString path;       // absolute, cleaned
   bool    isPrimary{false};
};

struct PathResolution
{
   QString inputPath;      // raw input (may include @workspace:)
   QString displayPath;    // normalized display path
   QString resolvedPath;   // absolute, cleaned
   QString workspaceName;  // matched workspace name (if any)
   QString workspaceRoot;  // absolute root path (if any)
   QString relPath;        // path relative to workspace root (if any)
   bool    isExternal{false};
   bool    ok{false};
   QString error;
};

class PathAccessManager
{
public:
   explicit PathAccessManager(PrefObject* aPrefs = nullptr);

   void Refresh();

   PathResolution ResolveForRead(const QString& aPath) const;
   PathResolution ResolveForWrite(const QString& aPath) const;
   PathResolution ResolveForList(const QString& aPath) const;
   PathResolution ResolveForSearch(const QString& aPath) const;

   /// Register an additional directory that read-only tools (read_file,
   /// list_files, search_files) are allowed to access even though it is
   /// outside the workspace root.  Intended for the global skills directory.
   void AddAllowedExternalPath(const QString& aPath);

   struct CommandResolution
   {
      QString command;      // command without @workspace prefix
      QString workingDir;   // resolved working directory
      QString displayDir;   // for UI/error
      bool    ok{false};
      QString error;
   };

   CommandResolution ResolveCommand(const QString& aCommand, const QString& aWorkingDir) const;

   QStringList WorkspaceNames() const;
   QString PrimaryRoot() const;

private:
   struct IgnoreRule
   {
      QRegularExpression regex;
      bool negated{false};
      QString pattern;
   };

   void LoadWorkspaceRoots();
   void LoadIgnoreRules();
   void LoadIgnoreRulesForRoot(const WorkspaceRoot& aRoot, const QStringList& aExtraPatterns);

   bool IsIgnored(const PathResolution& aRes, QString& aPattern) const;

   static QString NormalizePath(const QString& aPath);
   static QString GlobToRegex(const QString& aGlob);

   PathResolution Resolve(const QString& aPath, bool aAllowExternal) const;

   PrefObject* mPrefs{nullptr};

   QList<WorkspaceRoot> mRoots;
   QStringList mAllowedExternalPaths;  ///< Extra read-only dirs (e.g. global skills)
   QMap<QString, QList<IgnoreRule>> mIgnoreRulesByRoot;
};

} // namespace AiChat

#endif // AICHAT_PATH_ACCESS_MANAGER_HPP
