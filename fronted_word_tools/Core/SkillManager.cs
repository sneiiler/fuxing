using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FuXing
{
    /// <summary>
    /// 管理所有可供大模型使用的 Skill（技能）。
    /// 对齐 AIChat 项目的 Skill 系统设计：
    ///   - 全局 skills:   %USERPROFILE%\.fuxing\skills\*\SKILL.md
    ///   - 文档级 skills:  &lt;文档所在目录&gt;\.fuxing\skills\*\SKILL.md
    ///   - SKILL.md 的 YAML 前言必须包含 name 和 description 字段
    ///   - name 必须匹配其所在目录名
    /// </summary>
    public class SkillManager
    {
        // ═══════════════════════════════════════════════════════════════
        //  数据结构
        // ═══════════════════════════════════════════════════════════════

        public enum SkillSource { Global, Document }

        public class Skill
        {
            /// <summary>技能名称（== 目录名，小写标准化）</summary>
            public string Name { get; set; }

            /// <summary>简短描述（来自 frontmatter）</summary>
            public string Description { get; set; }

            /// <summary>SKILL.md 所在目录的绝对路径</summary>
            public string Directory { get; set; }

            /// <summary>SKILL.md 的绝对路径</summary>
            public string SkillPath { get; set; }

            /// <summary>正文内容（懒加载）</summary>
            public string Content { get; set; }

            /// <summary>技能目录中的附属文件（相对路径）</summary>
            public List<string> SupportFiles { get; set; } = new List<string>();

            /// <summary>来源</summary>
            public SkillSource Source { get; set; } = SkillSource.Document;

            /// <summary>内容是否已加载</summary>
            public bool ContentLoaded { get; set; }
        }

        // ═══════════════════════════════════════════════════════════════
        //  字段
        // ═══════════════════════════════════════════════════════════════

        private readonly Dictionary<string, Skill> _skills = new Dictionary<string, Skill>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _activatedSkills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string _lastDocumentDir = "";

        // ═══════════════════════════════════════════════════════════════
        //  公开 API
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 扫描全局和文档级 skill 目录，发现所有可用 skill。
        /// 全局 skill 优先级高于文档级同名 skill。
        /// </summary>
        public void LoadFromDocumentDir(string documentDir)
        {
            _lastDocumentDir = documentDir ?? "";
            _skills.Clear();

            // 1. 扫描文档级目录（低优先级）
            if (!string.IsNullOrWhiteSpace(documentDir))
            {
                string docSkills = Path.Combine(documentDir, ".fuxing", "skills");
                ScanSkillRoot(docSkills, SkillSource.Document);
            }

            // 2. 扫描全局目录（高优先级，同名覆盖文档级）
            ScanSkillRoot(GetGlobalSkillsDirectory(), SkillSource.Global);

            DebugLogger.Instance.LogInfo($"[SkillManager] 发现 {_skills.Count} 个 skill: {string.Join(", ", GetSkillNames())}");
        }

        /// <summary>重新发现（保留上次的文档目录）</summary>
        public void Rediscover()
        {
            LoadFromDocumentDir(_lastDocumentDir);
        }

        /// <summary>构建可用 skill 目录摘要（注入到 system prompt）</summary>
        public string BuildCatalogSummary()
        {
            var names = GetSkillNames();
            if (names.Count == 0) return "";

            var sb = new StringBuilder();
            sb.AppendLine("Available Skills (use the load_skill tool to load by name):");

            foreach (var name in names)
            {
                if (!_skills.TryGetValue(NormalizeName(name), out var skill)) continue;
                string sourceTag = skill.Source == SkillSource.Global ? " [全局]" : "";
                string desc = skill.Description.Length > 200
                    ? skill.Description.Substring(0, 200) + "..."
                    : skill.Description;
                sb.AppendLine($"  - \"{skill.Name}\": {desc}{sourceTag}");
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>判断 skill 是否存在</summary>
        public bool HasSkill(string name) => _skills.ContainsKey(NormalizeName(name));

        /// <summary>获取 skill（不存在时返回 null）</summary>
        public Skill GetSkill(string name)
        {
            _skills.TryGetValue(NormalizeName(name), out var skill);
            return skill;
        }

        /// <summary>获取 skill 的完整内容（懒加载）</summary>
        public string GetSkillContent(string name)
        {
            var key = NormalizeName(name);
            if (!_skills.TryGetValue(key, out var skill)) return "";
            if (!skill.ContentLoaded) LoadSkillContent(skill);
            return skill.Content ?? "";
        }

        /// <summary>获取所有已启用的 skill 名称（排序）</summary>
        public List<string> GetSkillNames()
        {
            var names = _skills.Values.Select(s => s.Name).ToList();
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        /// <summary>获取所有已发现的 skill</summary>
        public List<Skill> GetAllSkills() => _skills.Values.ToList();

        // ═══════════════════════════════════════════════════════════════
        //  激活管理
        // ═══════════════════════════════════════════════════════════════

        /// <summary>激活一个 skill（其内容将注入 system prompt）</summary>
        public void ActivateSkill(string name)
        {
            var key = NormalizeName(name);
            if (!_skills.TryGetValue(key, out var skill))
                throw new InvalidOperationException($"Skill \"{name}\" 不存在");

            if (!skill.ContentLoaded) LoadSkillContent(skill);
            _activatedSkills.Add(key);
            DebugLogger.Instance.LogInfo($"[SkillManager] Skill 已激活: {key}");
        }

        /// <summary>停用一个 skill</summary>
        public void DeactivateSkill(string name) => _activatedSkills.Remove(NormalizeName(name));

        /// <summary>清空所有激活的 skill（会话重置时调用）</summary>
        public void ClearActivatedSkills() => _activatedSkills.Clear();

        /// <summary>获取当前已激活的 skill 名称列表</summary>
        public List<string> GetActivatedSkillNames()
        {
            var names = _activatedSkills.ToList();
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        /// <summary>
        /// 构建所有已激活 skill 的内容块，用于注入 system prompt 的 SKILLS 段。
        /// 无激活 skill 时返回空字符串。
        /// </summary>
        public string BuildActivatedSkillsContent()
        {
            if (_activatedSkills.Count == 0) return "";

            var sb = new StringBuilder();
            sb.AppendLine("## Activated Skills\n");

            foreach (var key in _activatedSkills)
            {
                if (!_skills.TryGetValue(key, out var skill)) continue;
                if (!skill.ContentLoaded) LoadSkillContent(skill);
                if (string.IsNullOrEmpty(skill.Content)) continue;

                sb.AppendLine($"<skill name=\"{skill.Name}\">");
                sb.AppendLine(skill.Content.Trim());
                sb.AppendLine("</skill>");
                sb.AppendLine();
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>全局 skills 目录路径</summary>
        public static string GetGlobalSkillsDirectory()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string dir = Path.Combine(home, ".fuxing", "skills");
            if (!System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);
            return dir;
        }

        // ═══════════════════════════════════════════════════════════════
        //  内部实现
        // ═══════════════════════════════════════════════════════════════

        private static string NormalizeName(string name) => (name ?? "").Trim().ToLowerInvariant();

        private void ScanSkillRoot(string rootDir, SkillSource source)
        {
            if (!System.IO.Directory.Exists(rootDir)) return;

            DebugLogger.Instance.LogInfo($"[SkillManager] 扫描 {rootDir} ({source})");

            foreach (var subDir in System.IO.Directory.GetDirectories(rootDir))
            {
                string skillMdPath = Path.Combine(subDir, "SKILL.md");
                if (!File.Exists(skillMdPath)) continue;

                var skill = ParseSkillDirectory(subDir, skillMdPath, source);
                if (skill != null) AddSkill(skill);
            }
        }

        private Skill ParseSkillDirectory(string dirPath, string skillMdPath, SkillSource source)
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(skillMdPath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogInfo($"[SkillManager] 读取 {skillMdPath} 失败: {ex.Message}");
                return null;
            }

            if (lines.Length == 0) return null;

            // 解析 YAML 前言
            if (!ParseFrontMatter(lines, out int bodyStartIndex, out var meta))
            {
                DebugLogger.Instance.LogInfo($"[SkillManager] {dirPath} 缺少 YAML 前言，已跳过");
                return null;
            }

            string fmName = GetMetaValue(meta, "name");
            string fmDescription = GetMetaValue(meta, "description");
            string dirName = Path.GetFileName(dirPath);

            // 验证元数据
            if (string.IsNullOrEmpty(fmName))
            {
                DebugLogger.Instance.LogInfo($"[SkillManager] {dirPath} 缺少 name 字段，已跳过");
                return null;
            }
            if (string.IsNullOrEmpty(fmDescription))
            {
                DebugLogger.Instance.LogInfo($"[SkillManager] {dirPath} 缺少 description 字段，已跳过");
                return null;
            }
            if (!string.Equals(fmName, dirName, StringComparison.OrdinalIgnoreCase))
            {
                DebugLogger.Instance.LogInfo($"[SkillManager] name \"{fmName}\" 与目录名 \"{dirName}\" 不匹配，已跳过");
                return null;
            }

            // 枚举附属文件
            var supportFiles = new List<string>();
            foreach (var file in System.IO.Directory.GetFiles(dirPath, "*", SearchOption.AllDirectories))
            {
                string relativePath = file.Substring(dirPath.Length + 1);
                if (string.Equals(Path.GetFileName(file), "SKILL.md", StringComparison.OrdinalIgnoreCase))
                    continue;
                supportFiles.Add(relativePath);
            }
            supportFiles.Sort(StringComparer.OrdinalIgnoreCase);

            return new Skill
            {
                Name = fmName.Trim().ToLowerInvariant(),
                Description = fmDescription,
                Directory = dirPath,
                SkillPath = skillMdPath,
                Source = source,
                SupportFiles = supportFiles
            };
        }

        private void AddSkill(Skill skill)
        {
            var key = NormalizeName(skill.Name);
            if (_skills.TryGetValue(key, out var existing))
            {
                // 全局 > 文档级覆盖
                if (skill.Source == SkillSource.Global && existing.Source == SkillSource.Document)
                    _skills[key] = skill;
                else if (skill.Source == existing.Source)
                    _skills[key] = skill; // 同源：后扫描覆盖
                // 文档级不能覆盖全局 — 忽略
            }
            else
            {
                _skills[key] = skill;
            }
        }

        private void LoadSkillContent(Skill skill)
        {
            try
            {
                var lines = File.ReadAllLines(skill.SkillPath, Encoding.UTF8);
                if (ParseFrontMatter(lines, out int bodyStart, out _))
                {
                    skill.Content = string.Join("\n", lines, bodyStart, lines.Length - bodyStart).Trim();
                }
                else
                {
                    skill.Content = string.Join("\n", lines).Trim();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogInfo($"[SkillManager] 加载 skill 内容失败: {ex.Message}");
                skill.Content = "";
            }
            skill.ContentLoaded = true;
        }

        // ───────── YAML 前言解析 ─────────

        /// <summary>解析 --- 分隔的 YAML 前言</summary>
        private static bool ParseFrontMatter(string[] lines, out int bodyStartIndex, out Dictionary<string, string> meta)
        {
            bodyStartIndex = 0;
            meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (lines.Length == 0 || lines[0].Trim() != "---") return false;

            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line == "---")
                {
                    bodyStartIndex = i + 1;
                    return true;
                }

                int colonIdx = line.IndexOf(':');
                if (colonIdx <= 0) continue;

                string key = line.Substring(0, colonIdx).Trim().ToLowerInvariant();
                string value = line.Substring(colonIdx + 1).Trim();
                if (!string.IsNullOrEmpty(key))
                    meta[key] = value;
            }

            return false; // 未闭合的前言
        }

        private static string GetMetaValue(Dictionary<string, string> meta, string key)
        {
            meta.TryGetValue(key, out var value);
            return (value ?? "").Trim();
        }
    }
}
