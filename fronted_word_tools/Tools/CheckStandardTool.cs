using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>校验选中文本是否符合标准规范</summary>
    public class CheckStandardTool : ITool
    {
        public string Name => "check_standard";

        public string Description =>
            "校验用户选中的文本是否符合相关标准规范。将查询结果以批注形式添加到文档。";

        public JObject Parameters => null;

        public Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            var app = connect.WordApplication;
            var selection = app.Selection;
            string text = selection?.Text?.Trim();
            if (string.IsNullOrEmpty(text))
                return Task.FromResult(ToolExecutionResult.Fail("没有选中的文本"));

            var networkHelper = new NetWorkHelper();
            string result = networkHelper.SendStandardCheckRequest(text);

            var paragraphs = selection.Paragraphs;
            if (paragraphs.Count > 0)
            {
                var firstParagraph = paragraphs[1];
                var range = firstParagraph.Range.Duplicate;
                range.Start = firstParagraph.Range.End - 2;
                range.End = firstParagraph.Range.End;
                connect.AddCommentPublic(range, result);
            }

            return Task.FromResult(ToolExecutionResult.Ok($"标准校验完成。\n{result}"));
        }
    }
}
