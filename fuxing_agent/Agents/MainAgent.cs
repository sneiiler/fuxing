using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using FuXingAgent.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace FuXingAgent.Agents
{
#pragma warning disable MEAI001
    /// <summary>
    /// 主对话 Agent — 继承 AIAgent 基类，驱动用户与 LLM 之间的完整对话循环，
    /// 包含流式响应、工具调用、截断续传、STA 线程封送。
    /// </summary>
    public class MainAgent : AIAgent
    {
        private readonly AgentBootstrap _bootstrap;
        private readonly ToolRegistry _toolRegistry;
        private readonly int _maxToolRounds;
        private string _responseId;

        public MainAgent(AgentBootstrap bootstrap, ToolRegistry toolRegistry, int maxToolRounds = 50)
        {
            _bootstrap = bootstrap;
            _toolRegistry = toolRegistry;
            _maxToolRounds = maxToolRounds;
        }

        // ═══════════════════════════════════════════════════════════════
        //  Session 工厂（使用 AgentSession 承载会话历史）
        // ═══════════════════════════════════════════════════════════════

        protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken)
            => new ValueTask<AgentSession>(new FuXingSession());

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
            JsonElement serializedSession, JsonSerializerOptions jsonSerializerOptions,
            CancellationToken cancellationToken)
        {
            var session = new FuXingSession();

            if (serializedSession.ValueKind == JsonValueKind.Object &&
                serializedSession.TryGetProperty("messages", out var messagesElement) &&
                messagesElement.ValueKind == JsonValueKind.Array)
            {
                try
                {
                    var stored = JsonSerializer.Deserialize<List<SessionMessage>>(
                        messagesElement.GetRawText(),
                        jsonSerializerOptions) ?? new List<SessionMessage>();

                    session.State.ImportMessages(stored);
                }
                catch (Exception ex)
                {
                    DebugLogger.Instance.LogError("MainAgent.DeserializeSessionCoreAsync", ex);
                }
            }

            return new ValueTask<AgentSession>(session);
        }

        /// <summary>AgentSession 是抽象类，此为最小具体实现</summary>
        private sealed class FuXingSession : AgentSession
        {
            public ConversationState State { get; } = new ConversationState();
        }

        protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
            AgentSession session, JsonSerializerOptions jsonSerializerOptions,
            CancellationToken cancellationToken)
        {
            var fxSession = session as FuXingSession;
            var payload = new
            {
                messages = fxSession?.State?.ExportMessages() ?? new List<SessionMessage>()
            };

            return new ValueTask<JsonElement>(
                JsonSerializer.SerializeToElement(payload, jsonSerializerOptions));
        }

        // ═══════════════════════════════════════════════════════════════
        //  流式对话循环（主入口）— 使用 Channel 避免 yield-in-try-catch 限制
        // ═══════════════════════════════════════════════════════════════

        protected override IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession session,
            AgentRunOptions options,
            CancellationToken cancellationToken)
        {
            var channel = Channel.CreateUnbounded<AgentResponseUpdate>();
            _ = ProduceStreamingUpdatesAsync(channel.Writer, messages, session, options, cancellationToken);
            return channel.Reader.ReadAllAsync(cancellationToken);
        }

        /// <summary>
        /// 实际的流式对话生产者——在后台 Task 中运行，通过 ChannelWriter 推送更新。
        /// 主执行链使用 FunctionInvokingChatClient 自动处理工具调用循环。
        /// </summary>
        private async Task ProduceStreamingUpdatesAsync(
            ChannelWriter<AgentResponseUpdate> writer,
            IEnumerable<ChatMessage> messages,
            AgentSession session,
            AgentRunOptions options,
            CancellationToken cancellationToken)
        {
            try
            {
                var fxOpts = options as FuXingRunOptions;
                _responseId = Guid.NewGuid().ToString();

                var innerClient = _bootstrap.ChatClient;
                if (innerClient == null)
                {
                    writer.TryWrite(MakeErrorUpdate("Agent 未初始化，请检查配置"));
                    return;
                }

                var fxSession = session as FuXingSession ?? new FuXingSession();
                var sessionState = fxSession.State;

                foreach (var input in messages ?? Array.Empty<ChatMessage>())
                {
                    if (input?.Role == ChatRole.System) continue;
                    sessionState.AddMessage(CloneMessage(input));
                }

                var roundMessages = sessionState.PrepareMessages(fxOpts?.SystemPrompt ?? string.Empty);

                var chatOptions = BuildChatOptions();
                var contentBuilder = new StringBuilder();
                var toolCalls = new List<FunctionCallContent>();
                var approvalRequests = new List<FunctionApprovalRequestContent>();
                var runningToolCalls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string finishReason = null;

                try
                {
                    var messagesJson = JsonSerializer.Serialize(roundMessages, new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    });

                    var optionsJson = JsonSerializer.Serialize(new
                    {
                        Temperature = chatOptions.Temperature,
                        ToolCount = chatOptions.Tools?.Count ?? 0,
                        MaxToolRounds = _maxToolRounds
                    }, new JsonSerializerOptions { WriteIndented = true });

                    DebugLogger.Instance.LogLlmRequest(
                        _bootstrap.ChatClient?.ToString() ?? "未知",
                        _bootstrap.CurrentModel,
                        roundMessages.Count,
                        messagesJson,
                        optionsJson);
                }
                catch (Exception logEx)
                {
                    DebugLogger.Instance.LogDebug("MainAgent", $"记录请求日志失败: {logEx.Message}");
                }

                try
                {
                    using (ToolInvocationScope.Enter(fxOpts))
                    using (var funcClient = new FunctionInvokingChatClient(innerClient))
                    {
                        funcClient.MaximumIterationsPerRequest = _maxToolRounds;

                        await foreach (var update in funcClient.GetStreamingResponseAsync(
                            roundMessages, chatOptions, cancellationToken).ConfigureAwait(false))
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            if (update.Text != null)
                            {
                                contentBuilder.Append(update.Text);
                                writer.TryWrite(MakeTextUpdate(update.Text));
                            }

                            if (update.FinishReason != null)
                                finishReason = update.FinishReason.Value.ToString();

                            if (update.Contents != null)
                            {
                                foreach (var aiContent in update.Contents)
                                {
                                    if (aiContent is FunctionCallContent fc)
                                    {
                                        bool seen = toolCalls.Exists(x => x.CallId == fc.CallId);
                                        if (!seen)
                                            toolCalls.Add(fc);

                                        if (!runningToolCalls.ContainsKey(fc.CallId))
                                        {
                                            runningToolCalls[fc.CallId] = fc.Name;
                                            writer.TryWrite(MakeToolStartUpdate(fc.Name));

                                            string argsJson = fc.Arguments != null
                                                ? JsonSerializer.Serialize(fc.Arguments)
                                                : "{}";
                                            DebugLogger.Instance.LogToolCall(fc.Name, fc.CallId, argsJson);
                                        }
                                    }
                                    else if (aiContent is FunctionResultContent fr)
                                    {
                                        string toolName;
                                        if (!runningToolCalls.TryGetValue(fr.CallId, out toolName))
                                            toolName = "unknown_tool";

                                        string resultText = fr.Result?.ToString() ?? "";
                                        bool success = !resultText.StartsWith("错误:", StringComparison.OrdinalIgnoreCase);

                                        writer.TryWrite(MakeToolEndUpdate(toolName, success));
                                        DebugLogger.Instance.LogToolResult(toolName, fr.CallId, success, resultText);
                                        sessionState.AddToolResult(fr.CallId, resultText);

                                        runningToolCalls.Remove(fr.CallId);
                                    }
                                    else if (aiContent is FunctionApprovalRequestContent approval)
                                    {
                                        approvalRequests.Add(approval);
                                        writer.TryWrite(MakeApprovalRequestUpdate(approval));
                                    }
                                }
                            }
                        }
                    }

                    string content = contentBuilder.ToString();

                    DebugLogger.Instance.LogLlmStreamResponse(content, toolCalls.Count, finishReason);

                    if (approvalRequests.Count > 0)
                    {
                        var approvalMessage = new ChatMessage { Role = ChatRole.Assistant };
                        if (!string.IsNullOrEmpty(content))
                            approvalMessage.Contents.Add(new TextContent(content));
                        foreach (var approval in approvalRequests)
                            approvalMessage.Contents.Add(approval);

                        sessionState.AddMessage(approvalMessage);
                    }
                    else
                    {
                        if (toolCalls.Count > 0)
                        {
                            sessionState.AddAssistantToolCallMessage(content, toolCalls);
                            DebugLogger.Instance.LogAssistantToolCallMessage(content, toolCalls);
                        }
                        else
                        {
                            sessionState.AddAssistantMessage(content);
                            DebugLogger.Instance.LogAssistantMessage(content);
                        }
                    }

                    if (string.IsNullOrWhiteSpace(content))
                        writer.TryWrite(MakeErrorUpdate("未收到模型响应，请检查模型服务或重试"));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    DebugLogger.Instance.LogError("MainAgent.RunCoreStreamingAsync", ex);
                    DebugLogger.Instance.LogLlmError("MainAgent.RunCoreStreamingAsync", ex);
                    writer.TryWrite(MakeErrorUpdate($"对话请求失败: {ex.Message}"));
                }
            }
            catch (OperationCanceledException)
            {
                // 用户取消，静默结束
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("MainAgent.ProduceStreamingUpdatesAsync", ex);
                writer.TryWrite(MakeErrorUpdate($"内部错误: {ex.Message}"));
            }
            finally
            {
                writer.Complete();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  非流式对话（框架要求的 override）
        // ═══════════════════════════════════════════════════════════════

        protected override async Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession session,
            AgentRunOptions options,
            CancellationToken cancellationToken)
        {
            var allText = new StringBuilder();

            await foreach (var update in RunCoreStreamingAsync(
                messages, session, options, cancellationToken))
            {
                if (update.Text != null)
                    allText.Append(update.Text);
            }

            var responseMessages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.Assistant, allText.ToString())
            };

            return new AgentResponse
            {
                AgentId = this.Id,
                ResponseId = Guid.NewGuid().ToString(),
                Messages = responseMessages
            };
        }

        // ═══════════════════════════════════════════════════════════════
        //  标题生成（独立于 Agent 生命周期，不走工具循环）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>非流式调用，生成简短标题</summary>
        public async Task<string> GenerateTitleAsync(
            string conversationSummary,
            CancellationToken cancellationToken = default)
        {
            var client = _bootstrap.ChatClient;
            if (client == null) return null;

            try
            {
                var msgs = new List<ChatMessage>
                {
                    new ChatMessage(ChatRole.System,
                        "用5-10个中文字概括以下对话的主题。直接输出标题文本，不要加引号、序号或任何解释。"),
                    new ChatMessage(ChatRole.User, conversationSummary)
                };

                var opts = new ChatOptions { Temperature = 0.3f };
                var completion = await client.GetResponseAsync(msgs, opts, cancellationToken)
                    .ConfigureAwait(false);
                string title = completion?.Text?.Trim();

                if (string.IsNullOrWhiteSpace(title)) return null;
                title = Regex.Replace(title, @"<think>[\s\S]*?</think>", "", RegexOptions.IgnoreCase).Trim();
                if (string.IsNullOrWhiteSpace(title)) return null;
                if (title.Length > 20) title = title.Substring(0, 20);
                return title;
            }
            catch { return null; }
        }

        public List<SessionMessage> ExportSessionMessages(AgentSession session)
        {
            var fxSession = session as FuXingSession;
            return fxSession?.State?.ExportMessages() ?? new List<SessionMessage>();
        }

        public int EstimateSessionTokens(AgentSession session)
        {
            var fxSession = session as FuXingSession;
            return fxSession?.State?.EstimateTotalTokens() ?? 0;
        }

        // ═══════════════════════════════════════════════════════════════
        //  内部辅助
        // ═══════════════════════════════════════════════════════════════

        private ChatOptions BuildChatOptions()
        {
            var opts = new ChatOptions { Temperature = 0.7f };

            var tools = _toolRegistry.GetAllTools();
            if (tools != null && tools.Count > 0)
                opts.Tools = tools;

            return opts;
        }

        private AgentResponseUpdate MakeTextUpdate(string text)
        {
            return new AgentResponseUpdate
            {
                AgentId = this.Id,
                AuthorName = this.Name,
                Role = ChatRole.Assistant,
                ResponseId = _responseId,
                Contents = new List<AIContent> { new TextContent(text) }
            };
        }

        private AgentResponseUpdate MakeErrorUpdate(string message)
        {
            return new AgentResponseUpdate
            {
                AgentId = this.Id,
                AuthorName = this.Name,
                Role = ChatRole.Assistant,
                ResponseId = _responseId,
                Contents = new List<AIContent> { new AgentErrorContent(message) }
            };
        }

        private AgentResponseUpdate MakeToolStartUpdate(string toolName)
        {
            return new AgentResponseUpdate
            {
                AgentId = this.Id,
                AuthorName = this.Name,
                Role = ChatRole.Assistant,
                ResponseId = _responseId,
                Contents = new List<AIContent> { new ToolExecutionStartContent(toolName) }
            };
        }

        private AgentResponseUpdate MakeToolEndUpdate(string toolName, bool success)
        {
            return new AgentResponseUpdate
            {
                AgentId = this.Id,
                AuthorName = this.Name,
                Role = ChatRole.Assistant,
                ResponseId = _responseId,
                Contents = new List<AIContent> { new ToolExecutionEndContent(toolName, success) }
            };
        }

        private AgentResponseUpdate MakeApprovalRequestUpdate(FunctionApprovalRequestContent request)
        {
            return new AgentResponseUpdate
            {
                AgentId = this.Id,
                AuthorName = this.Name,
                Role = ChatRole.Assistant,
                ResponseId = _responseId,
                Contents = new List<AIContent> { request }
            };
        }

        private static ChatMessage CloneMessage(ChatMessage message)
        {
            if (message == null) return null;

            var clone = new ChatMessage { Role = message.Role };
            foreach (var content in message.Contents)
            {
                if (content is TextContent tc)
                    clone.Contents.Add(new TextContent(tc.Text));
                else if (content is FunctionCallContent fc)
                    clone.Contents.Add(new FunctionCallContent(fc.CallId, fc.Name, fc.Arguments));
                else if (content is FunctionResultContent fr)
                    clone.Contents.Add(new FunctionResultContent(fr.CallId, fr.Result));
            }

            return clone;
        }
    }
#pragma warning restore MEAI001
}
