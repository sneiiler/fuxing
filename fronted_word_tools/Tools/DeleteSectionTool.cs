using FuXing.Core;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>删除文档中指定章节（按标题名或节点 ID 定位）</summary>
    public class DeleteSectionTool : ToolBase
    {
        public override string Name => "delete_section";
        public override string DisplayName => "删除章节";
        public override ToolCategory Category => ToolCategory.Editing;
        public override bool RequiresApproval => true;

        public override string Description =>
            "Delete section by heading_name or node_id (from document_graph). " +
            "include_heading: true=delete all (default), false=clear body only. Track Changes mode.";

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
                ["node_id"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "目标节点 ID（从 document_graph 获取，优先于 heading_name）"
                },
                ["include_heading"] = new JObject
                {
                    ["type"] = "boolean",
                    ["description"] = "是否同时删除标题本身。true=删除标题和内容（默认），false=仅删除标题下的正文内容、保留标题"
                }
            }
        };

        public override Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            string nodeId = OptionalString(arguments, "node_id");
            string headingName = OptionalString(arguments, "heading_name");
            bool includeHeading = OptionalBool(arguments, "include_heading", true);

            var doc = RequireActiveDocument(connect);

            NetOffice.WordApi.Range deleteRange;
            string targetDesc;

            if (!string.IsNullOrWhiteSpace(nodeId))
            {
                // 按节点 ID 定位
                var graph = DocumentGraphCache.Instance.GetOrBuildAsync(doc).Result;
                var node = graph.ResolveNode(nodeId);
                if (node == null)
                    throw new ToolArgumentException(
                        $"节点不存在: {nodeId}。请先调用 document_graph(map) 获取有效节点，或检查 label 是否正确。");
                var range = DocumentGraphCache.Instance.GetNodeRange(doc, node);
                deleteRange = range;
                targetDesc = $"[{node.Id}] {node.Title}";

                // Section 节点的 CC 仅覆盖正文，如果要连标题一起删除需扩展范围
                if (includeHeading && node.Type == DocNodeType.Section
                    && node.Meta != null && node.Meta.TryGetValue("heading_start", out var hs))
                {
                    int headingStart = int.Parse(hs);
                    deleteRange = doc.Range(headingStart, range.End);
                }
            }
            else if (!string.IsNullOrWhiteSpace(headingName))
            {
                // 按标题名定位（兼容无图场景）
                var heading = DocumentGraphBuilder.FindHeading(doc, headingName);
                if (heading == null)
                    return Task.FromResult(ToolExecutionResult.Fail($"未找到标题: {headingName}"));

                var (start, end) = DocumentGraphBuilder.GetSectionRange(
                    doc, heading.Paragraph, heading.Level, includeHeading);
                deleteRange = doc.Range(start, end);
                targetDesc = headingName;
            }
            else
            {
                throw new ToolArgumentException("需要 node_id 或 heading_name 参数");
            }

            if (deleteRange.Start >= deleteRange.End)
            {
                string msg = includeHeading
                    ? "该目标范围为空"
                    : "该标题下没有内容可删除";
                return Task.FromResult(ToolExecutionResult.Ok(msg));
            }

            int deletedChars = deleteRange.Text.Length;

            using (BeginTrackRevisions(connect))
            {
                deleteRange.Delete();
            }

            // 删除后不重建图——CC 自动跟踪位置
            // 但必须失效缓存，因为被删的节点对应的 CC 已不存在
            DocumentGraphCache.Instance.Invalidate(doc);

            string scope = includeHeading ? "标题及其内容" : "标题下的内容（保留标题）";
            return Task.FromResult(
                ToolExecutionResult.Ok($"已删除「{targetDesc}」的{scope}（删除字符数: {deletedChars}）"));
        }
    }
}
