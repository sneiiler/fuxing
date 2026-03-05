using Microsoft.Extensions.AI;
using FuXingAgent.Core;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FuXingAgent.Agents
{
#pragma warning disable MEAI001
    /// <summary>
    /// AgentSession 内部会话状态。
    /// 仅用于会话驱动和序列化，不再对 UI 暴露独立历史链路。
    /// </summary>
    public class ConversationState
    {
        private readonly List<ChatMessage> _history = new List<ChatMessage>();

        public int ContextWindow { get; set; } = 128000;
        public int MaxAllowedTokens { get; set; } = 98000;
        public int ToolTokenReserve { get; set; }

        public void AddMessage(ChatMessage message)
        {
            if (message == null) return;
            _history.Add(message);
        }

        public void AddAssistantMessage(string content)
        {
            _history.Add(new ChatMessage(ChatRole.Assistant, content));
        }

        public void AddAssistantToolCallMessage(string content, IList<FunctionCallContent> toolCalls)
        {
            var msg = new ChatMessage { Role = ChatRole.Assistant };
            if (!string.IsNullOrEmpty(content))
                msg.Contents.Add(new TextContent(content));
            foreach (var tc in toolCalls)
                msg.Contents.Add(tc);
            _history.Add(msg);
        }

        public void AddToolResult(string callId, string result)
        {
            var msg = new ChatMessage { Role = ChatRole.Tool };
            msg.Contents.Add(new FunctionResultContent(callId, result));
            _history.Add(msg);
        }

        public List<ChatMessage> PrepareMessages(string systemPrompt)
        {
            var result = new List<ChatMessage>();
            if (!string.IsNullOrEmpty(systemPrompt))
                result.Add(new ChatMessage(ChatRole.System, systemPrompt));

            var historyToUse = NeedsCompaction() ? TruncateHistory() : new List<ChatMessage>(_history);
            result.AddRange(historyToUse);
            return result;
        }

        public List<SessionMessage> ExportMessages()
        {
            var result = new List<SessionMessage>();
            foreach (var msg in _history)
            {
                var sm = new SessionMessage { Role = msg.Role.Value };

                var textContent = msg.Contents.OfType<TextContent>().FirstOrDefault();
                sm.Content = textContent?.Text;

                var toolCalls = msg.Contents.OfType<FunctionCallContent>().ToList();
                if (toolCalls.Count > 0)
                {
                    sm.ToolCalls = toolCalls.Select(tc => new SessionToolCall
                    {
                        Id = tc.CallId,
                        FunctionName = tc.Name,
                        ArgumentsJson = tc.Arguments != null
                            ? System.Text.Json.JsonSerializer.Serialize(tc.Arguments)
                            : "{}"
                    }).ToList();
                }

                var toolResult = msg.Contents.OfType<FunctionResultContent>().FirstOrDefault();
                if (toolResult != null)
                {
                    sm.ToolCallId = toolResult.CallId;
                    sm.Content = toolResult.Result?.ToString();
                }

                result.Add(sm);
            }
            return result;
        }

        public void ImportMessages(List<SessionMessage> messages)
        {
            _history.Clear();
            if (messages == null) return;
            foreach (var sm in messages)
            {
                var role = new ChatRole(sm.Role);
                var msg = new ChatMessage { Role = role };

                if (sm.ToolCalls != null && sm.ToolCalls.Count > 0)
                {
                    if (!string.IsNullOrEmpty(sm.Content))
                        msg.Contents.Add(new TextContent(sm.Content));

                    foreach (var tc in sm.ToolCalls)
                    {
                        IDictionary<string, object> args = null;
                        if (!string.IsNullOrEmpty(tc.ArgumentsJson))
                        {
                            try
                            {
                                args = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(tc.ArgumentsJson);
                            }
                            catch { args = new Dictionary<string, object>(); }
                        }
                        msg.Contents.Add(new FunctionCallContent(tc.Id, tc.FunctionName, args));
                    }
                }
                else if (!string.IsNullOrEmpty(sm.ToolCallId))
                {
                    msg.Contents.Add(new FunctionResultContent(sm.ToolCallId, sm.Content));
                }
                else if (sm.Content != null)
                {
                    msg.Contents.Add(new TextContent(sm.Content));
                }

                _history.Add(msg);
            }
        }

        public int EstimateTotalTokens()
        {
            int chars = 0;
            foreach (var msg in _history)
                chars += EstimateMessageChars(msg);
            return (int)(chars / 2.5) + ToolTokenReserve;
        }

        private bool NeedsCompaction() => EstimateTotalTokens() > MaxAllowedTokens * 0.8;

        private static int EstimateMessageChars(ChatMessage msg)
        {
            int chars = 0;
            foreach (var c in msg.Contents)
            {
                if (c is TextContent tc) chars += tc.Text?.Length ?? 0;
                else if (c is FunctionCallContent fc)
                {
                    chars += fc.Name?.Length ?? 0;
                    if (fc.Arguments != null)
                        chars += System.Text.Json.JsonSerializer.Serialize(fc.Arguments).Length;
                }
                else if (c is FunctionResultContent fr)
                    chars += fr.Result?.ToString()?.Length ?? 0;
            }
            return chars;
        }

        private List<ChatMessage> TruncateHistory()
        {
            if (_history.Count <= 6)
                return new List<ChatMessage>(_history);

            int headEnd = FindFirstCompleteRoundEnd();
            int keepFromEnd = _history.Count / 2;
            int cutEnd = _history.Count - keepFromEnd;

            while (cutEnd < _history.Count && _history[cutEnd].Role == ChatRole.Tool)
                cutEnd++;

            if (cutEnd > 0 && cutEnd < _history.Count)
            {
                var prev = _history[cutEnd - 1];
                if (prev.Role == ChatRole.Assistant && prev.Contents.OfType<FunctionCallContent>().Any())
                    cutEnd--;
            }

            if (cutEnd <= headEnd)
                cutEnd = headEnd + 1;

            var result = new List<ChatMessage>();
            for (int i = 0; i <= headEnd && i < _history.Count; i++)
                result.Add(_history[i]);

            result.Add(new ChatMessage(ChatRole.User,
                "[Note: Earlier conversation history has been trimmed to save context space. Continue based on the remaining context.]"));
            result.Add(new ChatMessage(ChatRole.Assistant,
                "Understood. I will continue based on the available context."));

            for (int i = cutEnd; i < _history.Count; i++)
                result.Add(_history[i]);

            return result;
        }

        private int FindFirstCompleteRoundEnd()
        {
            int i = 0;
            if (i < _history.Count && _history[i].Role == ChatRole.User)
                i++;
            else
                return 0;

            if (i < _history.Count && _history[i].Role == ChatRole.Assistant)
            {
                if (_history[i].Contents.OfType<FunctionCallContent>().Any())
                {
                    i++;
                    while (i < _history.Count && _history[i].Role == ChatRole.Tool)
                        i++;
                    if (i < _history.Count && _history[i].Role == ChatRole.Assistant)
                        return i;
                    return i - 1;
                }
                return i;
            }
            return 0;
        }
    }

    /// <summary>
    /// AgentSession 序列化用的消息结构。
    /// </summary>
    public class SessionMessage
    {
        [JsonProperty("role")]
        public string Role { get; set; }

        [JsonProperty("content")]
        public string Content { get; set; }

        [JsonProperty("tool_calls", NullValueHandling = NullValueHandling.Ignore)]
        public List<SessionToolCall> ToolCalls { get; set; }

        [JsonProperty("tool_call_id", NullValueHandling = NullValueHandling.Ignore)]
        public string ToolCallId { get; set; }

        [JsonProperty("tool_name", NullValueHandling = NullValueHandling.Ignore)]
        public string ToolName { get; set; }
    }

    /// <summary>
    /// AgentSession 序列化用的工具调用结构。
    /// </summary>
    public class SessionToolCall
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("function_name")]
        public string FunctionName { get; set; }

        [JsonProperty("arguments")]
        public string ArgumentsJson { get; set; }
    }
#pragma warning restore MEAI001
}
