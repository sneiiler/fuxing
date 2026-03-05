using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using FuXingAgent.Agents;
using FuXingAgent.Core;
using FuXingAgent.Tools;
using Word = Microsoft.Office.Interop.Word;

namespace FuXingAgent.Workflows
{
    /// <summary>
    /// 纠错工作流 — 固定三步流水线：读取内容 → LLM 分析错误 → 应用修正。
    /// 主 Agent 只需调用此工作流并提供自然语言指令，无需手动构造纠错列表。
    /// Step 3 通过 EditContentTool / AddCommentTool 执行，复用现有工具系统。
    /// </summary>
    public class CorrectTextWorkflow
    {
        private readonly Connect _connect;
        private readonly EditContentTool _editTool;
        private readonly AddCommentTool _commentTool;

        public CorrectTextWorkflow(Connect connect)
        {
            _connect = connect;
            _editTool = new EditContentTool(connect);
            _commentTool = new AddCommentTool(connect);
        }

        [Description("Proofread and correct text errors in the document using a fixed workflow: " +
            "1) read document content, 2) AI analyzes errors and produces correction list, " +
            "3) apply corrections via edit_content (Track Changes) and add_comment tools. " +
            "The caller only provides a natural-language instruction describing what to check/correct.")]
        public string correct_text_workflow(
            [Description("纠错指令，描述需要检查和修正的内容（如：修正错别字、统一术语）")] string instruction,
            [Description("修正范围: all=全文, selection=仅选中区域（分析范围，修正应用于全文）")] string scope = "all")
        {
            if (string.IsNullOrWhiteSpace(instruction))
                throw new ArgumentException("缺少 instruction 参数");

            var app = _connect.WordApplication;
            var doc = app.ActiveDocument ?? throw new InvalidOperationException("没有活动文档");

            // ═══ Step 1: 读取目标文本 ═══
            string contentText = ReadTargetContent(app, doc, scope);
            if (string.IsNullOrWhiteSpace(contentText))
                throw new InvalidOperationException("目标范围没有文本内容");

            DebugLogger.Instance.LogInfo($"[CorrectTextWorkflow] Step1 读取完成，{contentText.Length} 字符");

            // ═══ Step 2: LLM 分析错误 ═══
            var corrections = AnalyzeErrors(contentText, instruction);
            if (corrections == null || corrections.Count == 0)
                return "分析完成，未发现需要修正的内容。";

            DebugLogger.Instance.LogInfo($"[CorrectTextWorkflow] Step2 分析完成，发现 {corrections.Count} 处待修正");

            // ═══ Step 3: 通过 EditContentTool + AddCommentTool 应用修正 ═══
            string result = ApplyCorrections(corrections);
            DebugLogger.Instance.LogInfo($"[CorrectTextWorkflow] Step3 修正完成");

            return result;
        }

        /// <summary>Step 1: 读取文档/选区内容</summary>
        private static string ReadTargetContent(Word.Application app, Word.Document doc, string scope)
        {
            if (scope == "selection")
            {
                var sel = app.Selection;
                if (sel == null || string.IsNullOrEmpty(sel.Text?.Trim()))
                    throw new InvalidOperationException("scope=selection 但没有选中文本");
                return sel.Text;
            }

            return doc.Content.Text;
        }

        /// <summary>Step 2: 调用 LLM 分析文本错误，返回结构化纠错列表</summary>
        private List<CorrectionEntry> AnalyzeErrors(string contentText, string instruction)
        {
            var bootstrap = Connect.CurrentInstance.AgentBootstrapInstance;
            var client = bootstrap.ChatClient
                ?? throw new InvalidOperationException("Agent 未初始化");

            string truncated = contentText.Length > 6000
                ? contentText.Substring(0, 6000)
                : contentText;

            string systemPrompt = BuildAnalysisPrompt(instruction);
            var messages = new List<Microsoft.Extensions.AI.ChatMessage>
            {
                new Microsoft.Extensions.AI.ChatMessage(
                    Microsoft.Extensions.AI.ChatRole.System, systemPrompt),
                new Microsoft.Extensions.AI.ChatMessage(
                    Microsoft.Extensions.AI.ChatRole.User, truncated)
            };

            var options = new Microsoft.Extensions.AI.ChatOptions { Temperature = 0.2f };
            var response = client.GetResponseAsync(messages, options).GetAwaiter().GetResult();
            string responseText = response?.Text;

            if (string.IsNullOrWhiteSpace(responseText))
                return new List<CorrectionEntry>();

            return ParseCorrections(responseText);
        }

        /// <summary>Step 3: 通过现有工具逐条应用纠错</summary>
        private string ApplyCorrections(List<CorrectionEntry> corrections)
        {
            int applied = 0;
            int notFound = 0;
            var details = new StringBuilder();

            foreach (var item in corrections)
            {
                if (string.IsNullOrEmpty(item.Original) || string.IsNullOrEmpty(item.Replacement))
                {
                    notFound++;
                    continue;
                }

                // 通过 EditContentTool 执行查找替换（内部自带 Track Changes）
                string editResult = _editTool.edit_content(
                    find_text: item.Original,
                    replace_text: item.Replacement,
                    match_case: true);

                if (editResult.Contains("未找到"))
                {
                    notFound++;
                    details.AppendLine($"  未找到: \"{item.Original}\"");
                }
                else
                {
                    applied++;
                    details.AppendLine($"  \"{item.Original}\" -> \"{item.Replacement}\" ({item.Reason}) {editResult}");

                    // 通过 AddCommentTool 添加修正原因批注
                    if (!string.IsNullOrWhiteSpace(item.Reason))
                    {
                        try
                        {
                            _commentTool.add_comment(
                                comment: item.Reason,
                                target: "search",
                                search_text: item.Replacement);
                        }
                        catch
                        {
                            // 替换文本在文档中不唯一时批注可能定位不精确，不影响修正结果
                        }
                    }
                }
            }

            return $"纠错完成: {applied} 处已修正, {notFound} 处未匹配\n{details}";
        }

        private static string BuildAnalysisPrompt(string instruction)
        {
            return @"你是一个专业的文字校对助手。根据用户的指令分析文本中的错误，输出 JSON 格式的纠错列表。

## 用户指令
" + instruction + @"

## 输出格式要求
严格输出一个 JSON 数组，不要输出任何其他内容（不要 markdown 代码块标记）。
每个元素包含三个字段：
- original: 原文中需修正的精确片段（必须是原文中能匹配到的完整子串，区分大小写，最长 255 字符）
- replacement: 修正后的文本
- reason: 修正原因（简短中文说明）

示例：
[{""original"":""recieve"",""replacement"":""receive"",""reason"":""拼写错误""}]

如果没有发现需要修正的内容，输出空数组 []。
只输出 JSON，不要输出任何前缀或后缀文字。";
        }

        private static List<CorrectionEntry> ParseCorrections(string text)
        {
            // 尝试提取 JSON 数组（兼容 LLM 可能添加的 markdown 代码块）
            string json = text.Trim();
            if (json.Contains("```"))
            {
                int start = json.IndexOf('[');
                int end = json.LastIndexOf(']');
                if (start >= 0 && end > start)
                    json = json.Substring(start, end - start + 1);
            }

            // 去除可能的 <think> 标签
            json = System.Text.RegularExpressions.Regex.Replace(
                json, @"<think>[\s\S]*?</think>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

            if (!json.StartsWith("["))
            {
                int idx = json.IndexOf('[');
                if (idx >= 0) json = json.Substring(idx);
            }
            if (!json.EndsWith("]"))
            {
                int idx = json.LastIndexOf(']');
                if (idx >= 0) json = json.Substring(0, idx + 1);
            }

            try
            {
                var items = JsonSerializer.Deserialize<List<CorrectionEntry>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return items ?? new List<CorrectionEntry>();
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("CorrectTextWorkflow.ParseCorrections", ex);
                return new List<CorrectionEntry>();
            }
        }

        internal class CorrectionEntry
        {
            public string Original { get; set; }
            public string Replacement { get; set; }
            public string Reason { get; set; }
        }
    }
}
