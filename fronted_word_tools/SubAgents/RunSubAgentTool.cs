using FuXing.SubAgents;
using Newtonsoft.Json.Linq;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>
    /// 主 Agent 可调用的子智能体工具。
    /// 启动一个拥有独立上下文的子智能体，将文档数据注入后执行分析任务，
    /// 完成后将结果作为 tool_result 返回给主 Agent。
    /// 子智能体可复用主插件的只读工具获取更多文档信息。
    /// </summary>
    public class RunSubAgentTool : ToolBase
    {
        /// <summary>
        /// UI 进度回调（由 TaskPaneControl 在执行前设置，执行后清除）。
        /// 单线程环境（UI 线程），无需加锁。
        /// </summary>
        public static ISubAgentProgress ProgressSink { get; set; }

        public override string Name => "run_sub_agent";
        public override string DisplayName => "运行子智能体";
        public override ToolCategory Category => ToolCategory.Advanced;

        public override string Description =>
            "Launch an independent-context sub-agent to perform document analysis tasks. " +
            "The sub-agent has a completely separate conversation context, unaffected by the current conversation, " +
            "and can call read-only tools to gather additional document information.\n" +
            "Supported task types:\n" +
            "- analyze_structure: analyze document structure, infer heading levels (especially for documents without heading styles), detect formatting inconsistencies\n" +
            "- extract_key_info: extract key document information (data, dates, terms, arguments), check cross-document consistency\n" +
            "- custom: custom analysis task (describe in detail in the prompt)\n" +
            "Sub-agents only perform analysis and information extraction — they do not modify the document.";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["task"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray("analyze_structure", "extract_key_info", "custom"),
                    ["description"] = "任务类型：analyze_structure=结构分析, extract_key_info=关键信息提取, custom=自定义"
                },
                ["prompt"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "给子智能体的具体指令。" +
                        "对于 analyze_structure，可指定重点关注的方面（如'检查编号连续性'）；" +
                        "对于 extract_key_info，可指定要关注的信息类别（如'重点检查数值是否前后一致'）；" +
                        "对于 custom，需详细描述分析任务"
                },
                ["include_structure"] = new JObject
                {
                    ["type"] = "boolean",
                    ["description"] = "是否提取并注入文档结构元数据（段落格式信息）。默认 true"
                },
                ["include_text"] = new JObject
                {
                    ["type"] = "boolean",
                    ["description"] = "是否注入文档全文文本。默认：analyze_structure 时 false，其他 true"
                },
                ["max_text_chars"] = new JObject
                {
                    ["type"] = "integer",
                    ["description"] = "注入文档文本时的最大字符数。默认 8000"
                }
            },
            ["required"] = new JArray("task")
        };

        public override async Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            // ── 参数解析 ──
            string taskStr = RequireString(arguments, "task");
            string prompt = OptionalString(arguments, "prompt", "");

            SubAgentTask task;
            switch (taskStr)
            {
                case "analyze_structure": task = SubAgentTask.AnalyzeStructure; break;
                case "extract_key_info": task = SubAgentTask.ExtractKeyInfo; break;
                case "custom": task = SubAgentTask.Custom; break;
                default: throw new ToolArgumentException($"未知任务类型: {taskStr}");
            }

            bool includeStructure = OptionalBool(arguments, "include_structure", true);
            bool defaultIncludeText = task != SubAgentTask.AnalyzeStructure;
            bool includeText = OptionalBool(arguments, "include_text", defaultIncludeText);
            int maxTextChars = OptionalInt(arguments, "max_text_chars", 8000);

            // ── 文档数据提取（UI 线程，Word COM） ──
            var app = connect.WordApplication;
            if (app.Documents.Count == 0)
                throw new ToolArgumentException("没有打开的文档");
            var doc = app.ActiveDocument;

            DocumentStructure structure = null;
            if (includeStructure)
                structure = DocumentStructureExtractor.Extract(doc);

            string documentText = null;
            if (includeText)
            {
                string fullText = doc.Content.Text;
                documentText = fullText.Length > maxTextChars
                    ? TruncateAtParagraph(fullText, maxTextChars)
                    : fullText;
            }

            // ── 准备工具共用：只读工具子集 ──
            var toolRegistry = connect.ToolRegistry;
            JArray subAgentTools = toolRegistry.GetToolDefinitions(ToolCategory.Query);

            // 工具执行委托：直接在当前（UI）线程上调用 ToolRegistry
            Func<string, JObject, Task<ToolExecutionResult>> toolExecutor =
                (name, args) => toolRegistry.ExecuteAsync(name, args);

            // ── 启动子智能体（在 UI 线程上 async 运行，工具调用自然在 UI 线程） ──
            var request = new SubAgentRequest
            {
                Task = task,
                Prompt = prompt,
                DocumentStructure = structure,
                DocumentText = documentText,
                MaxRounds = 5,
                ToolDefinitions = subAgentTools,
                ToolExecutor = toolExecutor
            };

            var subAgent = new SubAgent();

            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120)))
            {
                var result = await subAgent.RunAsync(request, cts.Token, ProgressSink);

                if (!result.Success)
                    return ToolExecutionResult.Fail($"子智能体执行失败: {result.Output}");

                var sb = new StringBuilder();
                sb.AppendLine($"[子智能体分析完成] 任务={taskStr}, 轮次={result.RoundsUsed}, 估计token={result.EstimatedTokens}");
                sb.AppendLine();
                sb.Append(result.Output);

                return ToolExecutionResult.Ok(sb.ToString());
            }
        }

        /// <summary>在段落边界处截取文本</summary>
        private static string TruncateAtParagraph(string text, int maxChars)
        {
            if (text.Length <= maxChars)
                return text;

            int cutPoint = text.LastIndexOf('\r', maxChars);
            if (cutPoint < maxChars / 2)
                cutPoint = maxChars;

            return text.Substring(0, cutPoint) +
                   $"\n\n…（已截取前 {cutPoint} 字符，全文共 {text.Length} 字符）";
        }
    }
}
