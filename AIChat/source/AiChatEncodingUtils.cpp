// -----------------------------------------------------------------------------
// File: AiChatEncodingUtils.cpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-12
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#include "AiChatEncodingUtils.hpp"

#include <QFileInfo>
#include <QTextCodec>
#include <algorithm>  // std::min

namespace AiChat
{
namespace EncodingUtils
{

// ---------------------------------------------------------------------------
// StripTrailingNulls
// ---------------------------------------------------------------------------

void StripTrailingNulls(QByteArray& aData)
{
   int end = aData.size();
   while (end > 0 && aData.at(end - 1) == '\0') {
      --end;
   }
   if (end < aData.size()) {
      aData.truncate(end);
   }
}

// ---------------------------------------------------------------------------
// StripTrailingNullChars
// ---------------------------------------------------------------------------

void StripTrailingNullChars(QString& aText)
{
   while (!aText.isEmpty() && aText.at(aText.size() - 1).unicode() == 0) {
      aText.chop(1);
   }
}

// ---------------------------------------------------------------------------
// IsValidUtf8
// ---------------------------------------------------------------------------

bool IsValidUtf8(const QByteArray& aData)
{
   const auto* bytes = reinterpret_cast<const unsigned char*>(aData.constData());
   const int   len   = aData.size();

   for (int i = 0; i < len; /* advanced inside */) {
      const unsigned char b = bytes[i];

      // ASCII range (including control chars like \n, \r, \t)
      if (b < 0x80) {
         ++i;
         continue;
      }

      // Determine expected sequence length from lead byte
      int seqLen = 0;
      if ((b & 0xE0) == 0xC0)      seqLen = 2;   // 110xxxxx
      else if ((b & 0xF0) == 0xE0) seqLen = 3;   // 1110xxxx
      else if ((b & 0xF8) == 0xF0) seqLen = 4;   // 11110xxx
      else return false;  // Invalid lead byte (0x80-0xBF or 0xF8+)

      if (i + seqLen > len) return false;  // Truncated sequence

      // Validate continuation bytes (must be 10xxxxxx)
      for (int j = 1; j < seqLen; ++j) {
         if ((bytes[i + j] & 0xC0) != 0x80) {
            return false;
         }
      }

      // Reject overlong encodings
      if (seqLen == 2 && b < 0xC2) return false;                  // Overlong 2-byte
      if (seqLen == 3 && b == 0xE0 && bytes[i + 1] < 0xA0) return false;  // Overlong 3-byte
      if (seqLen == 4 && b == 0xF0 && bytes[i + 1] < 0x90) return false;  // Overlong 4-byte

      i += seqLen;
   }

   return true;
}

// ---------------------------------------------------------------------------
// HasReplacementChars
// ---------------------------------------------------------------------------

bool HasReplacementChars(const QString& aText, int aSampleSize)
{
   const int limit = (aSampleSize > 0)
                        ? std::min(aSampleSize, aText.size())
                        : aText.size();
   for (int i = 0; i < limit; ++i) {
      if (aText.at(i).unicode() == 0xFFFD) {
         return true;
      }
   }
   return false;
}

// ---------------------------------------------------------------------------
// DetectEncoding
// ---------------------------------------------------------------------------

QString DetectEncoding(const QByteArray& aData)
{
   if (aData.size() < 2) {
      return QString();  // Too small to detect
   }

   const auto b0 = static_cast<unsigned char>(aData.at(0));
   const auto b1 = static_cast<unsigned char>(aData.at(1));

   // --- 1. BOM detection ---
   if (b0 == 0xFF && b1 == 0xFE) {
      return QStringLiteral("UTF-16LE");
   }
   if (b0 == 0xFE && b1 == 0xFF) {
      return QStringLiteral("UTF-16BE");
   }
   if (aData.size() >= 3) {
      const auto b2 = static_cast<unsigned char>(aData.at(2));
      if (b0 == 0xEF && b1 == 0xBB && b2 == 0xBF) {
         return QStringLiteral("UTF-8");
      }
   }

   // --- 2. Zero-byte heuristic for BOM-less UTF-16 ---
   {
      int zeroEven = 0;
      int zeroOdd  = 0;
      const int sample = std::min(aData.size(), 4096);
      for (int i = 0; i < sample; ++i) {
         if (aData.at(i) == 0) {
            if ((i % 2) == 0) ++zeroEven;
            else              ++zeroOdd;
         }
      }
      if (zeroOdd > sample / 6 && zeroEven < sample / 30) {
         return QStringLiteral("UTF-16LE");
      }
      if (zeroEven > sample / 6 && zeroOdd < sample / 30) {
         return QStringLiteral("UTF-16BE");
      }
   }

   // --- 3. UTF-8 validation ---
   if (IsValidUtf8(aData)) {
      return QStringLiteral("UTF-8");
   }

   // --- 4. Fallback: bytes in 0x80-0xFF that are NOT valid UTF-8 ---
   // This is characteristic of single-byte encodings like Windows-1252 / Latin-1.
   // These files typically have a few special characters (°, ², ×, etc.) embedded
   // in otherwise ASCII text.
   {
      bool hasHighBytes = false;
      const int sample = std::min(aData.size(), 8192);
      for (int i = 0; i < sample; ++i) {
         const auto byte = static_cast<unsigned char>(aData.at(i));
         if (byte >= 0x80) {
            hasHighBytes = true;
            break;
         }
      }
      if (hasHighBytes) {
         return QStringLiteral("Windows-1252");
      }
   }

   return QString();  // Inconclusive → caller defaults to UTF-8
}

// ---------------------------------------------------------------------------
// DecodeBytes
// ---------------------------------------------------------------------------

QString DecodeBytes(const QByteArray& aData, QString* aCodecUsed)
{
   if (aCodecUsed) {
      aCodecUsed->clear();
   }

   if (aData.isEmpty()) {
      return QString(QLatin1String(""));  // Empty but non-null
   }

   // Work on a copy so we can strip trailing nulls without mutating the caller's data
   QByteArray clean = aData;
   StripTrailingNulls(clean);

   if (clean.isEmpty()) {
      return QString(QLatin1String(""));
   }

   // Detect encoding
   const QString encoding = DetectEncoding(clean);
   const QString codecName = encoding.isEmpty() ? QStringLiteral("UTF-8") : encoding;

   // Decode
   QTextCodec* codec = QTextCodec::codecForName(codecName.toLatin1());
   if (!codec) {
      // Last resort: UTF-8
      codec = QTextCodec::codecForName("UTF-8");
   }

   QString result;
   if (codec) {
      result = codec->toUnicode(clean);
   } else {
      result = QString::fromUtf8(clean);
   }

   // Strip any remaining trailing null chars in the decoded string
   StripTrailingNullChars(result);

   if (aCodecUsed) {
      *aCodecUsed = codecName;
   }

   return result;
}

// ---------------------------------------------------------------------------
// RequiresAsciiOnly
// ---------------------------------------------------------------------------

bool RequiresAsciiOnly(const QString& aFilePath)
{
   const QString ext = QFileInfo(aFilePath).suffix().toLower();
   return (ext == QStringLiteral("txt")
        || ext == QStringLiteral("wsf")
        || ext == QStringLiteral("script"));
}

// ---------------------------------------------------------------------------
// FindFirstNonAscii
// ---------------------------------------------------------------------------

bool FindFirstNonAscii(const QString& aText, QChar& aOutChar, int& aOutIndex)
{
   for (int i = 0; i < aText.size(); ++i) {
      const QChar ch = aText.at(i);
      if (ch.unicode() > 0x7F) {
         aOutChar  = ch;
         aOutIndex = i;
         return true;
      }
   }
   return false;
}

// ---------------------------------------------------------------------------
// FormatNonAsciiError
// ---------------------------------------------------------------------------

QString FormatNonAsciiError(QChar aChar, int aIndex)
{
   // Map well-known offenders to their ASCII replacements
   struct Replacement { ushort code; const char* name; const char* suggestion; };
   static const Replacement kKnown[] = {
      {0x00B0, "degree sign",        "Use 'deg' instead"},
      {0x00B1, "plus-minus sign",    "Use '+/-' instead"},
      {0x00B2, "superscript two",    "Use '^2' instead"},
      {0x00B3, "superscript three",  "Use '^3' instead"},
      {0x00B5, "micro sign",         "Use 'u' instead"},
      {0x00D7, "multiplication sign","Use '*' instead"},
      {0x00F7, "division sign",      "Use '/' instead"},
      {0x2013, "en-dash",            "Use '-' instead"},
      {0x2014, "em-dash",            "Use '--' instead"},
      {0x2018, "left single quote",  "Use ' instead"},
      {0x2019, "right single quote", "Use ' instead"},
      {0x201C, "left double quote",  "Use \" instead"},
      {0x201D, "right double quote", "Use \" instead"},
      {0x2022, "bullet",             "Use '-' or '*' instead"},
      {0x2026, "ellipsis",           "Use '...' instead"},
      {0x00A9, "copyright sign",     "Use '(c)' instead"},
      {0x00AE, "registered sign",    "Use '(R)' instead"},
   };

   const ushort code = aChar.unicode();
   QString hint;
   for (const auto& r : kKnown) {
      if (r.code == code) {
         hint = QStringLiteral(" (%1). %2.").arg(QLatin1String(r.name), QLatin1String(r.suggestion));
         break;
      }
   }

   return QStringLiteral(
      "Non-ASCII character U+%1 at position %2%3 "
      "Only ASCII (0x20-0x7E) is allowed in .txt/.wsf/.script files. "
      "ACTION REQUIRED: Rewrite the file content using English-only comments and "
      "identifiers. Replace ALL Chinese/Unicode characters with ASCII equivalents. "
      "Do NOT simply retry with the same content.")
      .arg(QString::number(code, 16).rightJustified(4, QLatin1Char('0')).toUpper())
      .arg(aIndex)
      .arg(hint);
}

} // namespace EncodingUtils
} // namespace AiChat
