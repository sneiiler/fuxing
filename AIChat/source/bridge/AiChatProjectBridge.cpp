// -----------------------------------------------------------------------------
// File: AiChatProjectBridge.cpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#include "AiChatProjectBridge.hpp"
#include "AiChatEditorBridge.hpp"

#include <QApplication>
#include <QDir>
#include <QDirIterator>
#include <QFile>
#include <QFileInfo>
#include <QRegularExpression>
#include <QTextStream>

#include "Project.hpp"
#include "ProjectWorkspace.hpp"
#include "RunManager.hpp"
#include "Signals.hpp"

namespace AiChat
{

namespace
{
bool IsProbablyBinary(const QByteArray& aSample)
{
   if (aSample.isEmpty()) {
      return false;
   }

   int nonPrintable = 0;
   for (char c : aSample) {
      const unsigned char uc = static_cast<unsigned char>(c);
      if (uc == 0) {
         return true;
      }
      if (uc < 0x09 || (uc > 0x0D && uc < 0x20)) {
         ++nonPrintable;
      }
   }

   const double ratio = static_cast<double>(nonPrintable) / aSample.size();
   return ratio > 0.30;
}

QString SanitizeLine(const QString& aLine, int aMaxLen)
{
   QString cleaned;
   cleaned.reserve(qMin(aLine.size(), aMaxLen));
   for (const QChar ch : aLine) {
      if (ch == QChar('\t')) {
         cleaned += QChar(' ');
      } else if (ch.isPrint() || ch == QChar(' ')) {
         cleaned += ch;
      }
      if (cleaned.size() >= aMaxLen) {
         break;
      }
   }
   return cleaned;
}

QStringList ParseNameFilters(const QString& aFileGlob)
{
   if (aFileGlob.trimmed().isEmpty()) {
      return {};
   }

   QStringList filters;
   const QStringList tokens = aFileGlob.split(QRegularExpression(QStringLiteral("[\\s,;]+")),
                                              Qt::SkipEmptyParts);
   for (const auto& raw : tokens) {
      const QString token = raw.trimmed();
      if (token.isEmpty()) {
         continue;
      }
      filters.append(token);
   }
   filters.removeDuplicates();
   return filters;
}

QStringList DefaultSearchFilters()
{
   return QStringList{
      "*.wsf", "*.txt", "*.dat", "*.md", "*.json", "*.xml",
      "*.ini", "*.cfg", "*.conf", "*.csv", "*.log",
      "*.cpp", "*.c", "*.cc", "*.cxx", "*.hpp", "*.h",
      "*.hh", "*.hxx", "*.inl", "*.qml", "*.qrc", "*.qss",
      "*.py", "*.js", "*.ts", "*.tsx", "*.java", "*.cs",
      "*.cmake", "CMakeLists.txt", "*.bat", "*.ps1"};
}

wizard::ProjectWorkspace* CurrentWorkspace()
{
   return wizard::ProjectWorkspace::Instance();
}

wizard::Project* CurrentProject()
{
   auto* workspace = CurrentWorkspace();
   if (!workspace) return nullptr;
   return workspace->GetProject();
}

QString ResolveDirectoryOrProject(const QString& aDirectory)
{
   return aDirectory.isEmpty() ? ProjectBridge::GetProjectDirectory() : aDirectory;
}
} // namespace

// ---------------------------------------------------------------------------
// Utilities
// ---------------------------------------------------------------------------

QString ProjectBridge::ResolvePath(const QString& aPath)
{
   const QString root = QDir::cleanPath(GetWorkspaceRoot());
   if (root.isEmpty()) {
      return QDir::cleanPath(QDir::currentPath() + "/" + aPath);
   }

   QFileInfo info(aPath);
   const QString resolved = info.isAbsolute()
      ? QDir::cleanPath(aPath)
      : QDir::cleanPath(root + "/" + aPath);

#ifdef Q_OS_WIN
   const Qt::CaseSensitivity cs = Qt::CaseInsensitive;
#else
   const Qt::CaseSensitivity cs = Qt::CaseSensitive;
#endif
   const QString rootSlash = root.endsWith('/') ? root : root + '/';
   if (!(resolved.startsWith(rootSlash, cs) || resolved.compare(root, cs) == 0)) {
      return {};
   }

   return resolved;
}

QString ProjectBridge::ResolveReadPath(const QString& aPath)
{
   return ResolvePath(aPath);
}

// ---------------------------------------------------------------------------
// Project info
// ---------------------------------------------------------------------------

QString ProjectBridge::GetProjectName()
{
   auto* project = CurrentProject();
   if (!project) return {};
   return project->Name();
}

QString ProjectBridge::GetProjectDirectory()
{
   auto* project = CurrentProject();
   if (!project) return {};
   return QString::fromStdString(project->ProjectDirectory().GetSystemPath());
}

QString ProjectBridge::GetWorkspaceRoot()
{
   // Use the project directory as the stable workspace root.
   // NEVER derive root from the current editor file — that causes root drift
   // when the model creates files in subdirectories and the editor opens them.
   const QString projDir = GetProjectDirectory();
   if (!projDir.isEmpty()) {
      return QDir::cleanPath(projDir);
   }

   return QDir::cleanPath(QDir::currentPath());
}

QStringList ProjectBridge::GetStartupFiles()
{
   QStringList result;
   auto* project = CurrentProject();
   if (!project) return result;

   const auto& files = project->GetStartupFiles();
   for (const auto& f : files)
   {
      result.append(QString::fromStdString(f.GetSystemPath()));
   }
   return result;
}

// ---------------------------------------------------------------------------
// File listing
// ---------------------------------------------------------------------------

QStringList ProjectBridge::ListFiles(const QString& aDirectory, bool aRecursive, const QString& aPattern)
{
   const QString target = ResolveDirectoryOrProject(aDirectory);
   const QString absDir = ResolvePath(target);
   if (absDir.isEmpty()) {
      return {};
   }
   const QStringList nameFilters = ParseNameFilters(aPattern);
   return ListFilesAbs(absDir, aRecursive, nameFilters);
}

QStringList ProjectBridge::ListFilesAbs(const QString& aAbsDirectory, bool aRecursive,
                                        const QStringList& aNameFilters)
{
   QStringList result;

   QDir::Filters filters = QDir::Files | QDir::NoDotAndDotDot;
   QStringList nameFilters = aNameFilters;

   if (aRecursive)
   {
      QDirIterator it(aAbsDirectory, nameFilters, filters, QDirIterator::Subdirectories);
      while (it.hasNext())
      {
         it.next();
         result.append(it.filePath());
      }
   }
   else
   {
      QDir dir(aAbsDirectory);
      const auto entries = dir.entryInfoList(nameFilters, filters | QDir::Dirs);
      for (const auto& entry : entries)
      {
         QString name = entry.fileName();
         if (entry.isDir())
         {
            name += "/";
         }
         result.append(name);
      }
   }

   return result;
}

// ---------------------------------------------------------------------------
// File searching
// ---------------------------------------------------------------------------

QList<SearchResult> ProjectBridge::SearchInFiles(const QString& aPattern,
                                                  const QString& aDirectory,
                                                  const QString& aFileGlob,
                                                  int aMaxResults)
{
   const QString target = ResolveDirectoryOrProject(aDirectory);
   const QString absDir = ResolvePath(target);
   if (absDir.isEmpty()) {
      return {};
   }
   const QStringList nameFilters = ParseNameFilters(aFileGlob);
   return SearchInFilesAbs(aPattern, absDir, nameFilters, aMaxResults);
}

QList<SearchResult> ProjectBridge::SearchInFilesAbs(const QString& aPattern,
                                                     const QString& aAbsDirectory,
                                                     const QStringList& aNameFilters,
                                                     int aMaxResults)
{
   QList<SearchResult> results;

   QStringList nameFilters = aNameFilters;
   if (nameFilters.isEmpty())
   {
      nameFilters = DefaultSearchFilters();
   }

   QDirIterator it(aAbsDirectory, nameFilters, QDir::Files, QDirIterator::Subdirectories);
   while (it.hasNext() && results.size() < aMaxResults)
   {
      it.next();
      QFile file(it.filePath());
      if (!file.open(QIODevice::ReadOnly | QIODevice::Text)) continue;

      const QByteArray sample = file.read(4096);
      if (IsProbablyBinary(sample)) {
         file.close();
         continue;
      }
      file.seek(0);

      QTextStream in(&file);
      in.setCodec("UTF-8");
      int lineNum = 0;
      while (!in.atEnd() && results.size() < aMaxResults)
      {
         ++lineNum;
         QString line = in.readLine();
         if (line.contains(aPattern, Qt::CaseInsensitive))
         {
            SearchResult sr;
            sr.filePath    = it.filePath();
            sr.lineNumber  = lineNum;
            sr.lineContent = SanitizeLine(line.trimmed(), 500);
            results.append(sr);
         }
      }
   }

   return results;
}

bool ProjectBridge::SetStartupFiles(const QStringList& aFiles, QString& aError)
{
   if (aFiles.isEmpty()) {
      aError = QStringLiteral("No startup files provided.");
      return false;
   }

   auto* workspace = CurrentWorkspace();
   if (!workspace) {
      aError = QStringLiteral("No active project workspace.");
      return false;
   }

   auto* project = CurrentProject();
   if (!project) {
      aError = QStringLiteral("No active project.");
      return false;
   }

   std::vector<UtPath> startupFiles;
   startupFiles.reserve(static_cast<size_t>(aFiles.size()));

   for (const auto& file : aFiles)
   {
      const QString resolved = ResolvePath(file);
      if (resolved.isEmpty()) {
         aError = QStringLiteral("Startup file is outside workspace root: %1").arg(file);
         return false;
      }
      if (!QFileInfo::exists(resolved)) {
         aError = QStringLiteral("Startup file does not exist: %1").arg(file);
         return false;
      }
      startupFiles.emplace_back(resolved.toStdString());
   }

   project->SetStartupFiles(startupFiles);

   // Match the normal UI flow (ContextMenuActions::SetAsStartupFile):
   // sync the process CWD to the startup file's directory.
   if (!startupFiles.empty()) {
      wizRunMgr.SetWorkingDirectoryToProject(
         QString::fromStdString(startupFiles.front().GetSystemPath()));
   }

   // Request a *quick* DeferUpdate so that ParseUpdatedDeferred fires
   // immediately after the reparse completes, instead of waiting 2 seconds.
   // This eliminates the delay before project browser icons (wizard hats)
   // and other deferred handlers are updated.
   workspace->TouchParse(true);

   // Force a project browser tree rescan so that newly created files
   // (e.g. from a preceding write_to_file call) are discovered before
   // the startup file markers are applied.
   emit wizSignals->FileCheckRequested();

   // Re-emit ProjectStartupFilesChanged so that the project browser
   // (now with the up-to-date file tree) can correctly locate and
   // decorate the startup file items.  The first emission from inside
   // Project::SetStartupFiles may have missed newly created files.
   emit wizSignals->ProjectStartupFilesChanged(startupFiles);

   return true;
}

} // namespace AiChat
