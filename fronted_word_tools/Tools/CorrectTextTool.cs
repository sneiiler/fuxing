using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>对文档文本执行 AI 纠错（自动检测：有选中则纠错选中部分，否则纠错全文）</summary>
    public class CorrectTextTool : ToolBase
    {
        public override string Name => "correct_text";
        public override string DisplayName => "文本纠错";
        public override ToolCategory Category => ToolCategory.Editing;

        public override string Description =>
            "AI proofreading: detect and fix typos/errors in Track Changes mode with explanatory comments. " +
            "Operates on selection or entire document. " +
            "PRIMARY tool for any proofreading/error-checking task — use this, not search_and_replace. No parameters.";

        public override JObject Parameters => null;

        public override async Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            var app = connect.WordApplication;
            if (app.Documents.Count == 0)
                return ToolExecutionResult.Fail("没有打开的文档");

            var selection = app.Selection;
            bool hasSelection = selection != null &&
                                selection.Start < selection.End &&
                                !string.IsNullOrEmpty(selection.Text?.Trim());

            var mode = connect.CurrentCorrectionMode;
            var service = TextCorrectionService.FromConfig();

            if (hasSelection)
                return await CorrectSelectedAsync(connect, app, service, mode);
            else
                return await CorrectAllAsync(connect, app, service, mode);
        }

        private async Task<ToolExecutionResult> CorrectSelectedAsync(
            Connect connect, NetOffice.WordApi.Application app,
            TextCorrectionService service, CorrectionMode mode)
        {
            var selection = app.Selection;
            string text = selection.Text.Trim();
            int selStart = selection.Start;
            int selEnd = selection.End;
            var activeDoc = app.ActiveDocument;

            connect.HighlightRangePublic(activeDoc, selStart, selEnd);

            try
            {
                var result = await service.CorrectTextAsync(text, mode);

                if (!result.Success)
                    return ToolExecutionResult.Fail($"纠错失败: {result.ErrorMessage}");

                if (!result.HasCorrections)
                    return ToolExecutionResult.Ok("未发现需要修改的内容，文本质量良好。");

                int applied = connect.ApplyCorrectionsPublic(activeDoc, selStart, selEnd, result.Corrections);

                var details = result.Corrections
                    .Select(c => $"「{c.Original}」→「{c.Replacement}」（{c.Reason}）")
                    .ToList();

                string summary = $"共发现 {result.Corrections.Count} 处问题，已修改 {applied} 处（审阅模式）。\n"
                    + string.Join("\n", details);

                if (!string.IsNullOrEmpty(result.Summary))
                    summary += $"\n总结: {result.Summary}";

                return ToolExecutionResult.Ok(summary);
            }
            finally
            {
                connect.ClearHighlightPublic(activeDoc, selStart, selEnd);
            }
        }

        private async Task<ToolExecutionResult> CorrectAllAsync(
            Connect connect, NetOffice.WordApi.Application app,
            TextCorrectionService service, CorrectionMode mode)
        {
            var activeDoc = app.ActiveDocument;
            string fullText = activeDoc.Content.Text?.Trim();
            if (string.IsNullOrEmpty(fullText))
                return ToolExecutionResult.Fail("文档内容为空");

            var result = await service.CorrectTextAsync(fullText, mode);

            if (!result.Success)
                return ToolExecutionResult.Fail($"全文纠错失败: {result.ErrorMessage}");

            if (!result.HasCorrections)
                return ToolExecutionResult.Ok("全文检查完成，未发现需要修改的内容。");

            int docStart = activeDoc.Content.Start;
            int docEnd = activeDoc.Content.End;
            int applied = connect.ApplyCorrectionsPublic(activeDoc, docStart, docEnd, result.Corrections);

            string summary = $"全文纠错完成: 发现 {result.Corrections.Count} 处问题，已修改 {applied} 处（审阅模式）。";
            if (!string.IsNullOrEmpty(result.Summary))
                summary += $"\n{result.Summary}";

            return ToolExecutionResult.Ok(summary);
        }
    }
}
