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

        // ── 系统 Prompt（纯文本补丁格式） ──

        private const string SYSTEM_PROMPT =
@"你是一个专业的中文文本校对专家。请仔细检查用户提供的文本，找出其中的错误并提出修改建议。

检查以下类型的错误：
1. 错别字：汉字写错、混淆（如 的/地/得 用法错误）
2. 语法错误：句子结构不当、成分残缺或多余
3. 标点符号：标点使用不当、中英文标点混用
4. 用词不当：词语搭配不恰当、语义重复或矛盾
5. 格式问题：多余空格、缺失空格等

【输出格式 — 严格遵守，不要输出任何其他内容】

对每一处错误，输出如下格式的一条纠错：

>>>>ORIGINAL
（原文中需要修改的最小连续片段，必须与原文完全一致）
>>>>REPLACEMENT
（修改后的文本，即 ORIGINAL 片段的纠正版本）
>>>>REASON
（简短修改理由）
====

最后输出总结：

>>>>SUMMARY
（纠错总结说明）
====

【核心规则 — 务必遵守】
- ORIGINAL 必须是原文中可精确匹配到的连续文本片段，不要编造不存在的原文
- REPLACEMENT 是 ORIGINAL 的原位修正：仅替换其中的错误部分，保留所有正确的字词不变
- 禁止在修正时删除、增添或重组 ORIGINAL 中本身正确的内容
- 每条修改精准定位到最小错误片段，不要把整句话作为一条修改
- 如果文本完全正确，只输出 SUMMARY 段即可
- 不要输出 markdown、代码块或其他额外格式

【示例】

原文：进一步彰显了高分辨率遥感在综合国士治理中的核心地位。

正确的纠错输出：
>>>>ORIGINAL
国士
>>>>REPLACEMENT
国土
>>>>REASON
""国士""应为""国土""，指国家的领土，此处为形近字错误
====

错误示范（禁止）：
>>>>ORIGINAL
综合国士治理
>>>>REPLACEMENT
综合治理
>>>>REASON
...
====
以上是错误的，因为修正时删除了原文中正确的""国土""一词";

        // ── 核心方法 ──

        /// <summary>
        /// 向大模型发送纠错请求，解析纯文本补丁格式的返回结果。
        /// </summary>
        public async Task<CorrectionResult> CorrectTextAsync(
            string text,
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
                string responseText = await CallChatAsync(text, cancellationToken);

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

        private async Task<string> CallChatAsync(string text, CancellationToken cancellationToken = default)
        {
            string url = $"{_baseUrl}/chat/completions";

            var requestBody = new
            {
                model = _modelName,
                messages = new[]
                {
                    new { role = "system", content = SYSTEM_PROMPT },
                    new { role = "user", content = $"请检查以下文本并纠错：\n\n{text}" }
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
