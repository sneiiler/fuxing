using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>在当前文档中导航到指定标题位置</summary>
    public class NavigateToHeadingTool : ToolBase
    {
        public override string Name => "navigate_to_heading";
        public override string DisplayName => "导航到标题";
        public override ToolCategory Category => ToolCategory.Structure;

        public override string Description =>
            "Move cursor to a heading. Positions: before/after heading, end_of_section.";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["heading_name"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "要导航到的标题文本"
                },
                ["position"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray("before", "after", "end_of_section"),
                    ["description"] = "定位模式：before=标题前, after=标题后（标题段落结束处）, end_of_section=该章节末尾（下一个同级标题之前）"
                }
            },
            ["required"] = new JArray("heading_name", "position")
        };

        public override Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            string headingName = RequireString(arguments, "heading_name");
            string position = RequireString(arguments, "position");

            var doc = RequireActiveDocument(connect);

            var heading = DocumentHelper.FindHeading(doc, headingName);
            if (heading == null)
                return Task.FromResult(ToolExecutionResult.Fail($"未找到标题: {headingName}"));

            int targetPos;
            string posDesc;

            switch (position)
            {
                case "before":
                    targetPos = heading.Paragraph.Range.Start;
                    posDesc = "标题前";
                    break;

                case "after":
                    targetPos = heading.Paragraph.Range.End;
                    posDesc = "标题后";
                    break;

                case "end_of_section":
                    targetPos = DocumentHelper.FindSectionEnd(doc, heading.Paragraph, heading.Level);
                    posDesc = "章节末尾";
                    break;

                default:
                    return Task.FromResult(ToolExecutionResult.Fail(
                        $"无效的 position 值: {position}，应为 before/after/end_of_section"));
            }

            var range = doc.Range(targetPos, targetPos);
            range.Select();

            return Task.FromResult(
                ToolExecutionResult.Ok($"已导航到「{headingName}」的{posDesc}（位置: {targetPos}）"));
        }
    }
}
