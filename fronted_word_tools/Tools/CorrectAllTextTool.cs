using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>对全文执行 AI 纠错</summary>
    public class CorrectAllTextTool : ITool
    {
        public string Name => "correct_all_text";

        public string Description =>
            "对 Word 文档的全文执行 AI 纠错。按段落分块处理，逐块分析并以批注形式添加修改建议。";

        public JObject Parameters => null;

        public async Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            var app = connect.WordApplication;
            if (app.Documents.Count == 0)
                return ToolExecutionResult.Fail("没有打开的文档");

            var activeDoc = app.ActiveDocument;
            string fullText = activeDoc.Content.Text?.Trim();
            if (string.IsNullOrEmpty(fullText))
                return ToolExecutionResult.Fail("文档内容为空");

            var service = TextCorrectionService.FromConfig();
            var result = await service.CorrectTextAsync(fullText);

            if (!result.Success)
                return ToolExecutionResult.Fail($"全文纠错失败: {result.ErrorMessage}");

            if (!result.HasCorrections)
                return ToolExecutionResult.Ok("全文检查完成，未发现需要修改的内容。");

            int docStart = activeDoc.Content.Start;
            int docEnd = activeDoc.Content.End;
            int applied = connect.ApplyCorrectionsPublic(activeDoc, docStart, docEnd, result.Corrections);

            string summary = $"全文纠错完成: 发现 {result.Corrections.Count} 处问题，已添加 {applied} 条批注。";
            if (!string.IsNullOrEmpty(result.Summary))
                summary += $"\n{result.Summary}";

            return ToolExecutionResult.Ok(summary);
        }
    }
}
