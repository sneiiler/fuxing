using FuXing.Core;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>
    /// 统一的文本编辑工具，支持按光标/选区/节点/文档末尾编辑。
    /// action: insert_at_cursor / replace_selection / append_to_document / insert_at_node / replace_node_content
    /// </summary>
    public class EditDocumentTextTool : ToolBase
    {
        public override string Name => "edit_document_text";
        public override string DisplayName => "编辑文档文本";
        public override ToolCategory Category => ToolCategory.Editing;

        public override string Description =>
            "Insert, replace, or append text. Actions: insert_at_cursor (default), replace_selection, " +
            "append_to_document, insert_at_node (insert at node start/end via node_id from document_graph), " +
            "replace_node_content (replace entire node content).";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["action"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray("insert_at_cursor", "replace_selection",
                        "append_to_document", "insert_at_node", "replace_node_content"),
                    ["description"] = "操作类型（默认 insert_at_cursor）"
                },
                ["text"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "要插入/替换/追加的文本内容"
                },
                ["node_id"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "目标节点 ID 或 label 别名（从 document_graph 获取，用于 insert_at_node / replace_node_content）"
                },
                ["position"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray("start", "end"),
                    ["description"] = "insert_at_node 时插入位置：start=节点开头, end=节点末尾。默认 end"
                }
            },
            ["required"] = new JArray("text")
        };

        public override Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            string text = RequireString(arguments, "text");
            string action = OptionalString(arguments, "action", "insert_at_cursor");
            var doc = RequireActiveDocument(connect);

            switch (action)
            {
                case "insert_at_cursor":
                    return InsertAtCursor(connect, text);

                case "replace_selection":
                    return ReplaceSelection(connect, text);

                case "append_to_document":
                    return AppendToDocument(connect, doc, text);

                case "insert_at_node":
                    return InsertAtNode(connect, doc, arguments, text);

                case "replace_node_content":
                    return ReplaceNodeContent(connect, doc, arguments, text);

                default:
                    throw new ToolArgumentException(
                        $"未知 action: {action}，可选: insert_at_cursor, replace_selection, " +
                        "append_to_document, insert_at_node, replace_node_content");
            }
        }

        private Task<ToolExecutionResult> InsertAtCursor(Connect connect, string text)
        {
            using (BeginTrackRevisions(connect))
            {
                connect.WordApplication.Selection.TypeText(text);
            }
            var doc = connect.WordApplication.ActiveDocument;
            if (doc != null) DocumentGraphCache.Instance.RefreshHash(doc);
            return Task.FromResult(
                ToolExecutionResult.Ok($"已在光标位置插入 {text.Length} 个字符。"));
        }

        private Task<ToolExecutionResult> ReplaceSelection(Connect connect, string text)
        {
            var selection = connect.WordApplication.Selection;
            if (string.IsNullOrEmpty(selection?.Text?.Trim()))
                throw new ToolArgumentException("没有选中的文本");

            int oldLen = selection.Text.Length;
            using (BeginTrackRevisions(connect))
            {
                selection.TypeText(text);
            }
            var doc = connect.WordApplication.ActiveDocument;
            if (doc != null) DocumentGraphCache.Instance.RefreshHash(doc);
            return Task.FromResult(
                ToolExecutionResult.Ok($"已将选中文本（{oldLen}字符）替换为新文本（{text.Length}字符）。"));
        }

        private Task<ToolExecutionResult> AppendToDocument(Connect connect, NetOffice.WordApi.Document doc, string text)
        {
            int endPos = doc.Content.End - 1;
            using (BeginTrackRevisions(connect))
            {
                connect.WordApplication.Selection.SetRange(endPos, endPos);
                connect.WordApplication.Selection.TypeText(text);
            }
            DocumentGraphCache.Instance.RefreshHash(doc);
            return Task.FromResult(
                ToolExecutionResult.Ok($"已在文档末尾追加 {text.Length} 个字符。"));
        }

        private Task<ToolExecutionResult> InsertAtNode(
            Connect connect, NetOffice.WordApi.Document doc, JObject arguments, string text)
        {
            string nodeId = RequireString(arguments, "node_id");
            string position = OptionalString(arguments, "position", "end");

            var graph = DocumentGraphCache.Instance.GetOrBuildAsync(doc).Result;
            var node = graph.ResolveNode(nodeId);
            if (node == null)
                throw new ToolArgumentException(
                    $"节点不存在: {nodeId}。请先调用 document_graph(map) 获取有效节点，或检查 label 是否正确。");
            if (node.AnchorLabel == null)
                throw new ToolArgumentException($"节点 {node.Id} 无锚点（可能是根节点）");

            var range = DocumentGraphCache.Instance.Anchors.GetRange(doc, node.AnchorLabel);
            int targetPos = position == "start" ? range.Start : range.End;

            using (BeginTrackRevisions(connect))
            {
                connect.WordApplication.Selection.SetRange(targetPos, targetPos);
                connect.WordApplication.Selection.TypeText(text);
            }

            DocumentGraphCache.Instance.RefreshHash(doc);

            string posDesc = position == "start" ? "开头" : "末尾";
            return Task.FromResult(
                ToolExecutionResult.Ok(
                    $"已在节点 [{node.Id}] {node.Title} 的{posDesc}插入 {text.Length} 个字符。"));
        }

        private Task<ToolExecutionResult> ReplaceNodeContent(
            Connect connect, NetOffice.WordApi.Document doc, JObject arguments, string text)
        {
            string nodeId = RequireString(arguments, "node_id");

            var graph = DocumentGraphCache.Instance.GetOrBuildAsync(doc).Result;
            var node = graph.ResolveNode(nodeId);
            if (node == null)
                throw new ToolArgumentException(
                    $"节点不存在: {nodeId}。请先调用 document_graph(map) 获取有效节点，或检查 label 是否正确。");
            if (node.AnchorLabel == null)
                throw new ToolArgumentException($"节点 {node.Id} 无锚点（可能是根节点）");

            var range = DocumentGraphCache.Instance.Anchors.GetRange(doc, node.AnchorLabel);
            int oldLen = (range.Text ?? "").Length;

            using (BeginTrackRevisions(connect))
            {
                range.Text = text;
            }

            DocumentGraphCache.Instance.RefreshHash(doc);

            return Task.FromResult(
                ToolExecutionResult.Ok(
                    $"已替换节点 [{node.Id}] {node.Title} 的内容（{oldLen}→{text.Length} 字符）。"));
        }
    }
}
