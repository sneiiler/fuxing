using FuXing.SubAgents;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>
    /// 主 Agent 可调用的动态子智能体工具。
    /// 主 Agent（大模型）自行决定何时启用子智能体，并动态生成：
    ///   - agent_name: 子智能体名称（用于追踪）
    ///   - system_prompt: 子智能体的角色和职责定义
    ///   - task_instruction: 具体操作指令
    ///   - allowed_tools: 授权给子智能体的工具白名单
    /// 子智能体拥有独立上下文，完成后将结果作为 tool_result 返回给主 Agent。
    /// </summary>
    public class RunSubAgentTool : ToolBase
    {
        /// <summary>
        /// UI 进度回调（由 TaskPaneControl 在执行前设置，执行后清除）。
        /// 单线程环境（UI 线程），无需加锁。
        /// </summary>
        public static ISubAgentProgress ProgressSink { get; set; }

        /// <summary>
        /// 禁止子智能体接触的工具黑名单（防止递归调用或危险操作）。
        /// </summary>
        private static readonly HashSet<string> ForbiddenTools = new HashSet<string>
        {
            "run_sub_agent",          // 禁止子智能体递归创建子智能体
            "execute_word_script",    // 危险：任意代码执行
            "batch_operations",       // 危险：破坏性批量操作
            "delete_section",         // 危险：删除章节
        };

        public override string Name => "run_sub_agent";
        public override string DisplayName => "运行子智能体";
        public override ToolCategory Category => ToolCategory.Advanced;

        public override string Description =>
            "Launch a sub-agent with isolated context for complex tasks (e.g. cross-checking data across 50+ paragraphs, " +
            "structural analysis, exhaustive search). YOU dynamically define its role (system_prompt), task (task_instruction), " +
            "and which tools it can use (allowed_tools). The sub-agent works independently and returns only the final result. " +
            "Use this when a task requires extensive reading/searching that would bloat your main context.";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["agent_name"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "给子智能体起的名称（用于日志追踪，如 'AmountChecker', 'FormatAuditor'）"
                },
                ["system_prompt"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "动态生成的系统提示词。必须明确定义：1) 角色（如'你是一个合同金额核实专家'）; " +
                                      "2) 核心职责; 3) 输出格式要求。要求它只输出最终清洗后的结果，不要描述过程。"
                },
                ["task_instruction"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "发给子智能体的具体操作指令"
                },
                ["allowed_tools"] = new JObject
                {
                    ["type"] = "array",
                    ["items"] = new JObject { ["type"] = "string" },
                    ["description"] = "授权给子智能体使用的工具名称列表（建议只赋予只读工具如 " +
                                      "document_graph, get_document_info, get_selected_text, read_table, search_text 等）。" +
                                      "留空则为纯推理模式（不使用任何工具）。"
                },
                ["include_document_text"] = new JObject
                {
                    ["type"] = "boolean",
                    ["description"] = "是否将文档全文（前 8000 字符）预注入到子智能体上下文中（默认 false，推荐让它自己用工具获取）"
                },
                ["max_rounds"] = new JObject
                {
                    ["type"] = "integer",
                    ["description"] = "子智能体最大对话轮次（含工具调用循环，默认 5，推荐 3-8）"
                }
            },
            ["required"] = new JArray("system_prompt", "task_instruction")
        };

        public override async Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            // ── 参数解析 ──
            string agentName = OptionalString(arguments, "agent_name", "SubAgent");
            string systemPrompt = RequireString(arguments, "system_prompt");
            string taskInstruction = RequireString(arguments, "task_instruction");
            int maxRounds = OptionalInt(arguments, "max_rounds", 5);
            bool includeDocText = OptionalBool(arguments, "include_document_text", false);

            // 解析工具白名单
            var allowedToolNames = new List<string>();
            var allowedToolsToken = arguments["allowed_tools"] as JArray;
            if (allowedToolsToken != null)
            {
                foreach (var item in allowedToolsToken)
                    allowedToolNames.Add(item.ToString());
            }

            // 移除黑名单中的工具（静默过滤，不报错）
            allowedToolNames.RemoveAll(name => ForbiddenTools.Contains(name));

            // ── 文档上下文注入（可选） ──
            string documentContext = null;
            if (includeDocText)
            {
                var app = connect.WordApplication;
                if (app.Documents.Count > 0)
                {
                    var doc = app.ActiveDocument;
                    string fullText = doc.Content.Text;
                    int maxChars = 8000;
                    documentContext = fullText.Length > maxChars
                        ? TruncateAtParagraph(fullText, maxChars)
                        : fullText;
                }
            }

            // ── 根据白名单过滤工具定义 ──
            var toolRegistry = connect.ToolRegistry;
            JArray subAgentTools = null;
            Func<string, JObject, Task<ToolExecutionResult>> toolExecutor = null;

            if (allowedToolNames.Count > 0)
            {
                subAgentTools = toolRegistry.GetToolDefinitionsByName(allowedToolNames);
                toolExecutor = (name, args) => toolRegistry.ExecuteAsync(name, args);
            }

            // ── 启动子智能体（在 UI 线程上 async 运行，工具调用自然在 UI 线程） ──
            var request = new SubAgentRequest
            {
                AgentName = agentName,
                SystemPrompt = systemPrompt,
                TaskInstruction = taskInstruction,
                DocumentContext = documentContext,
                MaxRounds = maxRounds,
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
                sb.AppendLine($"[子智能体 {agentName} 完成] 轮次={result.RoundsUsed}, 估计token={result.EstimatedTokens}");
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
