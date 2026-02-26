// -----------------------------------------------------------------------------
// File: AiChatAutoApprovePolicy.cpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#include "AiChatAutoApprovePolicy.hpp"

#include "AiChatPrefObject.hpp"
#include "AiChatPathAccessManager.hpp"

namespace AiChat
{

AutoApprovePolicy::AutoApprovePolicy(PrefObject* aPrefs, PathAccessManager* aPathAccess)
   : mPrefs(aPrefs)
   , mPathAccess(aPathAccess)
{
}

bool AutoApprovePolicy::RequiresApproval(const QString& aToolName, const QJsonObject& aParams) const
{
   if (!mPrefs) {
      return true;
   }

   if (aToolName == QStringLiteral("ask_question") ||
       aToolName == QStringLiteral("attempt_completion")) {
      return false; // Conversational tools never require approval
   }

   if (mPrefs->AutoApply()) {
      return false;
   }

   if (aToolName == QStringLiteral("read_file") ||
       aToolName == QStringLiteral("list_files") ||
       aToolName == QStringLiteral("search_files") ||
       aToolName == QStringLiteral("list_code_definition_names") ||
       aToolName == QStringLiteral("load_skill"))
   {
      const QString path = aParams.value(QStringLiteral("path")).toString();
      if (mPathAccess) {
         const auto res = mPathAccess->ResolveForRead(path);
         if (!res.ok) {
            return true;
         }
         if (res.isExternal) {
            return !mPrefs->GetAutoApproveReadExternal();
         }
      }
      return !mPrefs->GetAutoApproveReadLocal();
   }

   if (aToolName == QStringLiteral("write_to_file") ||
       aToolName == QStringLiteral("replace_in_file") ||
       aToolName == QStringLiteral("delete_file") ||
       aToolName == QStringLiteral("insert_before") ||
       aToolName == QStringLiteral("insert_after") ||
       aToolName == QStringLiteral("set_startup_file") ||
       aToolName == QStringLiteral("normalize_workspace_encoding"))
   {
      const QString path = aParams.value(QStringLiteral("path")).toString();
      if (mPathAccess) {
         const auto res = mPathAccess->ResolveForWrite(path);
         if (!res.ok) {
            return true;
         }
         if (res.isExternal) {
            return true;
         }
      }
      return !mPrefs->GetAutoApproveWriteLocal();
   }

   if (aToolName == QStringLiteral("execute_command") ||
       aToolName == QStringLiteral("run_tests"))
   {
      if (mPrefs->GetAutoApproveCommandAll()) {
         return false;
      }
      if (aToolName == QStringLiteral("run_tests")) {
         return true;
      }
      const QString command = aParams.value(QStringLiteral("command")).toString();
      if (IsCommandSafe(command) && mPrefs->GetAutoApproveCommandSafe()) {
         return false;
      }
      return true;
   }

   return false;
}

bool AutoApprovePolicy::IsCommandSafe(const QString& aCommand) const
{
   const QString cmd = aCommand.trimmed().toLower();
   if (cmd.isEmpty()) {
      return false;
   }

   static const QStringList safePrefixes = {
      QStringLiteral("dir"),
      QStringLiteral("type "),
      QStringLiteral("findstr "),
      QStringLiteral("rg "),
      QStringLiteral("grep "),
      QStringLiteral("where "),
      QStringLiteral("ls "),
      QStringLiteral("cat "),
      QStringLiteral("Get-Content"),
      QStringLiteral("Select-String")
   };

   for (const auto& prefix : safePrefixes) {
      if (cmd.startsWith(prefix.toLower())) {
         return true;
      }
   }

   return false;
}

} // namespace AiChat
