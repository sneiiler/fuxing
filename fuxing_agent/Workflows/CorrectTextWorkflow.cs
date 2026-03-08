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
    public class CorrectTextWorkflow
    {
        private const string WorkflowName = "correct_text_workflow";
        private const string WorkflowDisplayName = "文本纠错工作流";
        private const int TotalSteps = 3;

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
            [Description("纠错指令，描述要检查或修正的内容")] string instruction,
            [Description("修正范围: all=全文, selection=仅选区")] string scope = "all")
        {
            WorkflowProgressReporter.StartWorkflow(WorkflowName, WorkflowDisplayName, TotalSteps);

            try
            {
                if (string.IsNullOrWhiteSpace(instruction))
                    throw new ArgumentException("缺少 instruction 参数");

                // Step 1: STA 线程一次性读取文档文本到内存
                WorkflowProgressReporter.StartStep(WorkflowName, 1, TotalSteps, "读取文档内容", $"范围: {scope}");
                string contentText = StaHelper.RunOnSta(() =>
                {
                    var app = _connect.WordApplication;
                    var doc = app.ActiveDocument ?? throw new InvalidOperationException("没有活动文档");
                    return ReadTargetContent(app, doc, scope);
                });
                if (string.IsNullOrWhiteSpace(contentText))
                    throw new InvalidOperationException("目标范围没有文本内容");
                DebugLogger.Instance.LogInfo($"[CorrectTextWorkflow] Step1 读取完成，{contentText.Length} 字符");
                WorkflowProgressReporter.FinishStep(WorkflowName, 1, TotalSteps, "读取文档内容", true, $"已读取 {contentText.Length} 字符");

                // Step 2: 后台线程调用 LLM 分析（不阻塞 UI）
                WorkflowProgressReporter.StartStep(WorkflowName, 2, TotalSteps, "分析错误项", "调用模型生成修正列表");
                var corrections = AnalyzeErrors(contentText, instruction);
                if (corrections == null || corrections.Count == 0)
                {
                    WorkflowProgressReporter.FinishStep(WorkflowName, 2, TotalSteps, "分析错误项", true, "未发现需要修正的内容");
                    WorkflowProgressReporter.FinishWorkflow(WorkflowName, WorkflowDisplayName, TotalSteps, true, "分析完成，无需修改");
                    return "分析完成，未发现需要修正的内容。";
                }

                DebugLogger.Instance.LogInfo($"[CorrectTextWorkflow] Step2 分析完成，发现 {corrections.Count} 处待修正");
                WorkflowProgressReporter.FinishStep(WorkflowName, 2, TotalSteps, "分析错误项", true, $"发现 {corrections.Count} 处待修正");

                // Step 3: STA 线程写回修改
                WorkflowProgressReporter.StartStep(WorkflowName, 3, TotalSteps, "应用修正", "写入文档并补充修订说明");
                string result = StaHelper.RunOnSta(() => ApplyCorrections(corrections));
                DebugLogger.Instance.LogInfo("[CorrectTextWorkflow] Step3 修正完成");
                WorkflowProgressReporter.FinishStep(WorkflowName, 3, TotalSteps, "应用修正", true, "已完成文档修正");
                WorkflowProgressReporter.FinishWorkflow(WorkflowName, WorkflowDisplayName, TotalSteps, true, "文本纠错已完成");

                return result;
            }
            catch (Exception ex)
            {
                WorkflowProgressReporter.FinishWorkflow(WorkflowName, WorkflowDisplayName, TotalSteps, false, ex.Message);
                throw;
            }
        }

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

                string editResult = _editTool.edit_content(
                    find_text: item.Original,
                    replace_text: item.Replacement,
                    match_case: true);

                if (editResult.Contains("未找到"))
                {
                    notFound++;
                    details.AppendLine($"  未找到 \"{item.Original}\"");
                }
                else
                {
                    applied++;
                    details.AppendLine($"  \"{item.Original}\" -> \"{item.Replacement}\" ({item.Reason}) {editResult}");

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
严格输出一个 JSON 数组，不要输出任何其他内容，也不要输出 markdown 代码块。
每个元素包含三个字段：
- original: 原文中需修正的精确片段，必须能在原文中直接匹配
- replacement: 修正后的文本
- reason: 修正原因，简短中文说明

示例：
[{""original"":""recieve"",""replacement"":""receive"",""reason"":""拼写错误""}]

如果没有发现需要修正的内容，输出 []。
只输出 JSON。";
        }

        private static List<CorrectionEntry> ParseCorrections(string text)
        {
            string json = text.Trim();
            if (json.Contains("```"))
            {
                int start = json.IndexOf('[');
                int end = json.LastIndexOf(']');
                if (start >= 0 && end > start)
                    json = json.Substring(start, end - start + 1);
            }

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
