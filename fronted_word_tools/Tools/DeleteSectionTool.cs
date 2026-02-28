using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>删除文档中指定标题的整个章节（标题+内容）或仅删除标题下的内容</summary>
    public class DeleteSectionTool : ToolBase
    {
        public override string Name => "delete_section";
        public override string DisplayName => "删除章节";
        public override ToolCategory Category => ToolCategory.Editing;
        public override bool RequiresApproval => true;

        public override string Description =>
            "Delete section by heading name. include_heading: true=delete all (default), false=clear body only. Track Changes mode.";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["heading_name"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "要删除的章节标题名称（精确匹配）"
                },
                ["include_heading"] = new JObject
                {
                    ["type"] = "boolean",
                    ["description"] = "是否同时删除标题本身。true=删除标题和内容（默认），false=仅删除标题下的正文内容、保留标题"
                }
            },
            ["required"] = new JArray("heading_name")
        };

        public override Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            string headingName = RequireString(arguments, "heading_name");
            bool includeHeading = OptionalBool(arguments, "include_heading", true);

            var doc = RequireActiveDocument(connect);

            var heading = DocumentHelper.FindHeading(doc, headingName);
            if (heading == null)
                return Task.FromResult(ToolExecutionResult.Fail($"未找到标题: {headingName}"));

            var (start, end) = DocumentHelper.GetSectionRange(doc, heading.Paragraph, heading.Level, includeHeading);

            if (start >= end)
            {
                string msg = includeHeading
                    ? "该标题下没有内容，且标题本身是零长度范围"
                    : "该标题下没有内容可删除";
                return Task.FromResult(ToolExecutionResult.Ok(msg));
            }

            var deleteRange = doc.Range(start, end);
            int deletedChars = deleteRange.Text.Length;

            using (BeginTrackRevisions(connect))
            {
                deleteRange.Delete();
            }

            string scope = includeHeading ? "标题及其内容" : "标题下的内容（保留标题）";
            return Task.FromResult(
                ToolExecutionResult.Ok($"已删除「{headingName}」的{scope}（删除字符数: {deletedChars}）"));
        }
    }
}
