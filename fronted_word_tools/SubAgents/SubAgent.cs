using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenAI;
using OpenAI.Chat;
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FuXing.SubAgents
{
    // ═══════════════════════════════════════════════════════════════
    //  子智能体引擎（动态编排模式）
    //
    //  核心设计理念：
    //  1. 上下文独立 — 每个 SubAgent 拥有自己的 ChatMemory，不污染主 Agent
    //  2. 动态定义   — SystemPrompt / Task / 工具白名单全由主 Agent 动态生成
    //  3. 工具白名单 — 可选地复用主插件的工具（按名称白名单过滤），通过委托执行
    //  4. 结果返回   — 完成后将结果作为 tool_result 回到主 Agent 上下文
    //  5. 无 UI      — 子智能体不产生流式输出，不直接操作界面
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 子智能体引擎。
    /// 每次调用 <see cref="RunAsync"/> 创建完全独立的上下文，
    /// 执行一次完整的 LLM 对话循环（支持工具调用多轮），最后返回结果。
    /// </summary>
    public class SubAgent
    {
        /// <summary>
        /// 执行子智能体任务。
        /// 当 request 中包含 ToolDefinitions + ToolExecutor 时支持工具调用；
        /// 否则为纯推理模式（单轮 LLM 调用）。
        /// 注意：如果提供了 ToolExecutor 且工具涉及 Word COM，
        /// 则本方法必须在 STA/UI 线程上调用（不要用 Task.Run 包装）。
        /// </summary>
        public async Task<SubAgentResult> RunAsync(
            SubAgentRequest request,
            CancellationToken cancellation = default,
            ISubAgentProgress progress = null)
        {
            // ── 读取配置 ──
            var config = new ConfigLoader().LoadConfig();
            string baseUrl = (config.BaseURL ?? "http://127.0.0.1:8000").TrimEnd('/');
            string apiKey = config.ApiKey ?? "";
            string modelName = (config.ModelName ?? "").Trim();

            if (string.IsNullOrWhiteSpace(modelName))
                return SubAgentResult.Fail("未配置模型名称");

            string agentLabel = request.AgentName ?? "SubAgent";

            // ── 构建独立上下文 ──
            var memory = new ChatMemory();
            memory.SetSystemPrompt(BuildSystemPrompt(request));
            memory.AddUserMessage(BuildUserMessage(request));

            bool hasTools = request.ToolDefinitions != null
                         && request.ToolDefinitions.Count > 0
                         && request.ToolExecutor != null;

            // ── 对话循环（支持工具调用多轮） ──
            int rounds = 0;
            string finalResponse = "";

            for (int round = 0; round < request.MaxRounds; round++)
            {
                cancellation.ThrowIfCancellationRequested();
                rounds++;

                Debug.WriteLine($"[{agentLabel}] Round {round + 1}/{request.MaxRounds}");
                DebugLogger.Instance.LogRoundStart(round, request.MaxRounds);
                progress?.OnLlmCallStart();

                var llm = await CallLlmAsync(
                    baseUrl, apiKey, modelName, memory,
                    hasTools ? request.ToolDefinitions : null,
                    cancellation);

                if (llm == null)
                {
                    DebugLogger.Instance.LogError(agentLabel, "LLM 请求失败");
                    progress?.OnComplete(false, "LLM 请求失败");
                    return SubAgentResult.Fail("LLM 请求失败");
                }

                // 记录子智能体每轮完整响应
                DebugLogger.Instance.LogSubAgentRound(
                    agentLabel, round, request.MaxRounds,
                    llm.Content, llm.ToolCalls?.Count ?? 0, llm.FinishReason);

                // 报告 LLM 的思考/推理内容
                if (!string.IsNullOrEmpty(llm.Content))
                    progress?.OnThinking(llm.Content);

                // ── 工具调用分支 ──
                if (llm.HasToolCalls && hasTools)
                {
                    memory.AddAssistantMessage(llm.Content, llm.ToolCalls);

                    foreach (var tc in llm.ToolCalls)
                    {
                        Debug.WriteLine($"[{agentLabel}] 调用工具: {tc.FunctionName} (id={tc.Id})");
                        DebugLogger.Instance.LogToolCall(tc.FunctionName, tc.Id, tc.Arguments);
                        progress?.OnToolCallStart(tc.FunctionName);

                        var toolResult = await request.ToolExecutor(tc.FunctionName, tc.Arguments);
                        memory.AddToolResult(tc.Id, tc.FunctionName, toolResult.Output);

                        Debug.WriteLine($"[{agentLabel}] 工具结果: success={toolResult.Success}, len={toolResult.Output?.Length}");
                        DebugLogger.Instance.LogToolResult(tc.FunctionName, tc.Id, toolResult.Success, toolResult.Output);
                        progress?.OnToolCallEnd(tc.FunctionName, toolResult.Success, toolResult.Output);
                    }

                    continue; // 带着工具结果进入下一轮
                }

                // ── 纯文本响应 ──
                finalResponse = llm.Content ?? "";
                memory.AddAssistantMessage(finalResponse);
                break;
            }

            if (rounds >= request.MaxRounds && string.IsNullOrEmpty(finalResponse))
                finalResponse = "(子智能体达到最大轮次限制)";

            progress?.OnComplete(true, finalResponse);

            int estimatedTokens = ChatMemory.EstimateTokens(memory.History);
            return SubAgentResult.Ok(finalResponse, rounds, estimatedTokens);
        }

        // ═══════════════════════════════════════════════════════════════
        //  Prompt 构建（动态模式：直接使用主 Agent 生成的 Prompt）
        // ═══════════════════════════════════════════════════════════════

        private static string BuildSystemPrompt(SubAgentRequest request)
        {
            var sb = new StringBuilder();

            // 使用主 Agent 动态生成的 SystemPrompt
            sb.AppendLine(request.SystemPrompt);

            // 追加通用底线规则（防止子智能体越权）
            sb.AppendLine();
            sb.AppendLine("通用规则（不可违反）：");
            sb.AppendLine("- 你是一个子智能体，只负责完成当前这一个任务，不要试图与用户对话。");
            sb.AppendLine("- 直接输出最终分析结果，不要描述你的工具调用过程或思考步骤。");
            sb.AppendLine("- 如果数据不足且有可用工具，使用工具获取更多信息后再得出结论。");
            sb.AppendLine("- 必须用中文回复。");

            return sb.ToString();
        }

        private static string BuildUserMessage(SubAgentRequest request)
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrEmpty(request.DocumentContext))
            {
                sb.AppendLine("<document_context>");
                sb.AppendLine(request.DocumentContext);
                sb.AppendLine("</document_context>");
                sb.AppendLine();
            }

            sb.AppendLine("<task>");
            sb.AppendLine(request.TaskInstruction);
            sb.AppendLine("</task>");

            return sb.ToString();
        }

        // ═══════════════════════════════════════════════════════════════
        //  LLM 调用（非流式 + tool_calls 解析）
        // ═══════════════════════════════════════════════════════════════

        private static async Task<LlmResponse> CallLlmAsync(
            string baseUrl,
            string apiKey,
            string modelName,
            ChatMemory memory,
            JArray tools,
            CancellationToken cancellation)
        {
            try
            {
                var requestLogObj = new JObject
                {
                    ["model"] = modelName,
                    ["messages"] = memory.BuildMessagesJson(),
                    ["stream"] = false,
                    ["temperature"] = 0.3
                };
                if (tools != null && tools.Count > 0)
                    requestLogObj["tools"] = tools;

                DebugLogger.Instance.LogLlmRequest("OpenAI.ChatClient.CompleteChatAsync", requestLogObj.ToString(Formatting.None));

                var options = new OpenAIClientOptions { Endpoint = new Uri(baseUrl) };
                var openaiClient = new OpenAIClient(new ApiKeyCredential(apiKey ?? "dummy"), options);
                var chatClient = openaiClient.GetChatClient(modelName);

                var messages = new List<ChatMessage>();
                foreach (var msg in memory.PrepareMessages())
                {
                    if (msg.Role == ChatMessageRole.System)
                        messages.Add(new SystemChatMessage(msg.Content ?? string.Empty));
                    else if (msg.Role == ChatMessageRole.User)
                        messages.Add(new UserChatMessage(msg.Content ?? string.Empty));
                    else if (msg.Role == ChatMessageRole.Assistant)
                    {
                        if (msg.ToolCalls != null && msg.ToolCalls.Count > 0)
                        {
                            var toolCallsParam = new List<ChatToolCall>();
                            foreach (var tc in msg.ToolCalls)
                            {
                                toolCallsParam.Add(ChatToolCall.CreateFunctionToolCall(
                                    tc.Id,
                                    tc.FunctionName,
                                    BinaryData.FromString(tc.Arguments?.ToString(Formatting.None) ?? "{}")));
                            }
                            messages.Add(new AssistantChatMessage(toolCallsParam));
                        }
                        else
                        {
                            messages.Add(new AssistantChatMessage(msg.Content ?? string.Empty));
                        }
                    }
                    else if (msg.Role == ChatMessageRole.Tool)
                    {
                        messages.Add(new ToolChatMessage(msg.ToolCallId, msg.Content ?? string.Empty));
                    }
                }

                var chatOptions = new ChatCompletionOptions { Temperature = 0.3f };
                if (tools != null)
                {
                    foreach (JToken tToken in tools)
                    {
                        if (!(tToken is JObject t)) continue;
                        string name = t["function"]?["name"]?.ToString() ?? "";
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        string desc = t["function"]?["description"]?.ToString() ?? "";
                        string paramStr = t["function"]?["parameters"]?.ToString(Formatting.None) ?? "{}";
                        chatOptions.Tools.Add(ChatTool.CreateFunctionTool(name, desc, BinaryData.FromString(paramStr)));
                    }
                }

                var completionResult = await chatClient.CompleteChatAsync(messages, chatOptions, cancellation);
                var completion = completionResult.Value;

                var parsed = new LlmResponse
                {
                    Content = ExtractTextFromCompletion(completion),
                    FinishReason = completion.FinishReason.ToString(),
                    ToolCalls = new List<ToolCallRequest>()
                };

                if (completion.ToolCalls != null)
                {
                    foreach (var tc in completion.ToolCalls)
                    {
                        string argsStr = tc.FunctionArguments?.ToString() ?? "{}";
                        JObject argsObj;
                        try { argsObj = JObject.Parse(argsStr); }
                        catch { argsObj = new JObject(); }

                        parsed.ToolCalls.Add(new ToolCallRequest
                        {
                            Id = tc.Id ?? Guid.NewGuid().ToString(),
                            FunctionName = tc.FunctionName,
                            Arguments = argsObj
                        });
                    }
                }

                var responseLogObj = new JObject
                {
                    ["finish_reason"] = parsed.FinishReason,
                    ["content"] = parsed.Content ?? string.Empty,
                    ["tool_calls"] = new JArray(parsed.ToolCalls.ConvertAll(tc => new JObject
                    {
                        ["id"] = tc.Id,
                        ["name"] = tc.FunctionName,
                        ["arguments"] = tc.Arguments?.ToString(Formatting.None) ?? "{}"
                    }))
                };
                DebugLogger.Instance.LogLlmResponse(responseLogObj.ToString(Formatting.None));

                Debug.WriteLine($"[SubAgent] 响应: content_len={parsed.Content?.Length ?? 0}, tool_calls={parsed.ToolCalls.Count}, finish={parsed.FinishReason}");
                return parsed;
            }
            catch (OperationCanceledException)
            {
                throw; // 让取消正常冒泡
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SubAgent] 请求异常: {ex.GetType().Name}: {ex.Message}");
                DebugLogger.Instance.LogError("SubAgent.CallLlmAsync", ex);
                return null;
            }
        }

        private static string ExtractTextFromCompletion(ChatCompletion completion)
        {
            if (completion == null || completion.Content == null)
                return string.Empty;

            var sb = new StringBuilder();
            foreach (var part in completion.Content)
            {
                if (!string.IsNullOrEmpty(part.Text))
                {
                    sb.Append(part.Text);
                }
            }
            return sb.ToString();
        }
    }
}
