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
    /// 主对话 Agent — 继承 DelegatingAIAgent，包裹内部 ChatClientAgent 以走 AF 管线，
    /// 同时提供自定义流式响应（工具执行通知、调试日志、STA 线程封送）。
    /// 
    /// 管线职责分配（由内部 ChatClientAgent 驱动）：
    /// - FuXingHistoryProvider: 通过 ProvideChatHistoryAsync / StoreChatHistoryAsync 管理历史
    /// - FileAgentSkillsProvider: 注入 load_skill / read_skill_resource 工具
    /// - ChatOptions.Tools: 注册 ToolRegistry 中的所有工具
    /// 
    /// 本类职责：
    /// - RunCoreStreamingAsync: 转发给内部 Agent 管线，拦截流式输出以发射工具执行通知
    /// - ToolInvocationScope: STA 线程封送，让工具操作在 Word COM 主线程执行
    /// </summary>
    public class MainAgent : DelegatingAIAgent
    {
        private readonly FuXingHistoryProvider _historyProvider;
        private readonly int _maxToolRounds;
        private string _responseId;

        public MainAgent(ChatClientAgent innerAgent,
            FuXingHistoryProvider historyProvider, int maxToolRounds = 50)
            : base(innerAgent)
        {
            _historyProvider = historyProvider;
            _maxToolRounds = maxToolRounds;
        }

        // ═══════════════════════════════════════════════════════════════
        //  流式对话循环 — 转发给内部 ChatClientAgent 管线，拦截工具通知
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
                Action<WorkflowProgressEvent> previousWorkflowReporter = fxOpts?.ReportWorkflowProgress;

                // 构建内部 ChatClientAgent 的运行选项
                var innerRunOptions = new ChatClientAgentRunOptions(fxOpts?.InnerChatOptions);

                var contentBuilder = new StringBuilder();
                var toolCalls = new List<FunctionCallContent>();
                var approvalRequests = new List<FunctionApprovalRequestContent>();
                var runningToolCalls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string finishReason = null;

                try
                {
                    var messageList = messages.ToList();

                    // 如果有 Instructions（系统提示词），添加到消息列表最前面
                    if (fxOpts?.InnerChatOptions?.Instructions != null)
                    {
                        var systemMsg = new ChatMessage(Microsoft.Extensions.AI.ChatRole.System, fxOpts.InnerChatOptions.Instructions);
                        messageList.Insert(0, systemMsg);
                    }

                    var messagesJson = JsonSerializer.Serialize(messageList, new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    });

                    // 记录工具定义
                    string toolsJson = "[]";
                    if (innerRunOptions?.ChatOptions?.Tools != null && innerRunOptions.ChatOptions.Tools.Count > 0)
                    {
                        toolsJson = JsonSerializer.Serialize(innerRunOptions.ChatOptions.Tools, new JsonSerializerOptions
                        {
                            WriteIndented = true,
                            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                        });
                    }

                    DebugLogger.Instance.LogLlmRequest(
                        InnerAgent?.ToString() ?? "未知",
                        Name ?? "MainAgent",
                        messageList.Count,
                        messagesJson,
                        $"MaxToolRounds={_maxToolRounds}\nTools:\n{toolsJson}");
                }
                catch (Exception logEx)
                {
                    DebugLogger.Instance.LogDebug("MainAgent", $"记录请求日志失败: {logEx.Message}");
                }

                try
                {
                    if (fxOpts != null)
                    {
                        fxOpts.ReportWorkflowProgress = evt =>
                        {
                            previousWorkflowReporter?.Invoke(evt);
                            writer.TryWrite(MakeWorkflowUpdate(evt));
                        };
                    }

                    using (ToolInvocationScope.Enter(fxOpts))
                    {
                        // 转发给内部 ChatClientAgent — 完整管线：
                        // 1. PrepareSessionAndMessages → FuXingHistoryProvider 注入历史
                        // 2. FileAgentSkillsProvider 注入技能工具
                        // 3. ChatClientAgent.RunCoreStreamingAsync → FunctionInvokingChatClient → LLM
                        // 4. StoreChatHistoryAsync → 保存对话到会话状态
                        await foreach (var update in ((ChatClientAgent)InnerAgent).RunStreamingAsync(
                            messages, session, innerRunOptions, cancellationToken).ConfigureAwait(false))
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            if (update.Text != null)
                            {
                                contentBuilder.Append(update.Text);
                                writer.TryWrite(MakeTextUpdate(update.Text));
                            }

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
                                                ? JsonSerializer.Serialize(fc.Arguments, new JsonSerializerOptions
                                                {
                                                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                                                })
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

                    if (toolCalls.Count > 0)
                        DebugLogger.Instance.LogAssistantToolCallMessage(content, toolCalls);
                    else if (!string.IsNullOrWhiteSpace(content))
                        DebugLogger.Instance.LogAssistantMessage(content);

                    if (string.IsNullOrWhiteSpace(content) && approvalRequests.Count == 0)
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
                finally
                {
                    if (fxOpts != null)
                        fxOpts.ReportWorkflowProgress = previousWorkflowReporter;
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
            var client = ((ChatClientAgent)InnerAgent).ChatClient;
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
            => _historyProvider.ExportMessages(session);

        public int EstimateSessionTokens(AgentSession session)
            => _historyProvider.EstimateTotalTokens(session);

        // ═══════════════════════════════════════════════════════════════
        //  内部辅助
        // ═══════════════════════════════════════════════════════════════

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

        private AgentResponseUpdate MakeWorkflowUpdate(WorkflowProgressEvent progressEvent)
        {
            AIContent content;

            switch (progressEvent.Kind)
            {
                case WorkflowProgressKind.WorkflowStarted:
                    content = new WorkflowExecutionStartContent(
                        progressEvent.WorkflowName,
                        progressEvent.WorkflowDisplayName,
                        progressEvent.TotalSteps);
                    break;
                case WorkflowProgressKind.StepStarted:
                    content = new WorkflowStepUpdateContent(
                        progressEvent.WorkflowName,
                        progressEvent.StepIndex,
                        progressEvent.TotalSteps,
                        progressEvent.StepName,
                        progressEvent.Description,
                        false,
                        true);
                    break;
                case WorkflowProgressKind.StepFinished:
                    content = new WorkflowStepUpdateContent(
                        progressEvent.WorkflowName,
                        progressEvent.StepIndex,
                        progressEvent.TotalSteps,
                        progressEvent.StepName,
                        progressEvent.Description,
                        true,
                        progressEvent.Success ?? true);
                    break;
                case WorkflowProgressKind.WorkflowFinished:
                    content = new WorkflowExecutionEndContent(
                        progressEvent.WorkflowName,
                        progressEvent.WorkflowDisplayName,
                        progressEvent.TotalSteps,
                        progressEvent.Success ?? true,
                        progressEvent.Description);
                    break;
                default:
                    content = new AgentErrorContent("未知 workflow 事件");
                    break;
            }

            return new AgentResponseUpdate
            {
                AgentId = this.Id,
                AuthorName = this.Name,
                Role = ChatRole.Assistant,
                ResponseId = _responseId,
                Contents = new List<AIContent> { content }
            };
        }

    }
#pragma warning restore MEAI001
}
