using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FuXing
{
    // ═══════════════════════════════════════════════════════════════
    //  数据模型
    // ═══════════════════════════════════════════════════════════════

    /// <summary>一条纠错建议（原文 → 建议）</summary>
    public class CorrectionItem
    {
        public string Original { get; set; }
        public string Replacement { get; set; }
        public string Reason { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════
    //  纠错模式
    // ═══════════════════════════════════════════════════════════════

    /// <summary>纠错检查级别</summary>
    public enum CorrectionMode
    {
        /// <summary>只检查错别字</summary>
        Typo,
        /// <summary>检查语义级别错误</summary>
        Semantic,
        /// <summary>检查文档前后表述一致性错误</summary>
        Consistency
    }

    // ═══════════════════════════════════════════════════════════════
    //  纠错服务（纯文本补丁格式，不依赖 Tool Calling / JSON）
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// AI 文本纠错服务。要求大模型以纯文本"补丁标记"格式输出纠错结果，
    /// 彻底避免 JSON 解析不稳定与 Tool Calling 兼容性问题。
    /// </summary>
    public class TextCorrectionService
    {
        private readonly string _baseUrl;
        private readonly string _apiKey;
        private readonly string _modelName;

        static TextCorrectionService()
        {
            // .NET Framework 默认仅启用 SSL3/TLS1.0，需手动启用 TLS 1.2
            ServicePointManager.SecurityProtocol |=
                SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;
        }

        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(300)
        };

        public TextCorrectionService(string baseUrl, string apiKey, string modelName)
        {
            _baseUrl = (baseUrl ?? "").TrimEnd('/');
            _apiKey = apiKey ?? "";
            _modelName = modelName ?? "";
        }

        /// <summary>从当前配置构造服务实例</summary>
        public static TextCorrectionService FromConfig()
        {
            var loader = new ConfigLoader();
            var cfg = loader.LoadConfig();
            return new TextCorrectionService(cfg.BaseURL, cfg.ApiKey, cfg.ModelName);
        }

        // ── 系统 Prompt（按纠错模式动态生成） ──

        private static readonly string OUTPUT_FORMAT =
@"【Output Format — Strictly follow. Do not output anything else.】

For each error found, output one correction entry in the following format:

>>>>ORIGINAL
(The minimal contiguous snippet from the original text that needs correction. Must match the source text exactly.)
>>>>REPLACEMENT
(The corrected version of the ORIGINAL snippet — only fix the erroneous part, keep all correct characters intact.)
>>>>REASON
(Brief reason for the correction.)
====

At the end, output a summary:

>>>>SUMMARY
(Overall correction summary.)
====

【Core Rules — Must follow】
- ORIGINAL must be a contiguous text snippet that can be precisely matched in the source text. Never fabricate non-existent original text.
- REPLACEMENT is an in-place fix of ORIGINAL: only replace the erroneous part, preserving all correct words unchanged.
- Never delete, add, or reorganize parts of ORIGINAL that are already correct.
- Each correction must pinpoint the minimal error snippet. Do not use an entire sentence as one correction.
- If the text is entirely correct, only output the SUMMARY section.
- Do not output markdown, code blocks, or any other extra formatting.

【Example】

Original text: 进一步彰显了高分辨率遥感在综合国士治理中的核心地位。

Correct output:
>>>>ORIGINAL
国士
>>>>REPLACEMENT
国土
>>>>REASON
""国士"" should be ""国土"" (national territory). This is a visually similar character error.
====

Incorrect example (FORBIDDEN):
>>>>ORIGINAL
综合国士治理
>>>>REPLACEMENT
综合治理
>>>>REASON
...
====
The above is wrong because the correction deleted the correct word ""国土"" from the original.";

        private static readonly string PROMPT_TYPO =
@"You are a professional Chinese typo detection expert. Your task is to **strictly and only check for typos** — do not check any other type of issue.

Only check the following types of errors:
1. Wrong characters: An incorrect Chinese character (e.g., ""国士"" should be ""国土"").
2. Homophone confusion: Wrong character due to identical pronunciation (e.g., ""以经"" should be ""已经"").
3. Visually similar character errors: Wrong character due to similar appearance (e.g., ""未"" written as ""末"").

【Strictly forbidden to check — violations are considered incorrect output】
- Do NOT check grammar errors.
- Do NOT check punctuation usage.
- Do NOT check whether word choice is appropriate.
- Do NOT check sentence structure.
- Do NOT check semantic fluency.
- Do NOT check consistency of expressions.
- Do NOT make any polishing or optimization suggestions.

Only output a correction entry when you are certain a character is written incorrectly. When in doubt, do not report.";

        private static readonly string PROMPT_SEMANTIC =
@"You are a professional Chinese text proofreading expert. Carefully examine the user-provided text and identify all semantic-level errors with correction suggestions.

Check the following types of errors:
1. Typos: Wrong characters, homophone and visually similar character confusion.
2. Grammar errors: Improper sentence structure, missing or redundant components.
3. Punctuation: Incorrect punctuation usage, mixed Chinese/English punctuation.
4. Word choice: Inappropriate collocations, semantic redundancy or contradiction.
5. Logic errors: Incoherent context, incorrect cause-effect relationships.
6. Formatting issues: Extra or missing spaces.

【Strictly forbidden to check】
- Do NOT check document-level cross-reference terminology consistency (that belongs to the consistency check mode).
- Do NOT perform style polishing — only focus on clear errors.";

        private static readonly string PROMPT_CONSISTENCY =
@"You are a professional document consistency reviewer. Carefully read through all the user-provided text and identify inconsistencies between different parts of the document.

Focus on the following types of consistency issues:
1. Terminology consistency: The same concept uses different terms or names in different places (e.g., ""server"" in one place and ""host"" in another referring to the same thing).
2. Data consistency: The same data point shows different values in different places.
3. Expression consistency: The same meaning is expressed differently in different places, potentially confusing readers.
4. Logic consistency: Earlier and later statements contradict each other.
5. Format consistency: Similar content uses inconsistent numbering, naming, or formatting conventions.

【Strictly forbidden to check】
- Do NOT check pure typos (unless the typo causes cross-reference inconsistency).
- Do NOT check single-sentence grammar errors.
- Do NOT check punctuation.
- Only focus on ""cross-reference inconsistency"" type issues.

Note: You must read the entire text and compare earlier and later sections to discover consistency issues. A single sentence may look fine in isolation, but comparing it against context reveals inconsistencies. Carefully compare all terms, data, and expressions throughout the text.";

        /// <summary>根据纠错模式获取完整的系统提示词</summary>
        private static string GetSystemPrompt(CorrectionMode mode)
        {
            string modePrompt;
            switch (mode)
            {
                case CorrectionMode.Typo: modePrompt = PROMPT_TYPO; break;
                case CorrectionMode.Semantic: modePrompt = PROMPT_SEMANTIC; break;
                case CorrectionMode.Consistency: modePrompt = PROMPT_CONSISTENCY; break;
                default: throw new ArgumentOutOfRangeException(nameof(mode));
            }
            return modePrompt + "\n\n" + OUTPUT_FORMAT;
        }

        // ── 核心方法 ──

        /// <summary>
        /// 向大模型发送纠错请求，解析纯文本补丁格式的返回结果。
        /// </summary>
        public async Task<CorrectionResult> CorrectTextAsync(
            string text,
            CorrectionMode mode = CorrectionMode.Typo,
            Action<string> onProgress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_baseUrl))
                return CorrectionResult.Error("未配置 Base URL");
            if (string.IsNullOrWhiteSpace(_modelName))
                return CorrectionResult.Error("未配置模型名称");
            if (string.IsNullOrWhiteSpace(text))
                return CorrectionResult.Error("文本内容为空");

            onProgress?.Invoke("正在发送纠错请求...");

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                onProgress?.Invoke("AI 正在分析文本...");
                string systemPrompt = GetSystemPrompt(mode);
                string responseText = await CallChatAsync(text, systemPrompt, cancellationToken);

                onProgress?.Invoke("正在解析纠错结果...");
                return ParsePatchResponse(responseText, text);
            }
            catch (OperationCanceledException)
            {
                return CorrectionResult.Error("用户已取消纠错");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"纠错请求异常: {ex}");
                return CorrectionResult.Error($"纠错请求失败: {ex.Message}");
            }
        }

        // ── 解析纯文本补丁格式 ──

        private CorrectionResult ParsePatchResponse(string response, string sourceText)
        {
            var corrections = new List<CorrectionItem>();
            string summary = "";

            if (string.IsNullOrWhiteSpace(response))
                return CorrectionResult.Error("模型返回空响应");

            System.Diagnostics.Debug.WriteLine($"纠错原始响应:\n{response}");

            // 按 "====" 分隔各条目
            var blocks = response.Split(new[] { "====" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var block in blocks)
            {
                string trimmed = block.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                // 总结段
                if (trimmed.Contains(">>>>SUMMARY"))
                {
                    summary = ExtractField(trimmed, ">>>>SUMMARY");
                    continue;
                }

                // 纠错段
                string original = ExtractField(trimmed, ">>>>ORIGINAL");
                string replacement = ExtractField(trimmed, ">>>>REPLACEMENT");
                string reason = ExtractField(trimmed, ">>>>REASON");

                if (string.IsNullOrEmpty(original) || replacement == null)
                    continue;

                // 验证 original 在原文中可匹配
                if (sourceText.Contains(original))
                {
                    corrections.Add(new CorrectionItem
                    {
                        Original = original,
                        Replacement = replacement,
                        Reason = reason ?? ""
                    });
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"纠错项被跳过（原文不匹配）: '{original}'");
                }
            }

            return new CorrectionResult
            {
                Success = true,
                Corrections = corrections,
                Summary = string.IsNullOrEmpty(summary)
                    ? $"共发现 {corrections.Count} 处问题"
                    : summary
            };
        }

        /// <summary>
        /// 提取 block 中指定标记之后、下一个 >>>> 标记之前的文本内容。
        /// </summary>
        private string ExtractField(string block, string marker)
        {
            int idx = block.IndexOf(marker);
            if (idx < 0) return null;

            // 跳过 marker 本身和同行剩余内容
            int start = idx + marker.Length;
            int lineEnd = block.IndexOf('\n', start);
            if (lineEnd >= 0)
                start = lineEnd + 1;
            else
                return ""; // marker 在最后一行且无换行

            // 找下一个 >>>> 标记作为结束位置
            int end = block.Length;
            int nextMarker = block.IndexOf(">>>>", start);
            if (nextMarker >= 0)
                end = nextMarker;

            if (start >= end) return "";
            return block.Substring(start, end - start).Trim();
        }

        // ── HTTP 调用（普通 Chat Completion，不使用 tools） ──

        private async Task<string> CallChatAsync(string text, string systemPrompt, CancellationToken cancellationToken = default)
        {
            string url = $"{_baseUrl}/chat/completions";

            var requestBody = new
            {
                model = _modelName,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = $"Please check and correct the following text:\n\n{text}" }
                },
                temperature = 0.1,
                max_tokens = 4096
            };

            string json = JsonConvert.SerializeObject(requestBody);
            System.Diagnostics.Debug.WriteLine($"纠错请求 JSON:\n{json}");

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };

            if (!string.IsNullOrEmpty(_apiKey))
                request.Headers.Add("Authorization", $"Bearer {_apiKey}");

            using (var response = await _httpClient.SendAsync(request, cancellationToken))
            {
                string body = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"纠错响应 [{response.StatusCode}]:\n{body}");

                if (!response.IsSuccessStatusCode)
                    throw new Exception($"HTTP {(int)response.StatusCode}: {body}");

                var respObj = JsonConvert.DeserializeObject<ChatCompletionResponse>(body);
                if (respObj?.Choices == null || respObj.Choices.Count == 0)
                    throw new Exception("模型未返回有效响应");

                return respObj.Choices[0].Message?.Content ?? "";
            }
        }

        // ── 最小化 Chat API 响应模型 ──

        private class ChatMsg
        {
            [JsonProperty("content")]
            public string Content { get; set; }
        }

        private class ChatChoice
        {
            [JsonProperty("message")]
            public ChatMsg Message { get; set; }
        }

        private class ChatCompletionResponse
        {
            [JsonProperty("choices")]
            public List<ChatChoice> Choices { get; set; }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  纠错结果
    // ═══════════════════════════════════════════════════════════════

    public class CorrectionResult
    {
        public bool Success { get; set; }
        public List<CorrectionItem> Corrections { get; set; } = new List<CorrectionItem>();
        public string Summary { get; set; }
        public string ErrorMessage { get; set; }

        public bool HasCorrections => Corrections != null && Corrections.Count > 0;

        public static CorrectionResult Error(string message)
        {
            return new CorrectionResult
            {
                Success = false,
                ErrorMessage = message,
                Summary = message
            };
        }
    }
}
