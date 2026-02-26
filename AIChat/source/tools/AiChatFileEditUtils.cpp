// -----------------------------------------------------------------------------
// File: AiChatFileEditUtils.cpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#include "AiChatFileEditUtils.hpp"

#include <QRegularExpression>

namespace AiChat
{

QList<SearchReplaceBlock> ParseSearchReplaceBlocks(const QString& aDiff, QString& aError)
{
   QList<SearchReplaceBlock> blocks;

   // Accept 5-7 '<' and '>' characters to tolerate minor LLM formatting variations
   QRegularExpression re(
      QStringLiteral("<{5,7} SEARCH\\n(.*?)\\n=======\\n(.*?)\\n>{5,7} REPLACE"),
      QRegularExpression::DotMatchesEverythingOption);

   auto it = re.globalMatch(aDiff);
   while (it.hasNext())
   {
      auto match = it.next();
      SearchReplaceBlock block;
      block.search = match.captured(1);
      block.replace = match.captured(2);
      blocks.append(block);
   }

   if (blocks.isEmpty())
   {
      aError = QStringLiteral(
         "Invalid diff format. Expected SEARCH/REPLACE blocks:\n"
         "<<<<<<< SEARCH\n...\n=======\n...\n>>>>>>> REPLACE");
   }

   return blocks;
}

bool ApplySearchReplaceBlocks(const QString& aContent,
                              const QList<SearchReplaceBlock>& aBlocks,
                              QString& aOutContent,
                              QString& aError,
                              int& aAppliedCount)
{
   if (aBlocks.isEmpty())
   {
      aError = QStringLiteral("No SEARCH/REPLACE blocks provided.");
      return false;
   }

   QString modifiedContent = aContent;
   int replacementsApplied = 0;
   aAppliedCount = 0;

   for (const auto& block : aBlocks)
   {
      const QString& searchText = block.search;
      const QString& replaceText = block.replace;

      int idx = modifiedContent.indexOf(searchText);
      if (idx >= 0)
      {
         modifiedContent.replace(idx, searchText.length(), replaceText);
         ++replacementsApplied;
         continue;
      }

      // Fuzzy match: compare trimmed lines for slight indentation differences.
      QStringList searchLines = searchText.split('\n');
      QStringList contentLines = modifiedContent.split('\n');

      bool found = false;
      for (int i = 0; i <= contentLines.size() - searchLines.size(); ++i)
      {
         bool matches = true;
         for (int j = 0; j < searchLines.size(); ++j)
         {
            if (contentLines[i + j].trimmed() != searchLines[j].trimmed())
            {
               matches = false;
               break;
            }
         }
         if (matches)
         {
            QStringList resultLines;
            resultLines.append(contentLines.mid(0, i));
            resultLines.append(replaceText.split('\n'));
            resultLines.append(contentLines.mid(i + searchLines.size()));
            modifiedContent = resultLines.join('\n');
            ++replacementsApplied;
            found = true;
            break;
         }
      }

      if (!found)
      {
         aError = QStringLiteral("Could not find SEARCH text in the file. Make sure it matches exactly.");
         return false;
      }
   }

   if (replacementsApplied == 0)
   {
      aError = QStringLiteral("No replacements were applied.");
      return false;
   }

   aAppliedCount = replacementsApplied;
   aOutContent = modifiedContent;
   return true;
}

} // namespace AiChat
