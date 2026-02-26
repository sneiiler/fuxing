// -----------------------------------------------------------------------------
// File: AiChatEncodingUtils.hpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-12
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// Description:
//   Shared encoding detection and byte-cleaning utilities for the AIChat plugin.
//
//   AFSIM scenario files (.txt, .wsf, .script) are expected to be pure ASCII.
//   However, files in the workspace can appear in a variety of legacy encodings
//   (UTF-8, UTF-8 with BOM, UTF-16LE/BE, Windows-1252 / Latin-1) or may
//   carry trailing null bytes inserted by external editors.
//
//   This module provides a single, deterministic detection pipeline that can be
//   used by both EditorBridge (in-memory TextSource reads) and
//   ReadFileToolHandler (disk reads) so that encoding logic is not duplicated.
//
//   Detection strategy (in order):
//     1. BOM detection  (UTF-8 BOM, UTF-16 LE/BE BOM)
//     2. Zero-byte heuristic for BOM-less UTF-16
//     3. UTF-8 validation (look for replacement chars after decoding)
//     4. Windows-1252 / Latin-1 fallback
// -----------------------------------------------------------------------------

#ifndef AICHAT_ENCODING_UTILS_HPP
#define AICHAT_ENCODING_UTILS_HPP

#include <QByteArray>
#include <QString>

namespace AiChat
{
namespace EncodingUtils
{

/// Remove trailing null bytes (0x00) from raw file data.
/// Files produced by some editors may have garbage null bytes appended; these
/// cause the binary-detection heuristic to reject the file.
void StripTrailingNulls(QByteArray& aData);

/// Remove trailing null QChars (U+0000) from a decoded string.
void StripTrailingNullChars(QString& aText);

/// Detect the encoding of raw bytes.
/// Returns a Qt codec name such as "UTF-8", "UTF-16LE", "UTF-16BE",
/// "Windows-1252", or an empty string when detection is inconclusive (the
/// caller should default to UTF-8).
///
/// The function does NOT decode the data; use DecodeBytes() for a one-step
/// detect-and-decode workflow.
QString DetectEncoding(const QByteArray& aData);

/// Detect encoding and decode raw bytes to a QString in one step.
/// Applies StripTrailingNulls internally.  On success the returned string is
/// never null (but may be empty for an empty file).  On failure it returns a
/// null QString.
///
/// @param aData      Raw file bytes (will NOT be modified).
/// @param aCodecUsed [out, optional] Receives the codec name that was used.
QString DecodeBytes(const QByteArray& aData, QString* aCodecUsed = nullptr);

/// Return true if the UTF-8-decoded string contains U+FFFD replacement
/// characters, which indicates that some source bytes were not valid UTF-8.
bool HasReplacementChars(const QString& aText, int aSampleSize = 0);

/// Quick check: are all bytes valid UTF-8 sequences?
/// (Does not strip trailing nulls – pass clean data.)
bool IsValidUtf8(const QByteArray& aData);

/// Return true if the file extension requires ASCII-only content.
/// AFSIM scenario files (.txt, .wsf, .script) are parsed by a C++ engine
/// that does not handle multi-byte encodings.
bool RequiresAsciiOnly(const QString& aFilePath);

/// Scan text for the first non-ASCII character (Unicode > U+007F).
/// Returns true if one is found, with the offending character and its index.
bool FindFirstNonAscii(const QString& aText, QChar& aOutChar, int& aOutIndex);

/// Build a human-readable error string for a non-ASCII violation.
/// Example: "Non-ASCII character U+00B0 (°) at position 42. ..."
QString FormatNonAsciiError(QChar aChar, int aIndex);

} // namespace EncodingUtils
} // namespace AiChat

#endif // AICHAT_ENCODING_UTILS_HPP
