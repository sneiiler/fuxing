using Microsoft.Extensions.AI;
using FuXingAgent.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FuXingAgent.Agents
{
    /// <summary>
    /// 子 Agent 运行器 — 使用 FunctionInvokingChatClient 自动处理工具调用循环，
    /// 不污染主对话上下文。
    /// </summary>
    public class SubAgentRunner
    {
        /// <summary>禁止子 Agent 使用的工具</summary>
        private static readonly HashSet<string> BlacklistedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "run_sub_agent",
            "execute_word_script",
            "batch_operations",
            "delete_section"
        };

        private readonly AgentBootstrap _bootstrap;
        private readonly ToolRegistry _toolRegistry;

        public SubAgentRunner(AgentBootstrap bootstrap, ToolRegistry toolRegistry)
        {
            _bootstrap = bootstrap;
            _toolRegistry = toolRegistry;
        }

        /// <summary>
        /// 运行子 Agent 任务，返回最终文本结果。
        /// FunctionInvokingChatClient 自动处理工具调用循环。
        /// </summary>
        public async Task<string> RunAsync(
            string agentName,
            string systemPrompt,
            string taskInstruction,
            IList<string> allowedTools,
            bool includeDocumentText,
            string documentText,
            int maxRounds = 10,
            CancellationToken cancellationToken = default)
        {
            var innerClient = _bootstrap.ChatClient;
            if (innerClient == null)
                throw new InvalidOperationException("Agent 未初始化");

            // 构建 system prompt
            var sb = new StringBuilder(systemPrompt ?? "");
            sb.AppendLine();
            sb.AppendLine("## 底线规则");
            sb.AppendLine("- 不要与用户聊天，你是一个执行任务的子代理");
            sb.AppendLine("- 只使用中文输出");
            sb.AppendLine("- 不要描述你正在做什么，直接执行");

            if (includeDocumentText && !string.IsNullOrEmpty(documentText))
            {
                string truncated = documentText.Length > 8000
                    ? TruncateAtParagraphBoundary(documentText, 8000)
                    : documentText;
                sb.AppendLine();
                sb.AppendLine("## 当前文档内容（前 8000 字符）");
                sb.AppendLine(truncated);
            }

            // 构建消息
            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, sb.ToString()),
                new ChatMessage(ChatRole.User, taskInstruction)
            };

            // 构建 ChatOptions（筛选工具）
            var options = new ChatOptions { Temperature = 0.5f };
            var filteredTools = _toolRegistry.GetAllTools(allowedTools)
                .Where(t => !BlacklistedTools.Contains(t.Name))
                .ToList();
            foreach (var tool in filteredTools)
                options.Tools.Add(tool);

            // FunctionInvokingChatClient 自动处理工具调用循环
            using (var funcClient = new FunctionInvokingChatClient(innerClient))
            {
                funcClient.MaximumIterationsPerRequest = maxRounds;
                DebugLogger.Instance.LogInfo($"[SubAgent] 启动: {agentName}");

                var response = await funcClient.GetResponseAsync(messages, options, cancellationToken);

                DebugLogger.Instance.LogInfo($"[SubAgent] {agentName} 完成, 消息数: {response.Messages?.Count ?? 0}");

                return response.Text ?? "";
            }
        }

        private static string TruncateAtParagraphBoundary(string text, int maxChars)
        {
            if (text.Length <= maxChars) return text;
            int lastNewline = text.LastIndexOf('\n', maxChars);
            return lastNewline > 0 ? text.Substring(0, lastNewline) : text.Substring(0, maxChars);
        }
    }
}
