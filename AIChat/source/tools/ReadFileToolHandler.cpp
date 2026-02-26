// -----------------------------------------------------------------------------
// File: ReadFileToolHandler.cpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#include "ReadFileToolHandler.hpp"
#include "../bridge/AiChatEditorBridge.hpp"
#include "../core/AiChatPathAccessManager.hpp"
#include "../AiChatEncodingUtils.hpp"

#include <QDebug>
#include <QFile>
#include <QFileInfo>
#include <QIODevice>
#include <QStringList>
#include <QTextCodec>
#include <QTextStream>

namespace AiChat
{

namespace
{
constexpr int kMaxOutputChars = 30000;
constexpr int kMaxLineChars   = 500;

QString ReadFileWithCodec(const QString& aPath, const QByteArray& aCodec,
                          int aStartLine, int aEndLine)
{
   QFile file(aPath);
   if (!file.open(QIODevice::ReadOnly)) {
      return QString();
   }

   QTextStream in(&file);
   in.setCodec(aCodec.constData());

   if (aStartLine <= 0 && aEndLine <= 0) {
      return in.readAll();
   }

   QStringList result;
   int lineNum = 0;
   while (!in.atEnd()) {
      ++lineNum;
      const QString line = in.readLine();
      if (aStartLine > 0 && lineNum < aStartLine) continue;
      if (aEndLine   > 0 && lineNum > aEndLine)   break;
      result.append(line);
   }
   return result.join('\n');
}

struct BinaryTextStats
{
   int  sampleSize = 0;
   int  nonPrintable = 0;
   bool hasNull = false;
   double ratio = 0.0;
};

BinaryTextStats GetBinaryTextStats(const QString& aText)
{
   BinaryTextStats stats;
   stats.sampleSize = qMin(aText.size(), 4096);
   if (stats.sampleSize == 0) {
      return stats;
   }

   for (int i = 0; i < stats.sampleSize; ++i) {
      const QChar ch = aText.at(i);
      if (ch.unicode() == 0) {
         stats.hasNull = true;
         break;
      }
      if (ch.isPrint() || ch == QChar(' ') || ch == QChar('\t') ||
          ch == QChar('\n') || ch == QChar('\r')) {
         continue;
      }
      ++stats.nonPrintable;
   }

   stats.ratio = static_cast<double>(stats.nonPrintable) / stats.sampleSize;
   return stats;
}

double AsciiPrintableRatio(const QString& aText)
{
   const int sampleSize = qMin(aText.size(), 4096);
   if (sampleSize <= 0) {
      return 0.0;
   }

   int printable = 0;
   for (int i = 0; i < sampleSize; ++i) {
      const QChar ch = aText.at(i);
      if (ch == QChar('\n') || ch == QChar('\r') || ch == QChar('\t')) {
         ++printable;
         continue;
      }
      // Count all Unicode printable characters (CJK, Latin, etc.)
      if (ch.isPrint()) {
         ++printable;
      }
   }

   return static_cast<double>(printable) / sampleSize;
}

/// Ratio of characters that are strictly ASCII (0x09, 0x0A, 0x0D, 0x20-0x7E).
/// Useful for detecting when an ASCII text file was misread as a different
/// encoding (e.g., UTF-16LE → CJK garbled text).  In that scenario,
/// AsciiPrintableRatio() would still return ~1.0 because CJK chars are
/// "printable", but PureAsciiRatio() would return ~0.0.
double PureAsciiRatio(const QString& aText)
{
   const int sampleSize = qMin(aText.size(), 4096);
   if (sampleSize <= 0) {
      return 0.0;
   }

   int ascii = 0;
   for (int i = 0; i < sampleSize; ++i) {
      const ushort code = aText.at(i).unicode();
      if (code == 0x09 || code == 0x0A || code == 0x0D ||
          (code >= 0x20 && code <= 0x7E)) {
         ++ascii;
      }
   }

   return static_cast<double>(ascii) / sampleSize;
}

QString DecodeBytesWithCodec(const QByteArray& aData, const QByteArray& aCodec)
{
   QTextCodec* codec = QTextCodec::codecForName(aCodec);
   if (!codec) {
      return QString();
   }
   return codec->toUnicode(aData);
}

QString SliceByLines(const QString& aText, int aStartLine, int aEndLine)
{
   if (aStartLine <= 0 && aEndLine <= 0) {
      return aText;
   }

   const QStringList lines = aText.split('\n');
   const int start = qMax(0, aStartLine - 1);
   const int end   = (aEndLine <= 0) ? lines.size() : qMin(aEndLine, lines.size());

   QStringList slice;
   slice.reserve(qMax(0, end - start));
   for (int i = start; i < end; ++i) {
      slice.append(lines[i]);
   }
   return slice.join('\n');
}

QString ReadFileAutoCodec(const QString& aPath, int aStartLine, int aEndLine, QString* aCodecUsed)
{
   if (aCodecUsed) {
      aCodecUsed->clear();
   }

   QFile file(aPath);
   if (!file.open(QIODevice::ReadOnly)) {
      return QString();
   }

   const QByteArray data = file.readAll();
   if (data.isEmpty()) {
      return QString();
   }

   // --- Strip trailing null bytes (common artifact from some editors) ---
   QByteArray cleanData = data;
   EncodingUtils::StripTrailingNulls(cleanData);
   if (cleanData.isEmpty()) {
      return QString();
   }

   QByteArray codecName;
   if (cleanData.size() >= 2) {
      const unsigned char b0 = static_cast<unsigned char>(cleanData.at(0));
      const unsigned char b1 = static_cast<unsigned char>(cleanData.at(1));
      if (b0 == 0xFF && b1 == 0xFE) {
         codecName = "UTF-16LE";
      } else if (b0 == 0xFE && b1 == 0xFF) {
         codecName = "UTF-16BE";
      }
   }
   if (codecName.isEmpty() && cleanData.size() >= 3) {
      const unsigned char b0 = static_cast<unsigned char>(cleanData.at(0));
      const unsigned char b1 = static_cast<unsigned char>(cleanData.at(1));
      const unsigned char b2 = static_cast<unsigned char>(cleanData.at(2));
      if (b0 == 0xEF && b1 == 0xBB && b2 == 0xBF) {
         codecName = "UTF-8";
      }
   }

   if (codecName.isEmpty()) {
      int zeroEven = 0;
      int zeroOdd = 0;
      const int sample = qMin(cleanData.size(), 4096);
      for (int i = 0; i < sample; ++i) {
         if (cleanData.at(i) == 0) {
            if ((i % 2) == 0) {
               ++zeroEven;
            } else {
               ++zeroOdd;
            }
         }
      }

      if (zeroOdd > sample / 6 && zeroEven < sample / 30) {
         codecName = "UTF-16LE";
      } else if (zeroEven > sample / 6 && zeroOdd < sample / 30) {
         codecName = "UTF-16BE";
      }
   }

   // --- CP1252 / Latin-1 fallback ---
   // If no BOM or UTF-16 pattern detected, check whether the bytes are valid
   // UTF-8.  If they are NOT (e.g. bare 0xB0 for °, 0xB2 for ², 0xD7 for ×),
   // treat the file as Windows-1252.
   if (codecName.isEmpty()) {
      if (!EncodingUtils::IsValidUtf8(cleanData)) {
         bool hasHighBytes = false;
         const int scanLen = qMin(cleanData.size(), 8192);
         for (int i = 0; i < scanLen; ++i) {
            if (static_cast<unsigned char>(cleanData.at(i)) >= 0x80) {
               hasHighBytes = true;
               break;
            }
         }
         if (hasHighBytes) {
            codecName = "Windows-1252";
         }
      }
   }

   if (codecName.isEmpty()) {
      return QString();
   }

   const QString decoded = DecodeBytesWithCodec(cleanData, codecName);
   if (decoded.isEmpty()) {
      return QString();
   }

   if (aCodecUsed) {
      *aCodecUsed = QString::fromLatin1(codecName);
   }

   return SliceByLines(decoded, aStartLine, aEndLine);
}

bool IsProbablyBinaryText(const QString& aText, BinaryTextStats* aStats = nullptr)
{
   BinaryTextStats stats = GetBinaryTextStats(aText);
   if (aStats) {
      *aStats = stats;
   }
   if (stats.sampleSize == 0) {
      return false;
   }
   if (stats.hasNull) {
      return true;
   }
   return stats.ratio > 0.30;
}

QString SanitizeLine(const QString& aLine)
{
   QString cleaned;
   cleaned.reserve(qMin(aLine.size(), kMaxLineChars));
   for (const QChar ch : aLine) {
      if (ch == QChar('\t')) {
         cleaned += QChar(' ');
      } else if (ch.isPrint() || ch == QChar(' ')) {
         cleaned += ch;
      }
      if (cleaned.size() >= kMaxLineChars) {
         break;
      }
   }
   return cleaned;
}
} // namespace

ReadFileToolHandler::ReadFileToolHandler(PathAccessManager* aPathAccess)
   : mPathAccess(aPathAccess)
{
}

ToolDefinition ReadFileToolHandler::GetDefinition() const
{
   ToolDefinition def;
   def.name        = QStringLiteral("read_file");
   def.description = QStringLiteral(
      "Read the contents of a file at the given path. "
      "Use this when you need to examine the contents of an existing file. "
      "Optionally specify start_line and end_line to read a specific range. "
      "Output will be returned with line numbers prefixed.");
   def.parameters = {
      {"path", "string",
         "The path of the file to read (relative to the workspace root, or absolute). "
         "Use @workspace:path to target a specific root.", true},
      {"start_line", "integer",
       "The 1-based line number to start reading from (inclusive). Omit to read from the beginning.",
       false},
      {"end_line", "integer",
       "The 1-based line number to stop reading at (inclusive). Omit to read to the end.", false}};
   return def;
}

bool ReadFileToolHandler::ValidateParams(const QJsonObject& aParams, QString& aError) const
{
   if (!aParams.contains("path") || aParams["path"].toString().trimmed().isEmpty()) {
      aError = QStringLiteral("Missing required parameter: path");
      return false;
   }
   return true;
}

ToolResult ReadFileToolHandler::Execute(const QJsonObject& aParams)
{
   const QString filePath  = aParams["path"].toString().trimmed();

   // Some LLMs send start_line/end_line as string floats (e.g., "37.0")
   // instead of JSON integers.  QVariant::toInt() FAILS on "37.0" (returns 0)
   // because it's not a valid integer string.  Use toDouble() + qRound() which
   // correctly handles all variants: int 37, double 37.0, string "37", string "37.0".
   const int     startLine = qRound(aParams.value("start_line").toVariant().toDouble());
   const int     endLine   = qRound(aParams.value("end_line").toVariant().toDouble());

   QString resolvedPath;
   if (mPathAccess) {
      const auto res = mPathAccess->ResolveForRead(filePath);
      if (!res.ok) {
         return {false,
                 QStringLiteral("Error: %1").arg(res.error),
                 {}, false};
      }
      resolvedPath = res.resolvedPath;
   }

   // Resolve the path (read operations are allowed outside the project directory)
   const QString resolved = resolvedPath.isEmpty()
      ? EditorBridge::ResolveReadPath(filePath)
      : resolvedPath;
   if (resolved.isEmpty()) {
      return {false,
              QStringLiteral("Error: Could not resolve path '%1'.")
                 .arg(filePath),
              {}, false};
   }

   if (!QFileInfo::exists(resolved)) {
      return {false,
              QStringLiteral("Error: File '%1' does not exist.")
                 .arg(filePath),
              {}, false};
   }

   // --- BOM-based encoding auto-detection (handles UTF-16LE/BE before EditorBridge) ---
   QString content;
   {
      QString codecUsed;
      const QString autoContent = ReadFileAutoCodec(resolved, startLine, endLine, &codecUsed);
      if (!autoContent.isEmpty() && AsciiPrintableRatio(autoContent) > 0.50) {
         content = autoContent;
         qWarning() << "[AIChat::ReadFile] Auto-detected encoding."
                    << "path=" << filePath << "codec=" << codecUsed;
      }
   }

   // Fall back to EditorBridge (supports in-memory editor buffers & UTF-8 disk read)
   if (content.isNull()) {
      content = EditorBridge::ReadFile(resolved, startLine, endLine);

      // Defensive check: If the editor opened the file with the wrong encoding
      // (e.g., a plain ASCII file loaded as UTF-16LE), the in-memory buffer will
      // contain garbled CJK characters.  AsciiPrintableRatio() won't catch this
      // because CJK chars pass isPrint().  PureAsciiRatio() detects it by
      // counting only true ASCII code points.
      if (!content.isNull() && PureAsciiRatio(content) < 0.50) {
         qWarning() << "[AIChat::ReadFile] EditorBridge returned suspicious content"
                    << "(pureAsciiRatio=" << PureAsciiRatio(content) << "),"
                    << "trying direct disk read. path=" << filePath;

         QFile diskFile(resolved);
         if (diskFile.open(QIODevice::ReadOnly)) {
            QByteArray diskRaw = diskFile.readAll();
            EncodingUtils::StripTrailingNulls(diskRaw);
            QString diskContent = EncodingUtils::DecodeBytes(diskRaw);

            if (!diskContent.isNull()) {
               diskContent = SliceByLines(diskContent, startLine, endLine);
               // Use disk content only if it's actually better
               if (!diskContent.isEmpty() && PureAsciiRatio(diskContent) > PureAsciiRatio(content) + 0.20) {
                  qWarning() << "[AIChat::ReadFile] Disk read succeeded with higher ASCII ratio,"
                             << "using disk content. path=" << filePath;
                  content = diskContent;
               }
            }
         }
      }
   }

   // If EditorBridge failed (e.g., path outside workspace sandbox) but
   // PathAccessManager already authorized the path, try direct disk read.
   if (content.isNull() && !resolvedPath.isEmpty()) {
      QFile directFile(resolved);
      if (directFile.open(QIODevice::ReadOnly)) {
         QByteArray directRaw = directFile.readAll();
         EncodingUtils::StripTrailingNulls(directRaw);
         content = EncodingUtils::DecodeBytes(directRaw);

         content = SliceByLines(content, startLine, endLine);
         qWarning() << "[AIChat::ReadFile] EditorBridge rejected, direct read succeeded."
                    << "path=" << filePath;
      }
   }

   if (content.isNull()) {
      return {false,
              QStringLiteral("Error: Could not read file '%1'. The file may not exist or is "
                             "not accessible.")
                 .arg(filePath),
              {}, false};
   }

   // Strip trailing null characters that may survive from the editor buffer or
   // disk read — they would cause IsProbablyBinaryText to reject the file.
   EncodingUtils::StripTrailingNullChars(content);

   const double asciiRatio = AsciiPrintableRatio(content);
   if (asciiRatio < 0.65) {
      QString codecUsed;
      const QString decoded = ReadFileAutoCodec(resolved, startLine, endLine, &codecUsed);
      if (!decoded.isEmpty()) {
         const double decodedRatio = AsciiPrintableRatio(decoded);
         if (decodedRatio > asciiRatio + 0.15) {
            qWarning() << "[AIChat::ReadFile] Re-decoded with codec." << "path=" << filePath
                       << "codec=" << codecUsed;
            content = decoded;
         }
      }
   }

   BinaryTextStats binStats;
   if (IsProbablyBinaryText(content, &binStats)) {
      QString utf16Content;
      if (!resolved.isEmpty()) {
         utf16Content = ReadFileWithCodec(resolved, QByteArray("UTF-16"), startLine, endLine);
      }

      if (!utf16Content.isEmpty() && !IsProbablyBinaryText(utf16Content)) {
         qWarning() << "[AIChat::ReadFile] Binary detected, decoded as UTF-16."
                    << "path=" << filePath;
         content = utf16Content;
      } else {
         qWarning() << "[AIChat::ReadFile] Binary check failed."
                  << "path=" << filePath
                  << "size=" << content.size()
                  << "sample=" << binStats.sampleSize
                  << "nonPrintable=" << binStats.nonPrintable
                  << "ratio=" << binStats.ratio
                  << "hasNull=" << binStats.hasNull;
         return {false,
                 QStringLiteral("Error: File '%1' appears to be binary and cannot be displayed.")
                    .arg(filePath),
                 {}, false};
      }
   }

   // Add line numbers to the output for LLM context
   QStringList lines = content.split('\n');
   QString     numbered;
   int         lineOffset = (startLine > 0) ? startLine : 1;

   numbered.reserve(content.size() + lines.size() * 8);
   for (int i = 0; i < lines.size(); ++i) {
      numbered += QString::number(lineOffset + i);
      numbered += QStringLiteral(" | ");
      numbered += SanitizeLine(lines[i]);
      if (i < lines.size() - 1) {
         numbered += '\n';
      }
      if (numbered.size() >= kMaxOutputChars) {
         numbered += QStringLiteral("\n[Truncated output to %1 chars]\n").arg(kMaxOutputChars);
         break;
      }
   }

   // Build a display message
   QString rangeInfo;
   if (startLine > 0 || endLine > 0) {
      rangeInfo = QStringLiteral(" (lines %1-%2)")
                     .arg(startLine > 0 ? startLine : 1)
                     .arg(endLine > 0 ? endLine : lineOffset + lines.size() - 1);
   }

   return {true, numbered,
           QStringLiteral("Read file: %1%2").arg(filePath, rangeInfo), false};
}

QString ReadFileToolHandler::GenerateDiff(const QJsonObject& aParams) const
{
   const QString path = aParams.value(QStringLiteral("path")).toString().trimmed();
   const int startLine = qRound(aParams.value(QStringLiteral("start_line")).toVariant().toDouble());
   const int endLine = qRound(aParams.value(QStringLiteral("end_line")).toVariant().toDouble());

   QStringList lines;
   lines << QStringLiteral("Path: %1").arg(path);
   if (startLine > 0 || endLine > 0) {
      const QString endText = endLine > 0 ? QString::number(endLine) : QStringLiteral("end");
      lines << QStringLiteral("Lines: %1-%2")
              .arg(startLine > 0 ? startLine : 1)
              .arg(endText);
   }
   return lines.join('\n');
}

} // namespace AiChat
