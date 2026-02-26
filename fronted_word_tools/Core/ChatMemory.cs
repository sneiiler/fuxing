using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FuXing
{
    // ═══════════════════════════════════════════════════════════════
    //  消息角色
    // ═══════════════════════════════════════════════════════════════

    public enum ChatMessageRole
    {
        System,
        User,
        Assistant,
        Tool
    }

    // ═══════════════════════════════════════════════════════════════
    //  工具调用结构
    // ═══════════════════════════════════════════════════════════════

    /// <summary>大模型返回的工具调用请求</summary>
    public class ToolCallRequest
    {
        public string Id { get; set; }
        public string FunctionName { get; set; }
        public JObject Arguments { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════
    //  会话消息
    // ═══════════════════════════════════════════════════════════════

    /// <summary>一条会话消息，支持文本、工具调用、工具结果</summary>
    public class MemoryMessage
    {
        public ChatMessageRole Role { get; set; }
        public string Content { get; set; }

        /// <summary>仅 Assistant 消息：大模型请求调用的工具列表</summary>
        public List<ToolCallRequest> ToolCalls { get; set; }

        /// <summary>仅 Tool 消息：对应的 tool_call_id</summary>
        public string ToolCallId { get; set; }

        /// <summary>仅 Tool 消息：工具名称</summary>
        public string ToolName { get; set; }

        /// <summary>角色字符串（用于序列化）</summary>
        public string RoleString
        {
            get
            {
                switch (Role)
                {
                    case ChatMessageRole.System: return "system";
                    case ChatMessageRole.User: return "user";
                    case ChatMessageRole.Assistant: return "assistant";
                    case ChatMessageRole.Tool: return "tool";
                    default: return "user";
                }
            }
        }

        /// <summary>序列化为 OpenAI API 格式的 JObject</summary>
        public JObject ToApiJson()
        {
            var obj = new JObject
            {
                ["role"] = RoleString
            };

            // Assistant 消息可能只有 tool_calls 没有 content
            if (Content != null)
                obj["content"] = Content;

            // 序列化工具调用
            if (ToolCalls != null && ToolCalls.Count > 0)
            {
                var arr = new JArray();
                foreach (var tc in ToolCalls)
                {
                    arr.Add(new JObject
                    {
                        ["id"] = tc.Id,
                        ["type"] = "function",
                        ["function"] = new JObject
                        {
                            ["name"] = tc.FunctionName,
                            ["arguments"] = tc.Arguments?.ToString(Formatting.None) ?? "{}"
                        }
                    });
                }
                obj["tool_calls"] = arr;
            }

            // Tool 消息需要 tool_call_id
            if (Role == ChatMessageRole.Tool && !string.IsNullOrEmpty(ToolCallId))
            {
                obj["tool_call_id"] = ToolCallId;
            }

            return obj;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  上下文记忆管理器
    //  参考 AIChat ContextManager 设计，适配 C# / Word 插件环境
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 管理整个会话的上下文历史。所有与大模型交互的消息（用户输入、
    /// AI 回复、工具调用及结果）都记录在此，确保大模型拥有完整记忆。
    /// </summary>
    public class ChatMemory
    {
        private readonly List<MemoryMessage> _history = new List<MemoryMessage>();
        private string _systemPrompt;

        /// <summary>上下文窗口大小（token）</summary>
        public int ContextWindow { get; set; } = 128000;

        /// <summary>安全上限（token），超过则裁剪</summary>
        public int MaxAllowedTokens { get; set; } = 98000;

        /// <summary>当前历史消息列表（只读视图）</summary>
        public IReadOnlyList<MemoryMessage> History => _history.AsReadOnly();

        /// <summary>历史消息数量</summary>
        public int Count => _history.Count;

        // ── 系统 Prompt ──

        /// <summary>设置系统 Prompt（会话开始时调用一次）</summary>
        public void SetSystemPrompt(string prompt)
        {
            _systemPrompt = prompt;
        }

        /// <summary>获取当前系统 Prompt</summary>
        public string GetSystemPrompt() => _systemPrompt;

        // ── 消息管理 ──

        /// <summary>添加用户消息</summary>
        public void AddUserMessage(string content)
        {
            _history.Add(new MemoryMessage
            {
                Role = ChatMessageRole.User,
                Content = content
            });
        }

        /// <summary>添加 AI 助手消息</summary>
        public void AddAssistantMessage(string content, List<ToolCallRequest> toolCalls = null)
        {
            _history.Add(new MemoryMessage
            {
                Role = ChatMessageRole.Assistant,
                Content = content,
                ToolCalls = toolCalls
            });
        }

        /// <summary>添加工具执行结果消息</summary>
        public void AddToolResult(string toolCallId, string toolName, string result)
        {
            _history.Add(new MemoryMessage
            {
                Role = ChatMessageRole.Tool,
                Content = result,
                ToolCallId = toolCallId,
                ToolName = toolName
            });
        }

        /// <summary>
        /// 记录一次插件操作到上下文（非交互式调用，如按钮点击触发的操作）。
        /// 同时记录"用户发起了操作"和"系统完成了操作"，让大模型了解发生了什么。
        /// </summary>
        public void RecordPluginAction(string actionName, string input, string output)
        {
            // 以用户消息形式记录操作意图
            _history.Add(new MemoryMessage
            {
                Role = ChatMessageRole.User,
                Content = "[用户通过插件按钮执行了「" + actionName + "」操作]\n输入: " + Truncate(input, 500)
            });

            // 以助手消息形式记录操作结果
            _history.Add(new MemoryMessage
            {
                Role = ChatMessageRole.Assistant,
                Content = "[「" + actionName + "」操作完成]\n结果: " + Truncate(output, 1000)
            });
        }

        /// <summary>清空所有历史记录</summary>
        public void Clear()
        {
            _history.Clear();
        }

        // ── 上下文准备（发送给 API 前调用） ──

        /// <summary>
        /// 构建发送给 API 的消息列表。包含系统提示 + 裁剪后的历史记录。
        /// 当 token 估计超出窗口时自动执行滑动窗口裁剪。
        /// </summary>
        public List<MemoryMessage> PrepareMessages()
        {
            var result = new List<MemoryMessage>();

            // 系统 Prompt 始终在最前面
            if (!string.IsNullOrEmpty(_systemPrompt))
            {
                result.Add(new MemoryMessage
                {
                    Role = ChatMessageRole.System,
                    Content = _systemPrompt
                });
            }

            // 估算 token，必要时裁剪
            var historyToUse = NeedsCompaction()
                ? TruncateHistory()
                : new List<MemoryMessage>(_history);

            result.AddRange(historyToUse);
            return result;
        }

        /// <summary>构建 API 请求用的 messages JSON 数组</summary>
        public JArray BuildMessagesJson()
        {
            var messages = PrepareMessages();
            var arr = new JArray();
            foreach (var msg in messages)
            {
                arr.Add(msg.ToApiJson());
            }
            return arr;
        }

        // ── Token 估算与裁剪 ──

        /// <summary>估算消息列表的 token 数（约 2 字符 / token 中文，3.5 字符 / token 混合）</summary>
        public static int EstimateTokens(IEnumerable<MemoryMessage> messages)
        {
            int totalChars = 0;
            foreach (var msg in messages)
            {
                totalChars += (msg.Content?.Length ?? 0);
                if (msg.ToolCalls != null)
                {
                    foreach (var tc in msg.ToolCalls)
                        totalChars += (tc.Arguments?.ToString()?.Length ?? 0) + (tc.FunctionName?.Length ?? 0);
                }
            }
            // 混合中英文场景，使用 2.5 字符/token 的经验比例
            return (int)(totalChars / 2.5);
        }

        /// <summary>是否需要裁剪</summary>
        public bool NeedsCompaction()
        {
            return EstimateTokens(_history) > MaxAllowedTokens * 0.8;
        }

        /// <summary>
        /// 滑动窗口裁剪：保留最早 2 条 + 最新一半消息。
        /// 裁剪后在衔接处插入截断提示。
        /// </summary>
        private List<MemoryMessage> TruncateHistory()
        {
            if (_history.Count <= 4)
                return new List<MemoryMessage>(_history);

            // 保留第一轮对话（前 2 条）+ 后一半
            int keepFromEnd = _history.Count / 2;
            // 确保不会切断工具调用对
            int cutEnd = _history.Count - keepFromEnd;

            // 向后调整，避免切在 Tool 消息上
            while (cutEnd < _history.Count && _history[cutEnd].Role == ChatMessageRole.Tool)
                cutEnd++;

            var result = new List<MemoryMessage>();

            // 保留最早的上下文
            result.Add(_history[0]);
            if (_history.Count > 1)
                result.Add(_history[1]);

            // 插入截断提示
            result.Add(new MemoryMessage
            {
                Role = ChatMessageRole.System,
                Content = "[注意：为节省上下文空间，中间部分对话历史已被省略。请基于剩余的上下文继续对话。]"
            });

            // 保留后半部分
            for (int i = cutEnd; i < _history.Count; i++)
                result.Add(_history[i]);

            return result;
        }

        // ── 辅助 ──

        private static string Truncate(string text, int maxLen)
        {
            if (string.IsNullOrEmpty(text)) return "(空)";
            return text.Length <= maxLen ? text : text.Substring(0, maxLen) + "...";
        }
    }
}
