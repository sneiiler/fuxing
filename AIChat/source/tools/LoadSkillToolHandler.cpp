// -----------------------------------------------------------------------------
// File: LoadSkillToolHandler.cpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.1.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#include "LoadSkillToolHandler.hpp"
#include "../core/AiChatSkillManager.hpp"

namespace AiChat
{

LoadSkillToolHandler::LoadSkillToolHandler(SkillManager* aSkillManager)
   : mSkillManagerPtr(aSkillManager)
{
}

ToolDefinition LoadSkillToolHandler::GetDefinition() const
{
   ToolDefinition def;
   def.name = QStringLiteral("load_skill");
   def.description = QStringLiteral(
      "Load and activate a skill by name from the .aichat/skills/ directory.\n"
      "The system prompt lists all available skills with names and descriptions.\n"
      "Read the catalog, pick the skill(s) relevant to the task, and call this tool.\n"
      "The returned content is the authoritative reference -- follow it directly.\n"
      "Avoid redundant calls within the same turn for a skill that was just loaded.");
   def.parameters = {
      {QStringLiteral("skill_name"), QStringLiteral("string"),
       QStringLiteral("The name of the skill to activate (must match exactly one of the "
                      "available skill names)"), true}
   };
   return def;
}

bool LoadSkillToolHandler::ValidateParams(const QJsonObject& aParams, QString& aError) const
{
   if (!aParams.contains(QStringLiteral("skill_name"))
       || !aParams.value(QStringLiteral("skill_name")).isString())
   {
      aError = QStringLiteral("Missing required parameter 'skill_name'. "
                              "Please provide the name of the skill to activate.");
      return false;
   }

   if (!mSkillManagerPtr)
   {
      aError = QStringLiteral("Skill manager not available.");
      return false;
   }

   return true;
}

ToolResult LoadSkillToolHandler::Execute(const QJsonObject& aParams)
{
   ToolResult result;
   result.success = false;

   if (!mSkillManagerPtr)
   {
      result.content = QStringLiteral("Skill manager not available.");
      return result;
   }

   const QString name = aParams.value(QStringLiteral("skill_name")).toString();
   if (name.isEmpty())
   {
      result.content = QStringLiteral("Error: Missing required parameter 'skill_name'. "
                                      "Please provide the name of the skill to activate.");
      return result;
   }

   // Check if skill exists (and is enabled)
   if (!mSkillManagerPtr->HasSkill(name))
   {
      const QStringList available = mSkillManagerPtr->GetSkillNames();
      if (available.isEmpty()) {
         result.content = QStringLiteral("Error: No skills are available. Skills may be "
                                         "disabled or not configured.");
      } else {
         result.content = QStringLiteral("Error: Skill \"%1\" not found. Available skills: %2")
                           .arg(name, available.join(QStringLiteral(", ")));
      }
      return result;
   }

   auto skill = mSkillManagerPtr->GetSkill(name);

   // Mark the skill as activated — its full content will be injected into the
   // system prompt on every subsequent LLM request (survives truncation).
   mSkillManagerPtr->ActivateSkill(name);

   // Return a short confirmation instead of the full content.  The system
   // prompt already carries the SKILL.md body, so duplicating it here
   // would waste context budget.  List support files so the LLM knows
   // what reference material is available via read_file.
   result.success = true;

   QStringList lines;
   lines << QStringLiteral("Skill \"%1\" is now active. Its SKILL.md instructions have been "
                           "injected into the system prompt and will persist for this session.")
             .arg(skill.name);
   lines << QStringLiteral("Source: %1")
             .arg(skill.source == SkillManager::SkillSource::Global
                     ? QStringLiteral("global") : QStringLiteral("project"));
   lines << QStringLiteral("Skill directory: %1").arg(skill.directory);

   if (!skill.supportFiles.isEmpty()) {
      lines << QString();
      lines << QStringLiteral("Support/reference files in skill directory:");
      for (const auto& file : skill.supportFiles) {
         lines << QStringLiteral("- %1/%2").arg(skill.directory, file);
      }
      lines << QStringLiteral("Use read_file to open these files when needed.");
   }

   result.content = lines.join('\n');
   result.userDisplayMessage = QStringLiteral("Loaded skill: %1").arg(skill.name);
   return result;
}

} // namespace AiChat
