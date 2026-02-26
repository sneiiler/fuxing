// -----------------------------------------------------------------------------
// File: AiChatEditorBridge.cpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#include "AiChatEditorBridge.hpp"
#include "AiChatProjectBridge.hpp"
#include "AiChatEncodingUtils.hpp"

#include <QByteArray>
#include <QDir>
#include <QFile>
#include <QFileInfo>
#include <QTextCodec>
#include <QTextStream>
#include <QTextBlock>

#include "Editor.hpp"
#include "EditorManager.hpp"
#include "Project.hpp"
#include "ProjectWorkspace.hpp"
#include "TextSource.hpp"
#include "TextSourceCache.hpp"
#include "WsfEditor.hpp"

namespace AiChat
{

// ---------------------------------------------------------------------------
// Utilities
// ---------------------------------------------------------------------------

QString EditorBridge::ResolvePath(const QString& aPath)
{
   const QString root = QDir::cleanPath(ProjectBridge::GetWorkspaceRoot());
   if (root.isEmpty()) {
      return QDir::cleanPath(QDir::currentPath() + "/" + aPath);
   }

   QString resolved;
   QFileInfo info(aPath);
   resolved = info.isAbsolute()
      ? QDir::cleanPath(aPath)
      : QDir::cleanPath(root + "/" + aPath);

   // Sandbox: resolved path must be inside the project directory.
   // On Windows, compare case-insensitively.
#ifdef Q_OS_WIN
   const Qt::CaseSensitivity cs = Qt::CaseInsensitive;
#else
   const Qt::CaseSensitivity cs = Qt::CaseSensitive;
#endif
   const QString rootSlash = root.endsWith('/') ? root : root + '/';
   if (!(resolved.startsWith(rootSlash, cs) || resolved.compare(root, cs) == 0)) {
      // Path escapes workspace roots — refuse and return empty
      return {};
   }

   return resolved;
}

QString EditorBridge::ResolveReadPath(const QString& aPath)
{
   return ResolvePath(aPath);
}

// ---------------------------------------------------------------------------
// ReadFile
// ---------------------------------------------------------------------------

QString EditorBridge::ReadFile(const QString& aFilePath, int aStartLine, int aEndLine)
{
   const QString absPath = ResolveReadPath(aFilePath);

   // Try in-memory TextSource first
   auto* workspace = wizard::ProjectWorkspace::Instance();
   if (workspace)
   {
      auto* cache = workspace->GetSourceCache();
      if (cache)
      {
         wizard::TextSource* source = cache->FindSource(
            QDir::toNativeSeparators(absPath).toStdString(), false);
         if (source && source->IsLoaded())
         {
            QString fullText;
            if (auto* qtDoc = source->GetDocumentAsQTextDocument()) {
               fullText = qtDoc->toPlainText();
            } else if (auto* doc = source->GetSource()) {
               QByteArray raw(doc->GetPointer(0, doc->Size()),
                              static_cast<int>(doc->Size()));
               fullText = EncodingUtils::DecodeBytes(raw);
            }

            // Clean up trailing null chars that may come from the editor buffer
            if (!fullText.isNull()) {
               EncodingUtils::StripTrailingNullChars(fullText);
            }

            if (fullText.isNull()) {
               return QString();
            }

            if (aStartLine <= 0 && aEndLine <= 0)
            {
               return fullText;
            }

            // Extract line range
            QStringList lines = fullText.split('\n');
            int start = qMax(0, aStartLine - 1);
            int end   = (aEndLine <= 0) ? lines.size() : qMin(aEndLine, lines.size());

            QStringList slice;
            for (int i = start; i < end; ++i)
            {
               slice.append(lines[i]);
            }
            return slice.join('\n');
         }
      }
   }

   // Fallback: read from disk with smart encoding detection
   QFile file(absPath);
   if (!file.open(QIODevice::ReadOnly))
   {
      return QString(); // null — caller checks with isNull()
   }

   QByteArray rawData = file.readAll();
   file.close();

   QString fullText = EncodingUtils::DecodeBytes(rawData);
   if (fullText.isNull()) {
      return QString();
   }

   if (aStartLine <= 0 && aEndLine <= 0)
   {
      return fullText;
   }

   // Extract specific lines
   QStringList lines = fullText.split('\n');
   int start = qMax(0, aStartLine - 1);
   int end   = (aEndLine <= 0) ? lines.size() : qMin(aEndLine, lines.size());

   QStringList result;
   for (int i = start; i < end; ++i)
   {
      result.append(lines[i]);
   }
   return result.join('\n');
}

// ---------------------------------------------------------------------------
// WriteFile
// ---------------------------------------------------------------------------

bool EditorBridge::WriteFile(const QString& aFilePath, const QString& aContent)
{
   const QString absPath = ResolvePath(aFilePath);

   // Ensure parent directory exists
   QFileInfo info(absPath);
   QDir().mkpath(info.absolutePath());

   QFile file(absPath);
   if (!file.open(QIODevice::WriteOnly | QIODevice::Text | QIODevice::Truncate))
   {
      return false;
   }

   QTextStream out(&file);
   out.setCodec("UTF-8");
   out << aContent;
   out.flush();           // MUST flush QTextStream buffer before closing QFile
   file.close();

   // Synchronize the in-memory TextSource with the disk content so that
   // subsequent ReadFile() calls (which read from the editor buffer first)
   // return the newly written content instead of stale data.
   auto* workspace = wizard::ProjectWorkspace::Instance();
   if (workspace)
   {
      auto* cache = workspace->GetSourceCache();
      if (cache)
      {
         const std::string nativePath = QDir::toNativeSeparators(absPath).toStdString();
         wizard::TextSource* source = cache->FindSource(nativePath, false);
         if (source && source->IsLoaded())
         {
            // Force TextSource to re-read from disk, updating the editor buffer.
            // When mModified is false (no user edits), this reloads silently.
            // ReadSource(true) also triggers TriggerReparse() if the file is
            // part of the current scenario — this is sufficient for the parse
            // cycle to pick up the change.
            source->ReadSource(true);
         }
      }
      // NOTE: We intentionally do NOT call ScheduleCheckingFilesForModification()
      // here.  ReadSource(true) above already handles the written source.
      // The async check (QtConcurrent::filtered) can block the
      // ProjectWorkspace::Update() loop — if a subsequent set_startup_file
      // triggers InvalidateScenario/TouchParse, the pending async check
      // prevents Update() from starting the reparse, delaying globe and
      // project-browser icon updates.
   }

   return true;
}

// ---------------------------------------------------------------------------
// Editor state queries
// ---------------------------------------------------------------------------

QString EditorBridge::GetCurrentFilePath()
{
   auto* workspace = wizard::ProjectWorkspace::Instance();
   if (!workspace) return {};

   auto* editor = workspace->GetEditorManager()->GetCurrentEditor();
   if (!editor) return {};

   auto* source = editor->GetSource();
   if (!source) return {};

   return QString::fromStdString(source->GetSystemPath());
}

int EditorBridge::GetCurrentLine()
{
   auto* workspace = wizard::ProjectWorkspace::Instance();
   if (!workspace) return 0;

   auto* editor = workspace->GetEditorManager()->GetCurrentEditor();
   if (!editor) return 0;

   size_t offset = 0, line = 0, col = 0;
   editor->GetCurrentPosition(offset, line, col);
   return static_cast<int>(line) + 1; // Convert to 1-based
}

void EditorBridge::OpenFileAtLine(const QString& aFilePath, int aLine)
{
   const QString absPath = ResolvePath(aFilePath);

   auto* workspace = wizard::ProjectWorkspace::Instance();
   if (!workspace) return;

   wizard::Editor* editor = workspace->GotoFile(absPath.toStdString());
   if (editor && aLine > 0)
   {
      editor->GoToLine(static_cast<size_t>(aLine));
   }
}

QStringList EditorBridge::GetOpenFiles()
{
   QStringList result;

   auto* workspace = wizard::ProjectWorkspace::Instance();
   if (!workspace) return result;

   const auto& editorMap = workspace->GetEditorManager()->GetEditorMap();
   for (const auto& pair : editorMap)
   {
      result.append(pair.first);
   }
   return result;
}

bool EditorBridge::SetAiChangeDecorations(const QString& aFilePath,
                                          const QList<AiLineHighlight>& aHighlights,
                                          const QMap<int, QColor>& aMarkers)
{
   const QString absPath = QDir::toNativeSeparators(ResolvePath(aFilePath));

   auto* workspace = wizard::ProjectWorkspace::Instance();
   if (!workspace) return false;

   auto* manager = workspace->GetEditorManager();
   if (!manager) return false;

   const auto& editorMap = manager->GetEditorMap();
   auto it = editorMap.find(absPath);
   if (it == editorMap.end()) return false;

   auto* editorPtr = dynamic_cast<wizard::WsfEditor*>(it->second);
   if (!editorPtr) return false;

   QList<QTextEdit::ExtraSelection> selections;
   for (const auto& highlight : aHighlights)
   {
      const int startLine = qMax(0, highlight.startLine);
      const int endLine = qMax(startLine, highlight.endLine);
      for (int line = startLine; line <= endLine; ++line)
      {
         QTextEdit::ExtraSelection sel;
         sel.cursor = QTextCursor(editorPtr->document()->findBlockByLineNumber(line));
         sel.cursor.movePosition(QTextCursor::EndOfLine, QTextCursor::KeepAnchor);
         sel.format.setProperty(QTextFormat::FullWidthSelection, true);
         sel.format.setBackground(highlight.color);
         selections.append(sel);
      }
   }

   editorPtr->SetAiChangeSelections(selections);
   editorPtr->SetAiChangeMarkers(aMarkers);
   return true;
}

void EditorBridge::ClearAiChangeDecorations(const QString& aFilePath)
{
   const QString absPath = QDir::toNativeSeparators(ResolvePath(aFilePath));

   auto* workspace = wizard::ProjectWorkspace::Instance();
   if (!workspace) return;

   auto* manager = workspace->GetEditorManager();
   if (!manager) return;

   const auto& editorMap = manager->GetEditorMap();
   auto it = editorMap.find(absPath);
   if (it == editorMap.end()) return;

   auto* editorPtr = dynamic_cast<wizard::WsfEditor*>(it->second);
   if (!editorPtr) return;

   editorPtr->ClearAiChangeDecorations();
}

QWidget* EditorBridge::GetEditorViewport(const QString& aFilePath)
{
   const QString absPath = QDir::toNativeSeparators(ResolvePath(aFilePath));

   auto* workspace = wizard::ProjectWorkspace::Instance();
   if (!workspace) return nullptr;

   auto* manager = workspace->GetEditorManager();
   if (!manager) return nullptr;

   const auto& editorMap = manager->GetEditorMap();
   auto it = editorMap.find(absPath);
   if (it == editorMap.end()) return nullptr;

   auto* editorPtr = dynamic_cast<wizard::WsfEditor*>(it->second);
   if (!editorPtr) return nullptr;

   return editorPtr->viewport();
}

} // namespace AiChat
