// -----------------------------------------------------------------------------
// File: ListCodeDefinitionNamesToolHandler.cpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#include "ListCodeDefinitionNamesToolHandler.hpp"

#include "../bridge/AiChatEditorBridge.hpp"
#include "../core/AiChatPathAccessManager.hpp"

#include <QFileInfo>
#include <QRegularExpression>
#include <QSet>

namespace AiChat
{

namespace
{
struct Definition
{
   QString kind;
   QString name;
};

QList<Definition> ExtractPython(const QStringList& aLines)
{
   QList<Definition> defs;
   QRegularExpression re(QStringLiteral("^\\s*(def|class)\\s+([A-Za-z_][A-Za-z0-9_]*)"));
   for (const auto& line : aLines) {
      const auto m = re.match(line);
      if (m.hasMatch()) {
         Definition d;
         d.kind = m.captured(1);
         d.name = m.captured(2);
         defs.append(d);
      }
   }
   return defs;
}

QList<Definition> ExtractCpp(const QStringList& aLines)
{
   QList<Definition> defs;
   QRegularExpression typeRe(QStringLiteral("^\\s*(class|struct|enum|namespace)\\s+([A-Za-z_][A-Za-z0-9_]*)"));
   QRegularExpression funcRe(
      QStringLiteral("^\\s*[A-Za-z_][A-Za-z0-9_:<>,\\s\t*&~]*\\s+([A-Za-z_][A-Za-z0-9_:]*)\\s*\\([^;]*\\)\\s*(?:const)?\\s*\\{"));

   for (const auto& line : aLines) {
      const auto typeMatch = typeRe.match(line);
      if (typeMatch.hasMatch()) {
         Definition d;
         d.kind = typeMatch.captured(1);
         d.name = typeMatch.captured(2);
         defs.append(d);
         continue;
      }
      const auto funcMatch = funcRe.match(line);
      if (funcMatch.hasMatch()) {
         Definition d;
         d.kind = QStringLiteral("function");
         d.name = funcMatch.captured(1);
         defs.append(d);
      }
   }
   return defs;
}

QList<Definition> ExtractJsTs(const QStringList& aLines)
{
   QList<Definition> defs;
   QRegularExpression typeRe(QStringLiteral("^\\s*(?:export\\s+)?(class|interface|function)\\s+([A-Za-z_][A-Za-z0-9_]*)"));
   QRegularExpression constFnRe(QStringLiteral("^\\s*(?:export\\s+)?const\\s+([A-Za-z_][A-Za-z0-9_]*)\\s*=\\s*\\(.*\\)\\s*=>"));

   for (const auto& line : aLines) {
      const auto typeMatch = typeRe.match(line);
      if (typeMatch.hasMatch()) {
         Definition d;
         d.kind = typeMatch.captured(1);
         d.name = typeMatch.captured(2);
         defs.append(d);
         continue;
      }
      const auto constMatch = constFnRe.match(line);
      if (constMatch.hasMatch()) {
         Definition d;
         d.kind = QStringLiteral("function");
         d.name = constMatch.captured(1);
         defs.append(d);
      }
   }
   return defs;
}

QString FormatDefinitions(const QList<Definition>& aDefs)
{
   if (aDefs.isEmpty()) {
      return QStringLiteral("No definitions found.");
   }

   QStringList lines;
   lines << QStringLiteral("Found %1 definitions:").arg(aDefs.size());
   for (const auto& def : aDefs) {
      lines << QStringLiteral("- %1: %2").arg(def.kind, def.name);
   }
   return lines.join('\n');
}

} // namespace

ListCodeDefinitionNamesToolHandler::ListCodeDefinitionNamesToolHandler(PathAccessManager* aPathAccess)
   : mPathAccess(aPathAccess)
{
}

ToolDefinition ListCodeDefinitionNamesToolHandler::GetDefinition() const
{
   ToolDefinition def;
   def.name        = QStringLiteral("list_code_definition_names");
   def.description = QStringLiteral(
      "List top-level code definitions (classes, functions, etc.) in a source file. "
      "Uses a lightweight regex-based parser.");
   def.parameters = {
      {"path", "string", "Path to the source file (relative to workspace root or absolute)", true}
   };
   return def;
}

bool ListCodeDefinitionNamesToolHandler::ValidateParams(const QJsonObject& aParams, QString& aError) const
{
   if (!aParams.contains("path") || aParams["path"].toString().trimmed().isEmpty()) {
      aError = QStringLiteral("Missing required parameter: path");
      return false;
   }
   return true;
}

ToolResult ListCodeDefinitionNamesToolHandler::Execute(const QJsonObject& aParams)
{
   QString path = aParams["path"].toString().trimmed();
   QString resolved = path;

   if (mPathAccess) {
      const auto res = mPathAccess->ResolveForRead(path);
      if (!res.ok) {
         return {false, QStringLiteral("Error: %1").arg(res.error), {}, false};
      }
      resolved = res.resolvedPath;
   }

   QString content = EditorBridge::ReadFile(resolved);
   if (content.isNull()) {
      return {false, QStringLiteral("Error: Could not read file '%1'.").arg(path), {}, false};
   }

   QStringList lines = content.split('\n');
   const QString ext = QFileInfo(resolved).suffix().toLower();

   QList<Definition> defs;
   if (ext == QStringLiteral("py")) {
      defs = ExtractPython(lines);
   } else if (ext == QStringLiteral("js") || ext == QStringLiteral("ts") || ext == QStringLiteral("tsx")) {
      defs = ExtractJsTs(lines);
   } else {
      defs = ExtractCpp(lines);
   }

   const QString output = FormatDefinitions(defs);
   return {true, output, QStringLiteral("Definitions: %1").arg(defs.size()), false};
}

QString ListCodeDefinitionNamesToolHandler::GenerateDiff(const QJsonObject& aParams) const
{
   const QString path = aParams.value(QStringLiteral("path")).toString().trimmed();
   return QStringLiteral("Path: %1").arg(path);
}

} // namespace AiChat
