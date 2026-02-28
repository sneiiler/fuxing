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
            sb.AppendLine("重要规则：");
            sb.AppendLine("- 你只执行信息分析和提取，不要修改文档。");
            sb.AppendLine("- 使用 [第N段] 格式来引用段落。");
            sb.AppendLine("- 必须用中文回复。");
            sb.AppendLine("- 直接输出分析结果，不要客套话或开场白。");
            sb.AppendLine("- 如果有可用的工具，可以调用它们获取更多文档信息。");

            return sb.ToString();
        }

        private static void BuildCustomAnalysisPrompt(StringBuilder sb)
        {
            sb.AppendLine("你是一个专业的文档分析子智能体。");
            sb.AppendLine("你的任务是按照用户的指令分析所提供的文档内容。");
            sb.AppendLine();
            sb.AppendLine("分析框架：");
            sb.AppendLine("1. 首先理解文档的整体结构和目的。");
            sb.AppendLine("2. 重点关注任务指令中请求的具体方面。");
            sb.AppendLine("3. 每个发现都要附带段落引用 [第N段]。");
            sb.AppendLine("4. 如果任务涉及对比或一致性检查，请交叉引用所有相关章节。");
            sb.AppendLine("5. 如果提供的数据不足，使用可用工具获取更多文档内容。");
            sb.AppendLine();
            sb.AppendLine("输出格式：");
            sb.AppendLine("- 使用带清晰标题（##）的结构化章节。");
            sb.AppendLine("- 将发现结果列为带段落引用的项目符号列表。");
            sb.AppendLine("- 最后以简洁的关键发现摘要结束。");
        }

        private static void BuildStructureAnalysisPrompt(StringBuilder sb)
        {
            sb.AppendLine("你是一个专业的文档结构分析子智能体。");
            sb.AppendLine("你的任务是根据段落格式元数据推断文档的真实标题层级。");
            sb.AppendLine();
            sb.AppendLine("关键分析点：");
            sb.AppendLine("1. 许多文档没有正确使用 Word 标题样式（标题1~6），而是通过字体大小、加粗、居中等方式来区分标题级别。");
            sb.AppendLine("2. 通过字体大小（越大→级别越高）、加粗、居中等格式线索来推断标题与正文的层级关系。");
            sb.AppendLine("3. 检测格式不一致问题（例如同级标题字体大小不同、编号序列中断）。");
            sb.AppendLine("4. 输出推断的文档大纲树以及发现的格式问题列表。");
            sb.AppendLine();
            sb.AppendLine("必需输出格式：");
            sb.AppendLine("## 推断的文档大纲");
            sb.AppendLine("一级：标题文字（第N段，特征：18pt 加粗 居中）");
            sb.AppendLine("  二级：标题文字（第N段，特征：14pt 加粗 左对齐）");
            sb.AppendLine("    三级：标题文字（第N段，特征：12pt 加粗 左对齐）");
            sb.AppendLine();
            sb.AppendLine("## 格式问题");
            sb.AppendLine("- [第N段] 疑似二级标题但字体为16pt，而同级为14pt");
            sb.AppendLine("- [第N段] 编号从'1.3'跳到'1.5'，缺少'1.4'");
        }

        private static void BuildKeyInfoExtractionPrompt(StringBuilder sb)
        {
            sb.AppendLine("你是一个专业的文档关键信息提取子智能体。");
            sb.AppendLine("你的任务是从文档文本中提取关键信息，用于交叉检查一致性。");
            sb.AppendLine();
            sb.AppendLine("需要提取的信息类别：");
            sb.AppendLine("1. **数据与数字**：统计数字、百分比、金额、数量 — 记录每个数据出现的段落。");
            sb.AppendLine("2. **日期与时间**：具体日期、时间范围、截止日期。");
            sb.AppendLine("3. **名称与术语**：人名、组织名、项目名、产品名、技术术语（包括缩写-全称对应关系）。");
            sb.AppendLine("4. **关键论断**：重要结论、承诺、目标、指标 — 特别关注可能相互矛盾的声明。");
            sb.AppendLine("5. **引用与标准**：引用的文献编号、标准编号、法规。");
            sb.AppendLine();
            sb.AppendLine("必需输出格式：");
            sb.AppendLine("## 关键数据");
            sb.AppendLine("- [第N段] 产量：500万吨");
            sb.AppendLine("- [第M段] 产量：520万吨  ⚠️ 与第N段不一致");
            sb.AppendLine();
            sb.AppendLine("## 日期");
            sb.AppendLine("- [第N段] 截止日期：2025-12-31");
            sb.AppendLine();
            sb.AppendLine("## 术语");
            sb.AppendLine("- \"项目XXX\" = \"XX计划\"（首次出现在第N段）");
            sb.AppendLine();
            sb.AppendLine("## 一致性问题");
            sb.AppendLine("- ⚠️ [第A段 vs 第B段] 数据冲突：...");
            sb.AppendLine("- ⚠️ [第A段 vs 第B段] 术语不一致：...");
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
