using Newtonsoft.Json.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>
    /// LLM 可调用的工具：按名称加载并激活一个 Skill。
    /// 激活后，Skill 的完整内容将注入 system prompt，在整个会话中持续生效。
    /// </summary>
    public class LoadSkillTool : ToolBase
    {
        public override string Name => "load_skill";
        public override string DisplayName => "加载技能";
        public override ToolCategory Category => ToolCategory.System;

        public override string Description =>
            "Load and activate a Skill by name. " +
            "Available skill names and descriptions are listed in the system prompt — choose the one relevant to the current task. " +
            "Once activated, the skill's full instructions are injected into the context and remain active for the session. " +
            "Do not reload the same skill within one conversation turn.";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["skill_name"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "要激活的 Skill 名称（必须与可用 Skill 列表中的名称精确匹配）"
                }
            },
            ["required"] = new JArray("skill_name")
        };

        public override Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            string skillName = arguments["skill_name"]?.ToString();
            if (string.IsNullOrWhiteSpace(skillName))
                return Task.FromResult(ToolExecutionResult.Fail("缺少 skill_name 参数"));

            var skillManager = connect.SkillManager;

            // 若 skill 不存在，尝试重新发现后再查一次
            if (!skillManager.HasSkill(skillName))
            {
                skillManager.Rediscover();
            }

            if (!skillManager.HasSkill(skillName))
            {
                var available = skillManager.GetSkillNames();
                if (available.Count == 0)
                    return Task.FromResult(ToolExecutionResult.Fail("当前没有可用的 Skill。"));

                return Task.FromResult(ToolExecutionResult.Fail(
                    $"Skill \"{skillName}\" 不存在。可用 Skills: {string.Join(", ", available)}"));
            }

            // 激活 skill
            skillManager.ActivateSkill(skillName);
            var skill = skillManager.GetSkill(skillName);

            // 构建返回消息
            var sb = new StringBuilder();
            sb.AppendLine($"Skill \"{skill.Name}\" 已激活。其 SKILL.md 指令已注入系统上下文，本次会话中持续生效。");
            sb.AppendLine($"来源: {(skill.Source == SkillManager.SkillSource.Global ? "全局" : "文档级")}");
            sb.AppendLine($"技能目录: {skill.Directory}");

            if (skill.SupportFiles.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("技能目录中的附属文件：");
                foreach (var file in skill.SupportFiles)
                    sb.AppendLine($"- {skill.Directory}\\{file}");
            }

            return Task.FromResult(ToolExecutionResult.Ok(sb.ToString().TrimEnd()));
        }
    }
}
