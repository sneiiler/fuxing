// -----------------------------------------------------------------------------
// File: AiChatSkillManager.hpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 2.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#ifndef AICHAT_SKILL_MANAGER_HPP
#define AICHAT_SKILL_MANAGER_HPP

#include <QMap>
#include <QSet>
#include <QString>
#include <QStringList>

namespace AiChat
{

/// Manages discovery, lazy-loading, override-resolution and toggle state of
/// skills.  Design aligned with Cline's skill system:
///   - Project skills:  <project>/.aichat/skills/*/SKILL.md
///   - Global skills:   ~/.aichat/skills/*/SKILL.md  (higher priority)
///   - SKILL.md frontmatter must contain `name` and `description`.
///   - `name` must match the containing directory name.
class SkillManager
{
public:
   /// Source of a skill: global (~/.aichat/skills) or project-level.
   enum class SkillSource
   {
      Global,   ///< From user-home global skills directory
      Project   ///< From a project-level .aichat/skills directory
   };

   struct Skill
   {
      QString name;            ///< Canonical skill name (== directory name)
      QString description;     ///< Short description from frontmatter
      QString directory;       ///< Absolute path to the skill directory
      QString skillPath;       ///< Absolute path to SKILL.md
      QString content;         ///< Full body content (loaded lazily)
      QStringList supportFiles;///< Relative paths of extra files in skill dir
      SkillSource source{SkillSource::Project};
      bool contentLoaded{false};///< Whether full content has been loaded
   };

   /// Load skills from global directory, project directory, and additional
   /// project directories.  Global skills take precedence over project skills
   /// with the same name.
   void LoadFromProject(const QString& aProjectDir, const QStringList& aProjectDirs);

   /// Re-discover skills on demand (tool handler calls this for always-fresh
   /// results).  Equivalent to LoadFromProject with the last-known roots.
   void Rediscover();

   /// Build a short catalog summary for system-prompt injection.
   /// Skills that are toggled off are excluded.
   QString BuildCatalogSummary() const;

   /// Return true if a skill exists and is enabled.
   bool HasSkill(const QString& aName) const;

   /// Get a skill by name (case-insensitive).  Returns an empty Skill if
   /// missing.
   Skill GetSkill(const QString& aName) const;

   /// Get the full content of a skill, loading lazily if needed.
   /// Returns empty string if skill doesn't exist.
   QString GetSkillContent(const QString& aName);

   /// Get all enabled skill names (sorted).
   QStringList GetSkillNames() const;

   /// Get all discovered skills (including disabled ones).
   QList<Skill> GetAllSkills() const;

   /// Format a skill for injection into a tool-call response.
   QString FormatSkillForPrompt(const Skill& aSkill) const;

   // ------------ Toggle (Enable / Disable) Support -------------------------

   /// Set the skill toggle state.  Key = SKILL.md absolute path.
   /// A skill is enabled by default unless explicitly set to false.
   void SetSkillToggle(const QString& aSkillPath, bool aEnabled);

   /// Get the toggle state for a skill.  Returns true (enabled) by default.
   bool IsSkillEnabled(const QString& aSkillPath) const;

   /// Get all toggle states (for persistence).
   QMap<QString, bool> GetSkillToggles() const { return mSkillToggles; }

   /// Set all toggle states at once (for restoring from settings).
   void SetSkillToggles(const QMap<QString, bool>& aToggles) { mSkillToggles = aToggles; }

   /// Return the global skills directory path (~/.aichat/skills).
   static QString GetGlobalSkillsDirectory();

   // ------------ Activated-skill lifecycle ---------------------------------
   // Skills marked as "activated" have their full content injected into the
   // system prompt on every subsequent LLM request, so the content survives
   // context truncation.

   /// Mark a skill as activated (called by LoadSkillToolHandler).
   /// If the skill content is not yet loaded, it will be loaded lazily.
   void ActivateSkill(const QString& aName);

   /// Remove a skill from the activated set.
   void DeactivateSkill(const QString& aName);

   /// Clear all activated skills (e.g. on session reset).
   void ClearActivatedSkills();

   /// Return the list of currently activated skill names.
   QStringList GetActivatedSkillNames() const;

   /// Build the combined content block for all activated skills, ready for
   /// injection into the system prompt's SKILLS section.
   /// Returns empty string if nothing is activated.
   QString BuildActivatedSkillsContent();

private:
   static QString NormalizeName(const QString& aName);
   void ScanSkillRoot(const QString& aRootDir, SkillSource aSource);
   Skill ParseSkillDirectory(const QString& aDirPath, SkillSource aSource) const;
   bool ValidateSkillMetadata(const QString& aName, const QString& aDescription,
                              const QString& aDirName, const QString& aDirPath) const;
   void AddSkill(const Skill& aSkill);
   void LoadSkillContent(Skill& aSkill) const;

   QMap<QString, Skill> mSkills;         ///< Normalized name -> skill
   QMap<QString, bool>  mSkillToggles;   ///< Skill path -> enabled
   QSet<QString>        mActivatedSkills; ///< Normalized names of activated skills

   // Cache last-known roots for Rediscover()
   QString     mLastProjectDir;
   QStringList mLastProjectDirs;
};

} // namespace AiChat

#endif // AICHAT_SKILL_MANAGER_HPP
