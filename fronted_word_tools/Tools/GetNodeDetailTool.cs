using FuXing.SubAgents;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>
    /// 获取文档 AST 节点的详细内容。
    /// 通过 get_document_map 返回的节点 ID 查询具体章节的文本内容。
    /// 类比编码智能体中的 read_file —— 先看 Repo Map，再按需深入。
    /// </summary>
    public class GetNodeDetailTool : ToolBase
    {
        public override string Name => "get_node_detail";
        public override string DisplayName => "获取节点详情";
        public override ToolCategory Category => ToolCategory.Query;

        public override string Description =>
            "Get detailed content of a node in the Document Map. " +
            "Input a node ID (obtained from get_document_map), returns the section's full text and child node list. " +
            "Similar to expanding a file in a code editor — first view the outline, then drill into specific content as needed.";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["node_id"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "节点 ID（从 get_document_map 返回的 hash ID，如 'a3f2e1d0'）"
                },
                ["max_chars"] = new JObject
                {
                    ["type"] = "integer",
                    ["description"] = "最大返回字符数。默认 5000"
                }
            },
            ["required"] = new JArray("node_id")
        };

        private const int DefaultMaxChars = 5000;

        public override async Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            string nodeId = RequireString(arguments, "node_id");
            int maxChars = OptionalInt(arguments, "max_chars", DefaultMaxChars);

            var doc = RequireActiveDocument(connect);
            var map = await DocumentMapCache.Instance.GetOrBuildAsync(doc);

            if (!map.Index.TryGetValue(nodeId, out var node))
                throw new ToolArgumentException(
                    $"节点不存在: {nodeId}。请先调用 get_document_map 获取有效的节点 ID。");

            var sb = new StringBuilder();
            sb.AppendLine($"═══ 节点详情: [{node.NodeId}] {node.Title} ═══");
            sb.AppendLine($"层级: {node.Level}级 | 段落范围: #{node.ParaStart}-#{node.ParaEnd - 1} | 总段落: {node.TotalParaCount}");

            // 子节点列表
            if (node.Children.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("子章节:");
                foreach (var child in node.Children)
                    sb.AppendLine($"  [{child.NodeId}] {child.Level}级: {child.Title} ({child.TotalParaCount}段)");
            }

            // 兄弟节点列表（同层相邻，方便 LLM 横向导航）
            var siblings = FindSiblings(map, node);
            if (siblings.Count > 1)
            {
                sb.AppendLine();
                sb.AppendLine("同级章节:");
                foreach (var sib in siblings)
                {
                    string marker = sib.NodeId == node.NodeId ? " ◀ 当前" : "";
                    sb.AppendLine($"  [{sib.NodeId}] {sib.Title}{marker}");
                }
            }

            // 读取章节文本
            sb.AppendLine();
            sb.AppendLine("───── 内容 ─────");

            int startPos = node.CharStart;
            int endPos = node.CharEnd == -1
                ? doc.Content.End - 1
                : node.CharEnd;

            var range = doc.Range(startPos, endPos);
            string text = range.Text;

            bool truncated = text.Length > maxChars;
            if (truncated)
                text = text.Substring(0, maxChars);

            sb.Append(text);

            if (truncated)
                sb.AppendLine($"\n\n…（已截取前 {maxChars} 字符，章节共 {range.Text.Length} 字符）");

            return ToolExecutionResult.Ok(sb.ToString());
        }

        /// <summary>找到节点的兄弟节点（同一父节点下的所有子节点）</summary>
        private static System.Collections.Generic.List<DocumentAstNode> FindSiblings(
            DocumentMap map, DocumentAstNode target)
        {
            return FindSiblingsRecursive(map.Root, target)
                   ?? new System.Collections.Generic.List<DocumentAstNode>();
        }

        private static System.Collections.Generic.List<DocumentAstNode> FindSiblingsRecursive(
            DocumentAstNode parent, DocumentAstNode target)
        {
            foreach (var child in parent.Children)
            {
                if (child.NodeId == target.NodeId)
                    return parent.Children;

                var found = FindSiblingsRecursive(child, target);
                if (found != null) return found;
            }

            return null;
        }
    }
}
