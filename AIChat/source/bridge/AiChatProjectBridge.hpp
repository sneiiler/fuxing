// -----------------------------------------------------------------------------
// File: AiChatProjectBridge.hpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#ifndef AICHAT_PROJECT_BRIDGE_HPP
#define AICHAT_PROJECT_BRIDGE_HPP

#include <QString>
#include <QStringList>

namespace AiChat
{

/// Search result within files
struct SearchResult
{
   QString filePath;
   int     lineNumber;   ///< 1-based
   QString lineContent;
};

/// Bridge to Wizard project subsystem.
class ProjectBridge
{
public:
   /// Get the current project name.
   static QString GetProjectName();

   /// Get the project root directory (absolute path).
   static QString GetProjectDirectory();

   /// Get the active workspace root directory (absolute path).
   /// Always returns the project directory to ensure a stable root across the session.
   static QString GetWorkspaceRoot();

   /// Get the list of startup (main) files.
   static QStringList GetStartupFiles();

   /// List files in a directory.
   /// @param aDirectory  The directory path (absolute or project-relative)
   /// @param aRecursive  If true, list recursively
   /// @param aPattern    Optional glob pattern (e.g. "*.wsf")
   static QStringList ListFiles(const QString& aDirectory, bool aRecursive = false,
                                const QString& aPattern = {});

   /// List files in an absolute directory path (thread-friendly; no workspace access).
   /// @param aAbsDirectory Absolute directory path
   /// @param aRecursive    If true, list recursively
   /// @param aNameFilters  Optional glob filters (e.g. "*.wsf")
   static QStringList ListFilesAbs(const QString& aAbsDirectory, bool aRecursive,
                                   const QStringList& aNameFilters = {});

   /// Search for a text pattern within files under a directory.
   /// @param aPattern    The text to search for (plain text)
   /// @param aDirectory  The directory to search in (absolute or project-relative)
   /// @param aFileGlob   Optional file glob (e.g. "*.wsf")
   /// @param aMaxResults Maximum number of results
   static QList<SearchResult> SearchInFiles(const QString& aPattern,
                                             const QString& aDirectory = {},
                                             const QString& aFileGlob = {},
                                             int aMaxResults = 100);

   /// Search in files under an absolute directory path (thread-friendly; no workspace access).
   static QList<SearchResult> SearchInFilesAbs(const QString& aPattern,
                                                const QString& aAbsDirectory,
                                                const QStringList& aNameFilters = {},
                                                int aMaxResults = 100);

   /// Set the project's startup files.
   /// @param aFiles List of file paths (absolute or project-relative)
   /// @param aError Populated on failure
   static bool SetStartupFiles(const QStringList& aFiles, QString& aError);

   /// Resolve a path for read-only access (no sandbox restriction).
   /// Returns the absolute, cleaned path; does NOT reject paths outside the project.
   static QString ResolveReadPath(const QString& aPath);

private:
   /// Resolve a path relative to the primary workspace root (sandboxed — write operations).
   static QString ResolvePath(const QString& aPath);
};

} // namespace AiChat

#endif // AICHAT_PROJECT_BRIDGE_HPP
