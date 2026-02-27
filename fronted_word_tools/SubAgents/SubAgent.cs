using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FuXing.SubAgents
{
    // ═══════════════════════════════════════════════════════════════
    //  子智能体引擎
    //
    //  核心设计理念（参考 LangChain Deep Agents / Claude Code SubAgent）：
    //  1. 上下文独立 — 每个 SubAgent 拥有自己的 ChatMemory，不污染主 Agent
    //  2. 工具共用   — 可选地复用主插件的工具（按分类过滤），通过委托执行
    //  3. 结果返回   — 完成后将结果作为 tool_result 回到主 Agent 上下文
    //  4. 无 UI      — 子智能体不产生流式输出，不直接操作界面
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 子智能体引擎。
    /// 每次调用 <see cref="RunAsync"/> 创建完全独立的上下文，
    /// 执行一次完整的 LLM 对话循环（支持工具调用多轮），最后返回结果。
    /// </summary>
    public class SubAgent
    {
        private static readonly HttpClient _httpClient;

        static SubAgent()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(180);
        }

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

                Debug.WriteLine($"[SubAgent] Round {round + 1}/{request.MaxRounds}, task={request.Task}");
                progress?.OnRoundStart(round + 1, request.MaxRounds);

                var llm = await CallLlmAsync(
                    baseUrl, apiKey, modelName, memory,
                    hasTools ? request.ToolDefinitions : null,
                    cancellation);

                if (llm == null)
                {
                    progress?.OnComplete(false, "LLM 请求失败");
                    return SubAgentResult.Fail("LLM 请求失败");
                }

                // 报告 LLM 的思考/推理内容
                if (!string.IsNullOrEmpty(llm.Content))
                    progress?.OnThinking(llm.Content);

                // ── 工具调用分支 ──
                if (llm.HasToolCalls && hasTools)
                {
                    memory.AddAssistantMessage(llm.Content, llm.ToolCalls);

                    foreach (var tc in llm.ToolCalls)
                    {
                        Debug.WriteLine($"[SubAgent] 调用工具: {tc.FunctionName} (id={tc.Id})");
                        progress?.OnToolCallStart(tc.FunctionName);

                        var toolResult = await request.ToolExecutor(tc.FunctionName, tc.Arguments);
                        memory.AddToolResult(tc.Id, tc.FunctionName, toolResult.Output);

                        Debug.WriteLine($"[SubAgent] 工具结果: success={toolResult.Success}, len={toolResult.Output?.Length}");
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
        //  System Prompt 构建
        // ═══════════════════════════════════════════════════════════════

        private static string BuildSystemPrompt(SubAgentRequest request)
        {
            var sb = new StringBuilder();

            switch (request.Task)
            {
                case SubAgentTask.AnalyzeStructure:
                    BuildStructureAnalysisPrompt(sb);
                    break;
                case SubAgentTask.ExtractKeyInfo:
                    BuildKeyInfoExtractionPrompt(sb);
                    break;
                case SubAgentTask.Custom:
                    BuildCustomAnalysisPrompt(sb);
                    break;
            }

            sb.AppendLine();
            sb.AppendLine("Important rules:");
            sb.AppendLine("- You only perform analysis and information extraction. Never modify the document.");
            sb.AppendLine("- Reference paragraphs using the format [Paragraph #N].");
            sb.AppendLine("- Always respond in Chinese.");
            sb.AppendLine("- Output analysis results directly without any pleasantries or preamble.");
            sb.AppendLine("- If tools are available, you may call them to obtain additional document information.");

            return sb.ToString();
        }

        private static void BuildCustomAnalysisPrompt(StringBuilder sb)
        {
            sb.AppendLine("You are a professional document analysis sub-agent.");
            sb.AppendLine("Your task is to analyze the provided document content according to the user's instructions.");
            sb.AppendLine();
            sb.AppendLine("Analysis framework:");
            sb.AppendLine("1. First, understand the overall structure and purpose of the document.");
            sb.AppendLine("2. Focus on the specific aspects requested in the task instructions.");
            sb.AppendLine("3. Support every finding with paragraph references [Paragraph #N].");
            sb.AppendLine("4. If the task involves comparison or consistency checking, cross-reference all relevant sections.");
            sb.AppendLine("5. If the provided data is insufficient, use available tools to fetch more document content.");
            sb.AppendLine();
            sb.AppendLine("Output format:");
            sb.AppendLine("- Use structured sections with clear headings (##).");
            sb.AppendLine("- List findings as bullet points with paragraph references.");
            sb.AppendLine("- End with a concise summary of key findings.");
        }

        private static void BuildStructureAnalysisPrompt(StringBuilder sb)
        {
            sb.AppendLine("You are a professional document structure analysis sub-agent.");
            sb.AppendLine("Your task is to infer the true heading hierarchy of the document based on paragraph format metadata.");
            sb.AppendLine();
            sb.AppendLine("Key analysis points:");
            sb.AppendLine("1. Many documents do not correctly use Word heading styles (Heading 1~6), but instead rely on font size, bold, centering, etc. to distinguish heading levels.");
            sb.AppendLine("2. Infer heading-to-body hierarchy by font size (larger → higher level), bold, centering, and other formatting cues.");
            sb.AppendLine("3. Detect formatting inconsistencies (e.g., same-level headings with different font sizes, broken numbering sequences).");
            sb.AppendLine("4. Output the inferred document outline tree plus a list of formatting issues found.");
            sb.AppendLine();
            sb.AppendLine("Required output format:");
            sb.AppendLine("## Inferred Document Outline");
            sb.AppendLine("Level 1: Heading text (Paragraph #N, traits: 18pt bold centered)");
            sb.AppendLine("  Level 2: Heading text (Paragraph #N, traits: 14pt bold left-aligned)");
            sb.AppendLine("    Level 3: Heading text (Paragraph #N, traits: 12pt bold left-aligned)");
            sb.AppendLine();
            sb.AppendLine("## Formatting Issues");
            sb.AppendLine("- [Paragraph #N] Suspected level-2 heading but font size is 16pt while peers are 14pt");
            sb.AppendLine("- [Paragraph #N] Numbering '1.3' jumps to '1.5', missing '1.4'");
        }

        private static void BuildKeyInfoExtractionPrompt(StringBuilder sb)
        {
            sb.AppendLine("You are a professional document key-information extraction sub-agent.");
            sb.AppendLine("Your task is to extract key information from the document text for cross-checking consistency.");
            sb.AppendLine();
            sb.AppendLine("Information categories to extract:");
            sb.AppendLine("1. **Data & Numbers**: Statistics, percentages, amounts, quantities — record the paragraph where each appears.");
            sb.AppendLine("2. **Dates & Times**: Specific dates, time ranges, deadlines.");
            sb.AppendLine("3. **Names & Terms**: Person names, organization names, project names, product names, technical terms (including abbreviation-full name mappings).");
            sb.AppendLine("4. **Key Assertions**: Important conclusions, commitments, goals, metrics — especially claims that could contradict each other.");
            sb.AppendLine("5. **References & Standards**: Cited literature numbers, standard numbers, regulations.");
            sb.AppendLine();
            sb.AppendLine("Required output format:");
            sb.AppendLine("## Key Data");
            sb.AppendLine("- [Paragraph #N] Output: 5 million tons");
            sb.AppendLine("- [Paragraph #M] Output: 5.2 million tons  ⚠️ Inconsistent with Paragraph #N");
            sb.AppendLine();
            sb.AppendLine("## Dates");
            sb.AppendLine("- [Paragraph #N] Deadline: 2025-12-31");
            sb.AppendLine();
            sb.AppendLine("## Terms");
            sb.AppendLine("- \"Project XXX\" = \"XX Plan\" (first appears in Paragraph #N)");
            sb.AppendLine();
            sb.AppendLine("## Consistency Issues");
            sb.AppendLine("- ⚠️ [Paragraph #A vs #B] Data conflict: ...");
            sb.AppendLine("- ⚠️ [Paragraph #A vs #B] Terminology inconsistency: ...");
        }

        // ═══════════════════════════════════════════════════════════════
        //  用户消息构建
        // ═══════════════════════════════════════════════════════════════

        private static string BuildUserMessage(SubAgentRequest request)
        {
            var sb = new StringBuilder();

            if (request.DocumentStructure != null)
            {
                sb.AppendLine("<document_structure>");
                sb.AppendLine(request.DocumentStructure.ToStructuredText());
                sb.AppendLine("</document_structure>");
                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(request.DocumentText))
            {
                sb.AppendLine("<document_text>");
                sb.AppendLine(request.DocumentText);
                sb.AppendLine("</document_text>");
                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(request.Prompt))
            {
                sb.AppendLine("<task>");
                sb.AppendLine(request.Prompt);
                sb.AppendLine("</task>");
            }

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
                string url = $"{baseUrl}/chat/completions";

                var requestObj = new JObject
                {
                    ["model"] = modelName,
                    ["messages"] = memory.BuildMessagesJson(),
                    ["stream"] = false,
                    ["temperature"] = 0.3 // 分析任务用较低 temperature
                };

                if (tools != null && tools.Count > 0)
                    requestObj["tools"] = tools;

                string jsonData = requestObj.ToString(Formatting.None);
                var content = new StringContent(jsonData, Encoding.UTF8, "application/json");

                var httpRequest = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
                if (!string.IsNullOrEmpty(apiKey))
                    httpRequest.Headers.Add("Authorization", $"Bearer {apiKey}");

                Debug.WriteLine($"[SubAgent] POST {url}, model={modelName}, msgs={memory.Count + 1}, tools={tools?.Count ?? 0}");

                using (var response = await _httpClient.SendAsync(httpRequest, cancellation))
                {
                    string body = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        Debug.WriteLine($"[SubAgent] HTTP {response.StatusCode}: {body}");
                        return null;
                    }

                    return ParseLlmResponse(body);
                }
            }
            catch (OperationCanceledException)
            {
                throw; // 让取消正常冒泡
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SubAgent] 请求异常: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>解析非流式 LLM 响应 JSON</summary>
        private static LlmResponse ParseLlmResponse(string json)
        {
            var root = JObject.Parse(json);
            var choice = root["choices"]?[0];
            if (choice == null) return null;

            var message = choice["message"];
            if (message == null) return null;

            var result = new LlmResponse
            {
                Content = message["content"]?.ToString(),
                FinishReason = choice["finish_reason"]?.ToString(),
                ToolCalls = new List<ToolCallRequest>()
            };

            // 解析 tool_calls
            var toolCallsArr = message["tool_calls"] as JArray;
            if (toolCallsArr != null)
            {
                foreach (var tc in toolCallsArr)
                {
                    var fn = tc["function"];
                    if (fn == null) continue;

                    string argsStr = fn["arguments"]?.ToString() ?? "{}";
                    JObject argsObj;
                    try { argsObj = JObject.Parse(argsStr); }
                    catch { argsObj = new JObject(); }

                    result.ToolCalls.Add(new ToolCallRequest
                    {
                        Id = tc["id"]?.ToString() ?? Guid.NewGuid().ToString(),
                        FunctionName = fn["name"]?.ToString(),
                        Arguments = argsObj
                    });
                }
            }

            Debug.WriteLine($"[SubAgent] 响应: content_len={result.Content?.Length ?? 0}, tool_calls={result.ToolCalls.Count}, finish={result.FinishReason}");
            return result;
        }
    }
}
