// -----------------------------------------------------------------------------
// File: AiChatSkillManager.cpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 2.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#include "AiChatSkillManager.hpp"

#include <QDebug>
#include <QDir>
#include <QDirIterator>
#include <QFile>
#include <QFileInfo>
#include <QTextStream>

namespace AiChat
{

namespace
{
QStringList ReadFileLines(const QString& aPath)
{
   QFile file(aPath);
   if (!file.open(QIODevice::ReadOnly | QIODevice::Text)) {
      return {};
   }

   QStringList lines;
   QTextStream in(&file);
   in.setCodec("UTF-8");
   while (!in.atEnd()) {
      lines.append(in.readLine());
   }
   return lines;
}

/// Parse YAML-like frontmatter delimited by --- lines.
/// Returns true if a valid frontmatter block was found.
bool ParseFrontMatter(const QStringList& aLines, int& aBodyStartIndex,
                      QMap<QString, QString>& aMeta)
{
   aBodyStartIndex = 0;
   if (aLines.isEmpty() || aLines.first().trimmed() != QStringLiteral("---")) {
      return false;
   }

   for (int i = 1; i < aLines.size(); ++i)
   {
      const QString line = aLines[i].trimmed();
      if (line == QStringLiteral("---")) {
         aBodyStartIndex = i + 1;
         return true;
      }
      const int colonIdx = line.indexOf(':');
      if (colonIdx <= 0) {
         continue;
      }
      const QString key = line.left(colonIdx).trimmed().toLower();
      const QString value = line.mid(colonIdx + 1).trimmed();
      if (!key.isEmpty()) {
         aMeta[key] = value;
      }
   }
   return false; // unclosed frontmatter
}
} // anonymous namespace

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

void SkillManager::LoadFromProject(const QString& aProjectDir,
                                    const QStringList& aProjectDirs)
{
   mLastProjectDir  = aProjectDir;
   mLastProjectDirs = aProjectDirs;
   mSkills.clear();

   // --- Collect unique project root directories ---
   QStringList roots;
   if (!aProjectDir.trimmed().isEmpty()) {
      roots.append(QDir::cleanPath(aProjectDir));
   }
   for (const auto& dir : aProjectDirs) {
      const QString clean = QDir::cleanPath(dir);
      if (!clean.isEmpty() && !roots.contains(clean)) {
         roots.append(clean);
      }
   }

   if (roots.isEmpty()) {
      qDebug() << "[AIChat::SkillManager] No project roots available";
   }

   // --- 1. Scan project-level skill directory (lower priority) ---
   //     Only .aichat/skills is supported.
   for (const auto& root : roots) {
      const QString projSkills = QDir::cleanPath(
         root + QStringLiteral("/.aichat/skills"));
      ScanSkillRoot(projSkills, SkillSource::Project);
   }

   // --- 2. Scan global skills directory (higher priority) ---
   ScanSkillRoot(GetGlobalSkillsDirectory(), SkillSource::Global);

   qDebug() << "[AIChat::SkillManager] Discovered" << mSkills.size()
            << "skill(s):" << GetSkillNames();
}

void SkillManager::Rediscover()
{
   LoadFromProject(mLastProjectDir, mLastProjectDirs);
}

QString SkillManager::BuildCatalogSummary() const
{
   const QStringList names = GetSkillNames();
   if (names.isEmpty()) {
      return {};
   }

   QStringList lines;
   lines << QStringLiteral("Available skills:");

   for (const auto& name : names)
   {
      const Skill skill = mSkills.value(NormalizeName(name));

      QString sourceTag;
      if (skill.source == SkillSource::Global) {
         sourceTag = QStringLiteral(" [global]");
      }

      QString desc = skill.description;
      if (desc.size() > 200) {
         desc = desc.left(200) + QStringLiteral("...");
      }

      lines << QStringLiteral("  - \"%1\": %2%3").arg(skill.name, desc, sourceTag);
   }

   return lines.join('\n');
}

bool SkillManager::HasSkill(const QString& aName) const
{
   const QString key = NormalizeName(aName);
   if (!mSkills.contains(key)) {
      return false;
   }
   return IsSkillEnabled(mSkills.value(key).skillPath);
}

SkillManager::Skill SkillManager::GetSkill(const QString& aName) const
{
   return mSkills.value(NormalizeName(aName));
}

QString SkillManager::GetSkillContent(const QString& aName)
{
   const QString key = NormalizeName(aName);
   if (!mSkills.contains(key)) {
      return {};
   }
   Skill& skill = mSkills[key];
   if (!skill.contentLoaded) {
      LoadSkillContent(skill);
   }
   return skill.content;
}

QStringList SkillManager::GetSkillNames() const
{
   QStringList names;
   for (auto it = mSkills.constBegin(); it != mSkills.constEnd(); ++it) {
      if (IsSkillEnabled(it.value().skillPath)) {
         names.append(it.value().name);
      }
   }
   names.sort(Qt::CaseInsensitive);
   return names;
}

QList<SkillManager::Skill> SkillManager::GetAllSkills() const
{
   QList<Skill> result;
   for (auto it = mSkills.constBegin(); it != mSkills.constEnd(); ++it) {
      result.append(it.value());
   }
   return result;
}

QString SkillManager::FormatSkillForPrompt(const Skill& aSkill) const
{
   if (aSkill.name.isEmpty()) {
      return {};
   }

   QStringList lines;
   lines << QStringLiteral("# Skill \"%1\" is now active\n").arg(aSkill.name);

   if (!aSkill.description.isEmpty()) {
      lines << QStringLiteral("Description: %1").arg(aSkill.description);
   }
   lines << QStringLiteral("Source: %1").arg(
      aSkill.source == SkillSource::Global ? QStringLiteral("global")
                                            : QStringLiteral("project"));
   lines << QString();
   lines << aSkill.content.trimmed();

   if (!aSkill.supportFiles.isEmpty()) {
      lines << QString();
      lines << QStringLiteral("Support files in skill directory:");
      for (const auto& file : aSkill.supportFiles) {
         lines << QStringLiteral("- %1").arg(file);
      }
      lines << QStringLiteral("Use read_file to open support files when needed.");
   }

   lines << QString();
   lines << QStringLiteral("---\n"
      "IMPORTANT: The skill is now loaded and its full content is shown above. "
      "Use this content as your primary reference. "
      "You may access support files in the skill directory at: %1/")
      .arg(aSkill.directory);

   return lines.join('\n');
}

// ---------------------------------------------------------------------------
// Toggle support
// ---------------------------------------------------------------------------

void SkillManager::SetSkillToggle(const QString& aSkillPath, bool aEnabled)
{
   mSkillToggles[aSkillPath] = aEnabled;
}

bool SkillManager::IsSkillEnabled(const QString& aSkillPath) const
{
   if (!mSkillToggles.contains(aSkillPath)) {
      return true;
   }
   return mSkillToggles.value(aSkillPath);
}

// ---------------------------------------------------------------------------
// Static helpers
// ---------------------------------------------------------------------------

QString SkillManager::GetGlobalSkillsDirectory()
{
   const QString globalDir = QDir::cleanPath(
      QDir::homePath() + QStringLiteral("/.aichat/skills"));
   QDir().mkpath(globalDir);
   return globalDir;
}

QString SkillManager::NormalizeName(const QString& aName)
{
   return aName.trimmed().toLower();
}

// ---------------------------------------------------------------------------
// Internal scanning
// ---------------------------------------------------------------------------

void SkillManager::ScanSkillRoot(const QString& aRootDir, SkillSource aSource)
{
   QDir root(aRootDir);
   if (!root.exists()) {
      return;
   }
   qDebug() << "[AIChat::SkillManager] Scanning" << aRootDir
            << (aSource == SkillSource::Global ? "(global)" : "(project)");

   const QFileInfoList entries = root.entryInfoList(QDir::Dirs | QDir::NoDotAndDotDot);
   for (const auto& entry : entries)
   {
      const QString skillDir = entry.absoluteFilePath();
      if (!QFileInfo::exists(QDir(skillDir).filePath(QStringLiteral("SKILL.md")))) {
         continue;
      }
      Skill skill = ParseSkillDirectory(skillDir, aSource);
      if (!skill.name.isEmpty()) {
         AddSkill(skill);
      }
   }
}

SkillManager::Skill SkillManager::ParseSkillDirectory(const QString& aDirPath,
                                                       SkillSource aSource) const
{
   Skill skill;
   skill.directory = aDirPath;
   skill.skillPath = QDir(aDirPath).filePath(QStringLiteral("SKILL.md"));
   skill.source    = aSource;

   const QStringList lines = ReadFileLines(skill.skillPath);
   if (lines.isEmpty()) {
      return {};
   }

   // --- Parse frontmatter ---
   int bodyStartIndex = 0;
   QMap<QString, QString> meta;
   const bool hasFrontMatter = ParseFrontMatter(lines, bodyStartIndex, meta);

   if (!hasFrontMatter) {
      // Cline design: frontmatter is required.
      qDebug() << "[AIChat::SkillManager] Skill at" << aDirPath
               << "has no frontmatter — skipped.";
      return {};
   }

   const QString fmName        = meta.value(QStringLiteral("name")).trimmed();
   const QString fmDescription = meta.value(QStringLiteral("description")).trimmed();
   const QString dirName       = QFileInfo(aDirPath).fileName();

   if (!ValidateSkillMetadata(fmName, fmDescription, dirName, aDirPath)) {
      return {};
   }

   skill.name        = NormalizeName(fmName);
   skill.description = fmDescription;

   // Content is loaded lazily via GetSkillContent().

   // --- Enumerate support files ---
   QDir dir(aDirPath);
   QDirIterator it(aDirPath, QDir::Files | QDir::NoDotAndDotDot,
                   QDirIterator::Subdirectories);
   while (it.hasNext()) {
      it.next();
      if (it.fileName().compare(QStringLiteral("SKILL.md"), Qt::CaseInsensitive) == 0) {
         continue;
      }
      skill.supportFiles.append(dir.relativeFilePath(it.filePath()));
   }
   skill.supportFiles.sort(Qt::CaseInsensitive);

   return skill;
}

bool SkillManager::ValidateSkillMetadata(const QString& aName,
                                          const QString& aDescription,
                                          const QString& aDirName,
                                          const QString& aDirPath) const
{
   if (aName.isEmpty()) {
      qDebug() << "[AIChat::SkillManager] Skill at" << aDirPath
               << "missing required 'name' field — skipped.";
      return false;
   }
   if (aDescription.isEmpty()) {
      qDebug() << "[AIChat::SkillManager] Skill at" << aDirPath
               << "missing required 'description' field — skipped.";
      return false;
   }
   if (aName.compare(aDirName, Qt::CaseInsensitive) != 0) {
      qDebug() << "[AIChat::SkillManager] Skill name" << aName
               << "doesn't match directory" << aDirName << "— skipped.";
      return false;
   }
   return true;
}

void SkillManager::LoadSkillContent(Skill& aSkill) const
{
   if (aSkill.skillPath.isEmpty()) {
      return;
   }
   const QStringList lines = ReadFileLines(aSkill.skillPath);
   if (lines.isEmpty()) {
      aSkill.contentLoaded = true;
      return;
   }

   int bodyStartIndex = 0;
   QMap<QString, QString> meta;
   const bool hasFrontMatter = ParseFrontMatter(lines, bodyStartIndex, meta);

   QStringList bodyLines;
   if (hasFrontMatter && bodyStartIndex < lines.size()) {
      bodyLines = lines.mid(bodyStartIndex);
   } else {
      bodyLines = lines;
   }
   aSkill.content       = bodyLines.join('\n').trimmed();
   aSkill.contentLoaded = true;
}

void SkillManager::AddSkill(const Skill& aSkill)
{
   const QString key = NormalizeName(aSkill.name);
   if (key.isEmpty()) {
      return;
   }

   if (mSkills.contains(key)) {
      const Skill& existing = mSkills[key];
      // Global > Project override.
      if (aSkill.source == SkillSource::Global
          && existing.source == SkillSource::Project)
      {
         mSkills[key] = aSkill;
      }
      // Same source: last scan wins.
      else if (aSkill.source == existing.source) {
         mSkills[key] = aSkill;
      }
      // Project cannot overwrite global — ignore.
      return;
   }
   mSkills.insert(key, aSkill);
}

// ---------------------------------------------------------------------------
// Activated-skill lifecycle
// ---------------------------------------------------------------------------

void SkillManager::ActivateSkill(const QString& aName)
{
   const QString key = NormalizeName(aName);
   if (!mSkills.contains(key)) {
      qDebug() << "[AIChat::SkillManager] ActivateSkill: unknown skill" << aName;
      return;
   }
   // Ensure content is loaded
   Skill& skill = mSkills[key];
   if (!skill.contentLoaded) {
      LoadSkillContent(skill);
   }
   mActivatedSkills.insert(key);
   qDebug() << "[AIChat::SkillManager] Skill activated:" << key;
}

void SkillManager::DeactivateSkill(const QString& aName)
{
   mActivatedSkills.remove(NormalizeName(aName));
}

void SkillManager::ClearActivatedSkills()
{
   mActivatedSkills.clear();
}

QStringList SkillManager::GetActivatedSkillNames() const
{
   QStringList names(mActivatedSkills.begin(), mActivatedSkills.end());
   names.sort(Qt::CaseInsensitive);
   return names;
}

QString SkillManager::BuildActivatedSkillsContent()
{
   if (mActivatedSkills.isEmpty()) {
      return {};
   }

   QStringList blocks;
   blocks << QStringLiteral("## Active Skills\n");

   for (const QString& key : mActivatedSkills)
   {
      if (!mSkills.contains(key)) {
         continue;
      }
      Skill& skill = mSkills[key];
      if (!skill.contentLoaded) {
         LoadSkillContent(skill);
      }
      if (skill.content.isEmpty()) {
         continue;
      }

      blocks << QStringLiteral("<skill name=\"%1\">").arg(skill.name);
      blocks << skill.content.trimmed();
      blocks << QStringLiteral("</skill>");
      blocks << QString();
   }

   return blocks.join('\n');
}

} // namespace AiChat
