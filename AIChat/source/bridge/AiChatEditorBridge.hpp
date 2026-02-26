// -----------------------------------------------------------------------------
// File: AiChatEditorBridge.hpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#ifndef AICHAT_EDITOR_BRIDGE_HPP
#define AICHAT_EDITOR_BRIDGE_HPP

#include <QMap>
#include <QColor>
#include <QString>
#include <QStringList>

class QWidget;

namespace AiChat
{

struct AiLineHighlight
{
   int    startLine{0};
   int    endLine{0};
   QColor color;
};

/// Bridge to Wizard editor subsystem — provides file I/O that prefers
/// in-memory TextSource over raw disk access.
class EditorBridge
{
public:
   /// Read a file's content (or a range of lines).
   /// Attempts to read from the in-memory TextSource first; falls back to disk.
   /// @param aFilePath  Absolute or project-relative path
   /// @param aStartLine 1-based start line (0 = from beginning)
   /// @param aEndLine   1-based end line   (0 = to end)
   static QString ReadFile(const QString& aFilePath, int aStartLine = 0, int aEndLine = 0);

   /// Write (create or overwrite) a file.
   /// If the file is open in an editor, updates via TextSource; otherwise writes to disk.
   /// @return true on success
   static bool WriteFile(const QString& aFilePath, const QString& aContent);

   /// Get the absolute file path of the currently active editor tab.
   /// Returns an empty string if no editor is open.
   static QString GetCurrentFilePath();

   /// Get the current cursor line number (1-based) in the active editor.
   static int GetCurrentLine();

   /// Open a file in the editor and optionally jump to a line.
   static void OpenFileAtLine(const QString& aFilePath, int aLine = 0);

   /// Get the list of all currently open editor file paths.
   static QStringList GetOpenFiles();

   /// Apply AI change highlights to an open editor.
   static bool SetAiChangeDecorations(const QString& aFilePath,
                                      const QList<AiLineHighlight>& aHighlights,
                                      const QMap<int, QColor>& aMarkers);

   /// Clear AI change highlights from an open editor.
   static void ClearAiChangeDecorations(const QString& aFilePath);

   /// Get the viewport widget of the editor for aFilePath (for overlay widgets).
   /// Returns nullptr if the file is not open or the editor is not a WsfEditor.
   static QWidget* GetEditorViewport(const QString& aFilePath);

   /// Resolve a potentially relative path against the primary workspace root (sandboxed — write operations).
   static QString ResolvePath(const QString& aPath);

   /// Resolve a path for read-only access (no sandbox restriction).
   static QString ResolveReadPath(const QString& aPath);
};

} // namespace AiChat

#endif // AICHAT_EDITOR_BRIDGE_HPP
