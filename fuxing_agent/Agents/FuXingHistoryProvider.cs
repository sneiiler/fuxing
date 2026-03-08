using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using FuXingAgent.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FuXingAgent.Agents
{
#pragma warning disable MEAI001
    /// <summary>
    /// 会话历史管理 -- 继承 ChatHistoryProvider，作为 AIContextProvider 参与 Agent 管线。
    /// ProvideChatHistoryAsync 在每次 Agent 调用前自动提供历史上下文（含令牌截断）。
    /// 历史通过 ProviderSessionState 存储在 AgentSession 中，随会话自动序列化。
    /// </summary>
    public sealed class FuXingHistoryProvider : ChatHistoryProvider
    {
        private const int MaxAllowedTokens = 98000;
        private readonly ProviderSessionState<HistoryState> _sessionState;

        public FuXingHistoryProvider() : base(null, null, null)
        {
            _sessionState = new ProviderSessionState<HistoryState>(
                _ => new HistoryState(),
                nameof(FuXingHistoryProvider));
        }

        public override IReadOnlyList<string> StateKeys => new[] { _sessionState.StateKey };

        // ═══════════════════════════════════════════════════════════════
        //  Pipeline 方法 — 由 ChatClientAgent 在管线中自动调用
        // ═══════════════════════════════════════════════════════════════

        /// <summary>在 Agent 调用前提供历史消息（自动截断）</summary>
        protected override ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
            InvokingContext context, CancellationToken cancellationToken)
        {
            var state = _sessionState.GetOrInitializeState(context.Session);
            var chatMessages = ImportToChatMessages(state.Messages);

            IEnumerable<ChatMessage> result = NeedsCompaction(chatMessages)
                ? TruncateHistory(chatMessages)
                : chatMessages;

            return new ValueTask<IEnumerable<ChatMessage>>(result);
        }

        /// <summary>在 Agent 调用后保存对话到会话状态</summary>
        protected override ValueTask StoreChatHistoryAsync(
            InvokedContext context, CancellationToken cancellationToken)
        {
            var state = _sessionState.GetOrInitializeState(context.Session);
            state.Messages.Clear();

            foreach (var msg in context.RequestMessages)
            {
                if (msg.Role == ChatRole.System) continue;
                state.Messages.Add(ChatMessageToSessionMessage(msg));
            }

            if (context.ResponseMessages != null)
            {
                foreach (var msg in context.ResponseMessages)
                    state.Messages.Add(ChatMessageToSessionMessage(msg));
            }

            _sessionState.SaveState(context.Session, state);
            return default;
        }

        /// <summary>导出为 SessionMessage 列表（用于 UI 显示）</summary>
        public List<SessionMessage> ExportMessages(AgentSession session)
        {
            return _sessionState.GetOrInitializeState(session).Messages;
        }

        public int EstimateTotalTokens(AgentSession session)
        {
            var state = _sessionState.GetOrInitializeState(session);
            int chars = 0;
            foreach (var sm in state.Messages)
                chars += EstimateSessionMessageChars(sm);
            return (int)(chars / 2.5);
        }

        // ═══════════════════════════════════════════════════════════════
        //  SessionMessage <-> ChatMessage 转换
        // ═══════════════════════════════════════════════════════════════

        private static List<ChatMessage> ImportToChatMessages(List<SessionMessage> messages)
        {
            var result = new List<ChatMessage>();
            if (messages == null) return result;

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
                            try { args = JsonSerializer.Deserialize<Dictionary<string, object>>(tc.ArgumentsJson); }
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

                result.Add(msg);
            }

            return result;
        }

        private static SessionMessage ChatMessageToSessionMessage(ChatMessage msg)
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
                        ? JsonSerializer.Serialize(tc.Arguments, new JsonSerializerOptions
                        {
                            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                        }) : "{}"
                }).ToList();
            }

            var toolResult = msg.Contents.OfType<FunctionResultContent>().FirstOrDefault();
            if (toolResult != null)
            {
                sm.ToolCallId = toolResult.CallId;
                sm.Content = toolResult.Result?.ToString();
            }

            return sm;
        }

        // ═══════════════════════════════════════════════════════════════
        //  内部实现
        // ═══════════════════════════════════════════════════════════════

        private static bool NeedsCompaction(List<ChatMessage> history)
        {
            int chars = 0;
            foreach (var msg in history)
                chars += EstimateMessageChars(msg);
            return (int)(chars / 2.5) > MaxAllowedTokens * 0.8;
        }

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
                        chars += JsonSerializer.Serialize(fc.Arguments).Length;
                }
                else if (c is FunctionResultContent fr)
                    chars += fr.Result?.ToString()?.Length ?? 0;
            }
            return chars;
        }

        private static int EstimateSessionMessageChars(SessionMessage sm)
        {
            int chars = sm.Content?.Length ?? 0;
            if (sm.ToolCalls != null)
                foreach (var tc in sm.ToolCalls)
                    chars += (tc.FunctionName?.Length ?? 0) + (tc.ArgumentsJson?.Length ?? 0);
            return chars;
        }

        private static List<ChatMessage> TruncateHistory(List<ChatMessage> history)
        {
            if (history.Count <= 6)
                return new List<ChatMessage>(history);

            int headEnd = FindFirstCompleteRoundEnd(history);
            int keepFromEnd = history.Count / 2;
            int cutEnd = history.Count - keepFromEnd;

            while (cutEnd < history.Count && history[cutEnd].Role == ChatRole.Tool)
                cutEnd++;

            if (cutEnd > 0 && cutEnd < history.Count)
            {
                var prev = history[cutEnd - 1];
                if (prev.Role == ChatRole.Assistant && prev.Contents.OfType<FunctionCallContent>().Any())
                    cutEnd--;
            }

            if (cutEnd <= headEnd)
                cutEnd = headEnd + 1;

            var result = new List<ChatMessage>();
            for (int i = 0; i <= headEnd && i < history.Count; i++)
                result.Add(history[i]);

            result.Add(new ChatMessage(ChatRole.User,
                "[系统提示: 为节省上下文空间，已截断较早的对话历史。请基于剩余上下文继续。]"));
            result.Add(new ChatMessage(ChatRole.Assistant,
                "好的，我将基于当前可用的上下文继续。"));

            for (int i = cutEnd; i < history.Count; i++)
                result.Add(history[i]);

            return result;
        }

        private static int FindFirstCompleteRoundEnd(List<ChatMessage> history)
        {
            int i = 0;
            if (i < history.Count && history[i].Role == ChatRole.User)
                i++;
            else
                return 0;

            if (i < history.Count && history[i].Role == ChatRole.Assistant)
            {
                if (history[i].Contents.OfType<FunctionCallContent>().Any())
                {
                    i++;
                    while (i < history.Count && history[i].Role == ChatRole.Tool)
                        i++;
                    if (i < history.Count && history[i].Role == ChatRole.Assistant)
                        return i;
                    return i - 1;
                }
                return i;
            }
            return 0;
        }

        /// <summary>历史状态，通过 ProviderSessionState 随 AgentSession 自动序列化</summary>
        public class HistoryState
        {
            public List<SessionMessage> Messages { get; set; } = new List<SessionMessage>();
        }
    }
#pragma warning restore MEAI001
}
