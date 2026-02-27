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
            _history.Add(new MemoryMessage
            {
                Role = ChatMessageRole.User,
                Content = "[User triggered '" + actionName + "' via plugin button]\nInput: " + Truncate(input, 500)
            });

            _history.Add(new MemoryMessage
            {
                Role = ChatMessageRole.Assistant,
                Content = "['" + actionName + "' completed]\nResult: " + Truncate(output, 1000)
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

        /// <summary>工具定义占用的预留 token 数（由外部设置，用于精确计算裁剪阈值）</summary>
        public int ToolTokenReserve { get; set; }

        /// <summary>是否需要裁剪（基于 system prompt + history + 工具定义的总量）</summary>
        public bool NeedsCompaction()
        {
            return EstimateTotalTokens() > MaxAllowedTokens * 0.8;
        }

        /// <summary>估算当前完整请求的 token 数（system prompt + history + 工具定义预留）</summary>
        public int EstimateTotalTokens()
        {
            var all = new List<MemoryMessage>();
            if (!string.IsNullOrEmpty(_systemPrompt))
            {
                all.Add(new MemoryMessage { Role = ChatMessageRole.System, Content = _systemPrompt });
            }
            all.AddRange(_history);
            return EstimateTokens(all) + ToolTokenReserve;
        }

        /// <summary>
        /// 滑动窗口裁剪：保留最早的完整对话轮次 + 最新一半消息。
        /// 裁剪时确保 tool_calls → tool result 配对完整性。
        /// </summary>
        private List<MemoryMessage> TruncateHistory()
        {
            if (_history.Count <= 6)
                return new List<MemoryMessage>(_history);

            // ── 计算头部保留边界：找到第一个完整对话轮次的结尾 ──
            int headEnd = FindFirstCompleteRoundEnd();

            // ── 计算尾部保留起点 ──
            int keepFromEnd = _history.Count / 2;
            int cutEnd = _history.Count - keepFromEnd;

            // 向后调整，跳过孤立的 Tool 消息
            while (cutEnd < _history.Count && _history[cutEnd].Role == ChatMessageRole.Tool)
                cutEnd++;

            // 向后调整，如果 cutEnd 指向一个带 tool_calls 的 assistant 消息，
            // 需要包含其后续所有 Tool 结果消息
            if (cutEnd > 0 && cutEnd < _history.Count)
            {
                var prev = _history[cutEnd - 1];
                if (prev.Role == ChatMessageRole.Assistant && prev.ToolCalls != null && prev.ToolCalls.Count > 0)
                {
                    // 回退到这个 assistant 消息开始
                    cutEnd--;
                }
            }

            // 确保 cutEnd 不会跑到 headEnd 之前
            if (cutEnd <= headEnd)
                cutEnd = headEnd + 1;

            var result = new List<MemoryMessage>();

            // 保留头部完整轮次
            for (int i = 0; i <= headEnd && i < _history.Count; i++)
                result.Add(_history[i]);

            // 插入截断提示（作为 user 消息，避免多 system 消息的兼容性问题）
            result.Add(new MemoryMessage
            {
                Role = ChatMessageRole.User,
                Content = "[Note: Earlier conversation history has been trimmed to save context space. Continue based on the remaining context.]"
            });
            result.Add(new MemoryMessage
            {
                Role = ChatMessageRole.Assistant,
                Content = "Understood. I will continue based on the available context."
            });

            // 保留后半部分
            for (int i = cutEnd; i < _history.Count; i++)
                result.Add(_history[i]);

            return result;
        }

        /// <summary>
        /// 找到第一个完整对话轮次的结束索引。
        /// 一个完整轮次 = user + assistant（+ 可能的 tool_calls 与 tool results）。
        /// </summary>
        private int FindFirstCompleteRoundEnd()
        {
            int i = 0;
            // 跳过开头的 user 消息
            if (i < _history.Count && _history[i].Role == ChatMessageRole.User)
                i++;
            else
                return 0;

            // 找 assistant 消息
            if (i < _history.Count && _history[i].Role == ChatMessageRole.Assistant)
            {
                // 如果 assistant 有 tool_calls，需要继续包含后续的 tool result 消息
                if (_history[i].ToolCalls != null && _history[i].ToolCalls.Count > 0)
                {
                    i++;
                    while (i < _history.Count && _history[i].Role == ChatMessageRole.Tool)
                        i++;
                    // 如果工具结果后还有一个纯文本 assistant 回复，也保留
                    if (i < _history.Count && _history[i].Role == ChatMessageRole.Assistant)
                        return i;
                    return i - 1;
                }
                return i;
            }

            return 0;
        }

        // ── 辅助 ──

        private static string Truncate(string text, int maxLen)
        {
            if (string.IsNullOrEmpty(text)) return "(空)";
            return text.Length <= maxLen ? text : text.Substring(0, maxLen) + "...";
        }
    }
}
