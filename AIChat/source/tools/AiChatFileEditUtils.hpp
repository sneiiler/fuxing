// -----------------------------------------------------------------------------
// File: AiChatFileEditUtils.hpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#ifndef AICHAT_FILE_EDIT_UTILS_HPP
#define AICHAT_FILE_EDIT_UTILS_HPP

#include <QList>
#include <QString>

namespace AiChat
{

struct SearchReplaceBlock
{
   QString search;
   QString replace;
};

QList<SearchReplaceBlock> ParseSearchReplaceBlocks(const QString& aDiff, QString& aError);

bool ApplySearchReplaceBlocks(const QString& aContent,
                              const QList<SearchReplaceBlock>& aBlocks,
                              QString& aOutContent,
                              QString& aError,
                              int& aAppliedCount);

} // namespace AiChat

#endif // AICHAT_FILE_EDIT_UTILS_HPP
