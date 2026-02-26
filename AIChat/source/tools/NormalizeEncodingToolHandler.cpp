// -----------------------------------------------------------------------------
// File: NormalizeEncodingToolHandler.cpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-12
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#include "NormalizeEncodingToolHandler.hpp"

#include "../bridge/AiChatProjectBridge.hpp"

#include <QByteArray>
#include <QDir>
#include <QDirIterator>
#include <QFile>
#include <QFileInfo>
#include <QJsonArray>
#include <QTextCodec>

namespace AiChat
{

namespace
{
constexpr qint64 kDefaultMaxFileBytes = 5 * 1024 * 1024; // 5 MB
constexpr int kMaxDetailLines = 400;

struct EncodingInfo
{
   QString encoding;
   bool isBinary = false;
   bool hasNonAscii = false;
   bool isUtf8 = false;
   bool isUtf16 = false;
};

QStringList SplitPatterns(const QString& aPattern)
{
   if (aPattern.trimmed().isEmpty()) {
      return {};
   }
   QString normalized = aPattern;
   normalized.replace(',', ';');
   const QStringList parts = normalized.split(';', Qt::SkipEmptyParts);
   QStringList out;
   for (const QString& p : parts) {
      const QString trimmed = p.trimmed();
      if (!trimmed.isEmpty()) {
         out.append(trimmed);
      }
   }
   return out;
}

QStringList DefaultExcludeDirs()
{
   return {
      QStringLiteral(".git"),
      QStringLiteral(".aichat"),
      QStringLiteral(".vs"),
      QStringLiteral(".vscode"),
      QStringLiteral("bin"),
      QStringLiteral("build"),
      QStringLiteral("out"),
      QStringLiteral("dist")
   };
}

bool ShouldSkipDir(const QString& aDirName, const QStringList& aExclude)
{
   for (const QString& ex : aExclude) {
      if (aDirName.compare(ex, Qt::CaseInsensitive) == 0) {
         return true;
      }
   }
   return false;
}

EncodingInfo DetectEncoding(const QByteArray& aData)
{
   EncodingInfo info;
   if (aData.isEmpty()) {
      info.encoding = QStringLiteral("empty");
      info.isUtf8 = true;
      return info;
   }

   // BOM checks
   if (aData.size() >= 3 &&
       static_cast<unsigned char>(aData.at(0)) == 0xEF &&
       static_cast<unsigned char>(aData.at(1)) == 0xBB &&
       static_cast<unsigned char>(aData.at(2)) == 0xBF) {
      info.encoding = QStringLiteral("UTF-8-BOM");
      info.isUtf8 = true;
   } else if (aData.size() >= 2 &&
              static_cast<unsigned char>(aData.at(0)) == 0xFF &&
              static_cast<unsigned char>(aData.at(1)) == 0xFE) {
      info.encoding = QStringLiteral("UTF-16LE");
      info.isUtf16 = true;
   } else if (aData.size() >= 2 &&
              static_cast<unsigned char>(aData.at(0)) == 0xFE &&
              static_cast<unsigned char>(aData.at(1)) == 0xFF) {
      info.encoding = QStringLiteral("UTF-16BE");
      info.isUtf16 = true;
   }

   // Zero-byte heuristic for UTF-16
   int zeroEven = 0;
   int zeroOdd = 0;
   int nonAscii = 0;
   const int sample = qMin(aData.size(), 4096);
   for (int i = 0; i < sample; ++i) {
      const unsigned char b = static_cast<unsigned char>(aData.at(i));
      if (b == 0) {
         if ((i % 2) == 0) {
            ++zeroEven;
         } else {
            ++zeroOdd;
         }
      }
      if (b >= 0x80) {
         ++nonAscii;
      }
   }

   if (info.encoding.isEmpty()) {
      if (zeroOdd > sample / 6 && zeroEven < sample / 30) {
         info.encoding = QStringLiteral("UTF-16LE");
         info.isUtf16 = true;
      } else if (zeroEven > sample / 6 && zeroOdd < sample / 30) {
         info.encoding = QStringLiteral("UTF-16BE");
         info.isUtf16 = true;
      }
   }

   info.hasNonAscii = (nonAscii > 0);

   // Binary detection
   if (!info.isUtf16) {
      int nonPrintable = 0;
      bool hasNull = false;
      for (int i = 0; i < sample; ++i) {
         const unsigned char b = static_cast<unsigned char>(aData.at(i));
         if (b == 0) {
            hasNull = true;
            break;
         }
         const bool printable = (b == '\n' || b == '\r' || b == '\t' || (b >= 0x20 && b <= 0x7E));
         if (!printable && b < 0x80) {
            ++nonPrintable;
         }
      }
      const double ratio = (sample > 0) ? static_cast<double>(nonPrintable) / sample : 0.0;
      if (hasNull || ratio > 0.30) {
         info.isBinary = true;
      }
   }

   if (info.encoding.isEmpty()) {
      // Try UTF text detection
      QTextCodec* codec = QTextCodec::codecForUtfText(aData, nullptr);
      if (codec) {
         const QString name = QString::fromLatin1(codec->name());
         info.encoding = name.toUpper();
         info.isUtf8 = name.contains("UTF-8", Qt::CaseInsensitive);
         info.isUtf16 = name.contains("UTF-16", Qt::CaseInsensitive);
      } else {
         info.encoding = QStringLiteral("unknown");
      }
   }

   if (info.encoding == QStringLiteral("UTF-8") || info.encoding == QStringLiteral("UTF-8-BOM")) {
      info.isUtf8 = true;
   }

   return info;
}

QString DecodeWithCodec(const QByteArray& aData, const QString& aEncoding)
{
   if (aEncoding.startsWith("UTF-16", Qt::CaseInsensitive)) {
      QTextCodec* codec = QTextCodec::codecForName("UTF-16");
      return codec ? codec->toUnicode(aData) : QString();
   }
   if (aEncoding.startsWith("UTF-8", Qt::CaseInsensitive)) {
      QTextCodec* codec = QTextCodec::codecForName("UTF-8");
      return codec ? codec->toUnicode(aData) : QString();
   }
   return QString();
}

} // namespace

ToolDefinition NormalizeEncodingToolHandler::GetDefinition() const
{
   ToolDefinition def;
   def.name = QStringLiteral("normalize_workspace_encoding");
   def.description = QStringLiteral(
      "Audit or normalize workspace file encodings. Use mode='audit' to report encodings "
      "and non-ASCII content. Use mode='convert' to rewrite UTF-16/UTF-8-BOM files into UTF-8 "
      "(no BOM). This tool only scans within the workspace root.");
   def.parameters = {
      {"path", "string", "Root folder to scan (relative to workspace root). Default '.'", false},
      {"recursive", "boolean", "Whether to scan subdirectories. Default true.", false},
      {"file_pattern", "string", "Semicolon-separated glob patterns (e.g., '*.txt;*.wsf').", false},
      {"mode", "string", "'audit' or 'convert'. Default 'audit'.", false},
      {"max_file_kb", "integer", "Skip files larger than this size (KB). Default 5120.", false}
   };
   return def;
}

bool NormalizeEncodingToolHandler::ValidateParams(const QJsonObject& aParams, QString& aError) const
{
   if (aParams.contains("mode")) {
      const QString mode = aParams.value("mode").toString().trimmed().toLower();
      if (!mode.isEmpty() && mode != QStringLiteral("audit") && mode != QStringLiteral("convert")) {
         aError = QStringLiteral("Invalid mode. Use 'audit' or 'convert'.");
         return false;
      }
   }
   return true;
}

ToolResult NormalizeEncodingToolHandler::Execute(const QJsonObject& aParams)
{
   const QString workspaceRoot = ProjectBridge::GetWorkspaceRoot();
   if (workspaceRoot.isEmpty()) {
      return {false, QStringLiteral("Error: Workspace root is empty."), {}, false};
   }

   const QString relPath = aParams.value("path").toString().trimmed();
   const QString scanRoot = relPath.isEmpty() || relPath == QStringLiteral(".")
      ? workspaceRoot
      : QDir::cleanPath(workspaceRoot + "/" + relPath);

   const bool recursive = aParams.value("recursive").toBool(true);
   const QStringList patterns = SplitPatterns(aParams.value("file_pattern").toString());
   const QString mode = aParams.value("mode").toString().trimmed().toLower().isEmpty()
      ? QStringLiteral("audit")
      : aParams.value("mode").toString().trimmed().toLower();
   const QJsonValue maxKbVal = aParams.value("max_file_kb");
   const qint64 maxFileBytes = static_cast<qint64>(
      maxKbVal.isUndefined() ? 5120 : qMax(1, qRound(maxKbVal.toVariant().toDouble()))) * 1024;

   QStringList excludeDirs = DefaultExcludeDirs();

   QDirIterator it(scanRoot,
                   patterns.isEmpty() ? QStringList{QStringLiteral("*.*")} : patterns,
                   QDir::Files | QDir::NoSymLinks,
                   recursive ? QDirIterator::Subdirectories : QDirIterator::NoIteratorFlags);

   int scanned = 0;
   int converted = 0;
   int skippedBinary = 0;
   int skippedLarge = 0;
   int nonAsciiCount = 0;
   int unknownCount = 0;

   QStringList details;
   while (it.hasNext()) {
      const QString filePath = it.next();
      QFileInfo fi(filePath);

      if (ShouldSkipDir(fi.dir().dirName(), excludeDirs)) {
         continue;
      }

      if (fi.size() > maxFileBytes) {
         ++skippedLarge;
         continue;
      }

      QFile file(filePath);
      if (!file.open(QIODevice::ReadOnly)) {
         continue;
      }
      const QByteArray data = file.readAll();
      file.close();

      const EncodingInfo info = DetectEncoding(data);
      ++scanned;
      if (info.isBinary) {
         ++skippedBinary;
      }
      if (info.hasNonAscii) {
         ++nonAsciiCount;
      }
      if (info.encoding == QStringLiteral("unknown")) {
         ++unknownCount;
      }

      const QString relDisplay = QDir(workspaceRoot).relativeFilePath(filePath);
      if (details.size() < kMaxDetailLines) {
         details.append(QStringLiteral("%1 | %2 | %3 | %4 bytes | %5")
            .arg(info.isBinary ? "binary" : (info.hasNonAscii ? "non-ascii" : "ascii"))
            .arg(info.encoding)
            .arg(info.isUtf8 ? "utf8" : (info.isUtf16 ? "utf16" : "other"))
            .arg(fi.size())
            .arg(relDisplay));
      }

      if (mode == QStringLiteral("convert")) {
         if (info.isBinary || info.encoding == QStringLiteral("unknown")) {
            continue;
         }
         if (info.encoding == QStringLiteral("UTF-16LE") || info.encoding == QStringLiteral("UTF-16BE") ||
             info.encoding == QStringLiteral("UTF-8-BOM")) {
            const QString decoded = DecodeWithCodec(data, info.encoding);
            if (!decoded.isEmpty()) {
               QFile outFile(filePath);
               if (outFile.open(QIODevice::WriteOnly | QIODevice::Truncate | QIODevice::Text)) {
                  QTextStream out(&outFile);
                  out.setCodec("UTF-8");
                  out << decoded;
                  outFile.close();
                  ++converted;
               }
            }
         }
      }
   }

   if (details.size() >= kMaxDetailLines) {
      details.append(QStringLiteral("... (truncated)"));
   }

   QString summary = QStringLiteral(
      "Encoding %1 summary:\n"
      "- root: %2\n"
      "- scanned: %3\n"
      "- converted: %4\n"
      "- skipped_binary: %5\n"
      "- skipped_large: %6\n"
      "- non_ascii: %7\n"
      "- unknown: %8\n\n"
      "Details:\n%9")
      .arg(mode)
      .arg(QDir::cleanPath(scanRoot))
      .arg(scanned)
      .arg(converted)
      .arg(skippedBinary)
      .arg(skippedLarge)
      .arg(nonAsciiCount)
      .arg(unknownCount)
      .arg(details.join("\n"));

   ToolResult result;
   result.success = true;
   result.content = summary;
   result.userDisplayMessage = QStringLiteral("Encoding %1 complete: scanned %2, converted %3")
                                  .arg(mode)
                                  .arg(scanned)
                                  .arg(converted);
   return result;
}

} // namespace AiChat
