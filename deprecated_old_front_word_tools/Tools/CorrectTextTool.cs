using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>一条纠错建议（原文 → 建议）</summary>
    public class CorrectionItem
    {
        public string Original { get; set; }
        public string Replacement { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>
    /// AI 原生设计：纯"应用修改"工具。
    /// LLM 自行分析文本找出错误，然后调用此工具执行查找替换（审阅模式 + 批注原因）。
    /// 纠错推理由主 Agent 或子智能体负责，本工具不做任何 LLM 调用。
    /// </summary>
    public class CorrectTextTool : ToolBase
    {
        public override string Name => "correct_text";
        public override string DisplayName => "应用纠错";
        public override ToolCategory Category => ToolCategory.Editing;

        public override string Description =>
            "Apply text corrections to the document in Track Changes mode. " +
            "You provide a list of corrections (original→replacement with reason); " +
            "the tool executes find-replace with revision tracking and adds comments for each correction. " +
            "Use this AFTER you've analyzed the text and identified errors. " +
            "scope: 'selection' (only within selected text) or 'all' (entire document, default). " +
            "Each correction's 'original' must be an exact substring found in the document (max 255 chars).";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["corrections"] = new JObject
                {
                    ["type"] = "array",
                    ["description"] = "纠错列表，每项包含 original/replacement/reason",
                    ["items"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["original"] = new JObject { ["type"] = "string", ["description"] = "原文中需修正的精确片段（必须能在文档中匹配到）" },
                            ["replacement"] = new JObject { ["type"] = "string", ["description"] = "修正后的文本" },
                            ["reason"] = new JObject { ["type"] = "string", ["description"] = "修正原因（中文说明，将作为批注添加）" }
                        },
                        ["required"] = new JArray("original", "replacement")
                    }
                },
                ["scope"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray("all", "selection"),
                    ["description"] = "修正范围：all=全文（默认），selection=仅选中区域"
                }
            },
            ["required"] = new JArray("corrections")
        };

        public override Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            var doc = RequireActiveDocument(connect);
            var app = connect.WordApplication;

            // ── 解析参数 ──
            var correctionsArray = OptionalArray(arguments, "corrections");
            if (correctionsArray == null || correctionsArray.Count == 0)
                throw new ToolArgumentException("corrections 参数为空或缺失");

            string scope = OptionalString(arguments, "scope", "all");

            // ── 确定修正范围 ──
            int rangeStart, rangeEnd;
            if (scope == "selection")
            {
                var selection = app.Selection;
                if (selection == null || selection.Start >= selection.End)
                    throw new ToolArgumentException("scope=selection 但没有选中文本");
                rangeStart = selection.Start;
                rangeEnd = selection.End;
            }
            else
            {
                rangeStart = doc.Content.Start;
                rangeEnd = doc.Content.End;
            }

            // ── 解析纠错列表 ──
            var corrections = new List<CorrectionItem>();
            foreach (var item in correctionsArray)
            {
                string original = item["original"]?.ToString();
                string replacement = item["replacement"]?.ToString();
                string reason = item["reason"]?.ToString();

                if (string.IsNullOrEmpty(original))
                    continue;
                if (replacement == null)
                    replacement = "";

                corrections.Add(new CorrectionItem
                {
                    Original = original,
                    Replacement = replacement,
                    Reason = reason ?? ""
                });
            }

            if (corrections.Count == 0)
                return Task.FromResult(ToolExecutionResult.Ok("纠错列表为空，无需修改。"));

            // ── 应用修改（审阅模式） ──
            int applied = connect.ApplyCorrectionsPublic(doc, rangeStart, rangeEnd, corrections);

            // ── 构建结果摘要 ──
            var details = new List<string>();
            foreach (var c in corrections)
                details.Add($"「{c.Original}」→「{c.Replacement}」（{c.Reason}）");

            string summary = $"共提交 {corrections.Count} 处纠错，成功应用 {applied} 处（审阅模式）。";
            if (applied < corrections.Count)
                summary += $"\n{corrections.Count - applied} 处未在文档中匹配到原文。";
            summary += "\n" + string.Join("\n", details);

            return Task.FromResult(ToolExecutionResult.Ok(summary));
        }
    }
}
