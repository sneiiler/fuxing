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

        // ── 共用输出格式规范（不含示例） ──
        private static readonly string FORMAT_RULES =
@"【输出格式 — 必须严格遵守，不要输出任何其他内容。】

对于发现的每处错误，按以下格式输出一条纠错条目：

>>>>ORIGINAL
（从原文中提取的最小连续片段，必须与原文完全一致。）
>>>>REPLACEMENT
（对 ORIGINAL 片段的修正版本 — 仅修复错误部分，保留所有正确的字词不变。）
>>>>REASON
（简要说明纠错原因，必须使用中文。）
====

在最后，输出一段总结：

>>>>SUMMARY
（整体纠错总结，必须使用中文。）
====

【核心规则 — 必须遵守】
- ORIGINAL 必须是能在原文中精确匹配到的连续文本片段，不得编造不存在的原文。
- REPLACEMENT 是对 ORIGINAL 的原位修复：仅替换错误部分，保留所有正确的字词不变。
- 不得删除、增加或重新组织 ORIGINAL 中已经正确的部分。
- 每条纠错必须精确定位到最小的错误片段，不要把整个句子作为一条纠错。
- 如果文本完全正确，只需输出 SUMMARY 部分。
- 不要输出 Markdown、代码块或任何多余的格式。
- REASON 和 SUMMARY 必须使用中文书写。";

        // ── 错别字模式 ──

        private static readonly string PROMPT_TYPO =
@"你是一个专业的中文错别字检测专家。你的任务是**严格只检查错别字** — 不要检查其他类型的问题。

只检查以下类型的错误：
1. 错别字：书写错误的汉字（例如""国士""应为""国土""）。
2. 同音字混淆：因发音相同而用错字（例如""以经""应为""已经""）。
3. 形似字混淆：因字形相似而用错字（例如""未""写成""末""）。

【严格禁止检查 — 违反视为错误输出】
- 不要检查语法错误。
- 不要检查标点使用。
- 不要检查用词是否恰当。
- 不要检查句子结构。
- 不要检查语义流畅性。
- 不要检查表达一致性。
- 不要提出任何润色或优化建议。

只有当你确定某个字写错了才输出纠错条目。不确定时不要报告。";

        private static readonly string EXAMPLES_TYPO =
@"【示例一：形近字错误】

原文：进一步彰显了高分辨率遥感在综合国士治理中的核心地位。

正确输出：
>>>>ORIGINAL
国士
>>>>REPLACEMENT
国土
>>>>REASON
""国士""应为""国土""（国家领土），属于形近字混淆错误。
====

>>>>SUMMARY
共发现 1 处错别字：形近字混淆。
====

【示例二：多处同音字错误】

原文：该项目以经完成了初步的瓶估工作。

正确输出：
>>>>ORIGINAL
以经
>>>>REPLACEMENT
已经
>>>>REASON
""以经""应为""已经""，属于同音字混淆错误。
====

>>>>ORIGINAL
瓶估
>>>>REPLACEMENT
评估
>>>>REASON
""瓶估""应为""评估""，属于同音字混淆错误。
====

>>>>SUMMARY
共发现 2 处错别字：均为同音字混淆。
====

【反面示例（禁止）】
>>>>ORIGINAL
综合国士治理
>>>>REPLACEMENT
综合治理
>>>>REASON
...
====
以上写法是错的，因为纠正时把正确的""国土""一词也删除了。ORIGINAL 必须仅包含错误的最小片段。

【示例三：无错误文本】

原文：该项目已经完成了初步的评估工作。

正确输出：
>>>>SUMMARY
文本检查完毕，未发现错别字。
====";

        // ── 语义模式 ──

        private static readonly string PROMPT_SEMANTIC =
@"你是一个专业的中文文本校对专家。仔细检查用户提供的文本，找出所有语义层面的错误并给出修改建议。

检查以下类型的错误：
1. 错别字：错别字、同音和形似字混淆。
2. 语法错误：句子结构不当、成分缺失或冗余。
3. 标点问题：标点使用不当、中英文标点混用。
4. 用词问题：搭配不当、语义重复或矛盾。
5. 逻辑错误：上下文不连贯、因果关系错误。
6. 格式问题：多余或缺失空格。

【严格禁止检查】
- 不要检查文档级跨引用术语一致性（这属于一致性检查模式）。
- 不要进行润色 — 只关注明显的错误。";

        private static readonly string EXAMPLES_SEMANTIC =
@"【示例一：语法错误 — 介词套用导致主语缺失】

原文：通过这次改革使企业效率得到了显著提高。

正确输出：
>>>>ORIGINAL
通过这次改革使
>>>>REPLACEMENT
通过这次改革，
>>>>REASON
""通过……使……""构成介词套用，导致句子主语缺失。应去掉""使""，改为逗号分隔。
====

>>>>SUMMARY
共发现 1 处语法问题：介词套用导致主语缺失。
====

【示例二：语义重复】

原文：可以减少不必要的浪费。

正确输出：
>>>>ORIGINAL
不必要的浪费
>>>>REPLACEMENT
浪费
>>>>REASON
""浪费""本身已含""不必要""之义，""不必要的浪费""属于语义重复。
====

>>>>SUMMARY
共发现 1 处用词问题：语义重复。
====

【反面示例（禁止）】
>>>>ORIGINAL
通过这次改革使企业效率得到了显著提高。
>>>>REPLACEMENT
这次改革使企业效率得到了显著提高。
>>>>REASON
...
====
以上写法是错的，ORIGINAL 应仅包含需要修正的最小片段，而非整个句子。";

        // ── 一致性模式 ──

        private static readonly string PROMPT_CONSISTENCY =
@"你是一个专业的文档一致性审查专家。仔细阅读用户提供的全文，找出不同部分之间的不一致问题。

重点关注以下类型的一致性问题：
1. 术语一致性：同一概念在不同地方使用不同的术语或名称（例如某处用""服务器""另一处用""主机""指同一事物）。
2. 数据一致性：同一数据点在不同地方显示不同的值。
3. 表达一致性：同一含义在不同地方表达方式不同，可能让读者困惑。
4. 逻辑一致性：前后文表述相互矛盾。
5. 格式一致性：相似内容使用不一致的编号、命名或格式约定。

【严格禁止检查】
- 不要检查纯错别字（除非错别字导致跨引用不一致）。
- 不要检查单句语法错误。
- 不要检查标点。
- 只关注""跨引用不一致""类型的问题。

注意：你必须通读全文，比较前后章节来发现一致性问题。单独的句子可能看起来没问题，但与上下文比较就能发现不一致。仔细比较全文中的所有术语、数据和表达。";

        private static readonly string EXAMPLES_CONSISTENCY =
@"【示例：术语不一致】

原文片段：
第3段：""本项目部署了高性能服务器集群……""
第15段：""系统主机采用双路处理器架构……""
第22段：""服务器运行状态良好……""

正确输出：
>>>>ORIGINAL
主机
>>>>REPLACEMENT
服务器
>>>>REASON
第15段使用""主机""，而第3段和第22段使用""服务器""指代同一设备，应统一术语为""服务器""。
====

>>>>SUMMARY
共发现 1 处术语不一致：同一设备在不同段落中使用了""服务器""和""主机""两种称呼。
====

【反面示例（禁止）】
仅因为某段的句式和其他段不同就报告为不一致。一致性检查只关注同一概念的术语、数据、表达是否统一，不关注写作风格差异。";

        // ── 组合：模式提示词 + 格式规范 + 模式专属示例 ──

        /// <summary>根据纠错模式获取完整的系统提示词</summary>
        private static string GetSystemPrompt(CorrectionMode mode)
        {
            string modePrompt;
            string examples;
            switch (mode)
            {
                case CorrectionMode.Typo:
                    modePrompt = PROMPT_TYPO;
                    examples = EXAMPLES_TYPO;
                    break;
                case CorrectionMode.Semantic:
                    modePrompt = PROMPT_SEMANTIC;
                    examples = EXAMPLES_SEMANTIC;
                    break;
                case CorrectionMode.Consistency:
                    modePrompt = PROMPT_CONSISTENCY;
                    examples = EXAMPLES_CONSISTENCY;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }
            return modePrompt + "\n\n" + FORMAT_RULES + "\n\n" + examples;
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
                    new { role = "user", content = $"请检查并纠正以下文本：\n\n{text}" }
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
