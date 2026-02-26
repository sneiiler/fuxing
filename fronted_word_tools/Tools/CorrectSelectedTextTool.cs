using Newtonsoft.Json.Linq;
using System.Linq;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>对用户选中的文本执行 AI 纠错</summary>
    public class CorrectSelectedTextTool : ITool
    {
        public string Name => "correct_selected_text";

        public string Description =>
            "对 Word 文档中用户当前选中的文本执行 AI 纠错。自动高亮标注错误位置并以批注形式添加修改建议。";

        public JObject Parameters => null;

        public async Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            var app = connect.WordApplication;
            var selection = app.Selection;
            string text = selection?.Text?.Trim();
            if (string.IsNullOrEmpty(text))
                return ToolExecutionResult.Fail("没有选中的文本");

            int selStart = selection.Start;
            int selEnd = selection.End;
            var activeDoc = app.ActiveDocument;

            connect.HighlightRangePublic(activeDoc, selStart, selEnd);

            try
            {
                var service = TextCorrectionService.FromConfig();
                var result = await service.CorrectTextAsync(text);

                if (!result.Success)
                    return ToolExecutionResult.Fail($"纠错失败: {result.ErrorMessage}");

                if (!result.HasCorrections)
                    return ToolExecutionResult.Ok("未发现需要修改的内容，文本质量良好。");

                int applied = connect.ApplyCorrectionsPublic(activeDoc, selStart, selEnd, result.Corrections);

                var details = result.Corrections
                    .Select(c => $"「{c.Original}」→「{c.Replacement}」（{c.Reason}）")
                    .ToList();

                string summary = $"共发现 {result.Corrections.Count} 处问题，已添加 {applied} 条批注。\n"
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
    }
}
