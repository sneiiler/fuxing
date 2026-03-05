using Newtonsoft.Json.Linq;
using NetOffice.WordApi;

namespace FuXing
{
    /// <summary>
    /// 在文档中添加批注（Comment）。
    /// 用于审阅场景，AI 提出修改建议而非直接覆盖原文。
    /// </summary>
    public class AddCommentTool : ToolBase
    {
        public override string Name => "add_comment";
        public override string DisplayName => "添加批注";
        public override ToolCategory Category => ToolCategory.Editing;

        public override string Description =>
            "Add review comment on selected text or searched text (target: selection/search). " +
            "Use instead of direct editing when suggesting changes for user review.";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["target"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray("selection", "search"),
                    ["description"] = "定位方式（默认 selection）"
                },
                ["search_text"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "要批注的文本（target=search 时必填）"
                },
                ["comment"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "批注内容"
                }
            },
            ["required"] = new JArray("comment")
        };

        public override System.Threading.Tasks.Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            var doc = RequireActiveDocument(connect);
            string commentText = RequireString(arguments, "comment");
            string target = OptionalString(arguments, "target", "selection");
            string searchText = OptionalString(arguments, "search_text");

            Range targetRange;

            if (target == "search")
            {
                if (string.IsNullOrWhiteSpace(searchText))
                    throw new ToolArgumentException("target=search 时必须提供 search_text");

                var range = doc.Content;
                range.Find.ClearFormatting();
                range.Find.Text = searchText;
                range.Find.Forward = true;
                range.Find.Wrap = NetOffice.WordApi.Enums.WdFindWrap.wdFindStop;

                if (!range.Find.Execute())
                    throw new ToolArgumentException($"未找到文本: {searchText}");

                targetRange = range;
            }
            else
            {
                var selection = connect.WordApplication.Selection;
                if (string.IsNullOrEmpty(selection?.Text?.Trim()))
                    throw new ToolArgumentException("没有选中的文本，请先选中要批注的内容");

                targetRange = selection.Range;
            }

            doc.Comments.Add(targetRange, commentText);

            string rangePreview = targetRange.Text;
            if (rangePreview.Length > 50)
                rangePreview = rangePreview.Substring(0, 47) + "...";

            return System.Threading.Tasks.Task.FromResult(
                ToolExecutionResult.Ok($"已在「{rangePreview}」处添加批注: {commentText}"));
        }
    }
}
