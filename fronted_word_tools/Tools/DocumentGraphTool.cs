using FuXing.Core;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FuXing
{
    // ═══════════════════════════════════════════════════════════════
    //  文档图工具（统一入口）
    //
    //  万物皆节点：Section / Table / Image / TextBlock / List / Paragraph
    //  逐层 expand 按需展开，所有节点都可被 AI 赋予 label 别名。
    //  编辑工具通过 node_id 或 label 直接定位，行为一致。
    //
    //  五个 action：map / expand / read / goto / label
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 文档图工具。LLM 通过此工具感知文档结构、定位内容、赋予别名。
    /// </summary>
    public class DocumentGraphTool : ToolBase
    {
        public override string Name => "document_graph";
        public override string DisplayName => "文档图";
        public override ToolCategory Category => ToolCategory.Query;

        public override string Description =>
            "Navigate document structure via graph model (万物皆节点). " +
            "Actions: map (get L1 structure), expand (Section→tables/images/textblocks, TextBlock→paragraphs), " +
            "read (read node content), goto (move cursor to node), label (assign alias to node). " +
            "Each node is backed by a ContentControl — stable across edits. " +
            "Use node_id or label in edit/format/delete tools for precise positioning.";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["action"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray("map", "expand", "read", "goto", "label"),
                    ["description"] =
                        "map: 获取文档图（L1 标题骨架）; " +
                        "expand: 展开节点（Section→发现内部元素, TextBlock→段落）; " +
                        "read: 读取节点内容; " +
                        "goto: 光标跳到节点; " +
                        "label: 给节点赋予别名（多步操作中的稳定引用）"
                },
                ["node_id"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "目标节点 ID（如 s01）、label 别名、或 \"selected\"（自动定位光标所在节点）"
                },
                ["deep"] = new JObject
                {
                    ["type"] = "boolean",
                    ["description"] = "map 时启用深度感知（LLM 推断标题），默认 false"
                },
                ["force_rebuild"] = new JObject
                {
                    ["type"] = "boolean",
                    ["description"] = "map 时强制重建图（忽略缓存），默认 false"
                },
                ["position"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray("start", "end"),
                    ["description"] = "goto 时光标位置：start=节点开头, end=节点末尾。默认 start"
                },
                ["max_chars"] = new JObject
                {
                    ["type"] = "integer",
                    ["description"] = "read 时最大返回字符数（默认 5000）"
                },
                ["label"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "label 动作时要赋予的别名。传空字符串可清除别名。"
                },
                ["heading_name"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "read/goto 时按标题名定位（不依赖图）"
                },
                ["file_path"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "read 时读取外部 .docx 文件（而非当前文档）"
                }
            },
            ["required"] = new JArray("action")
        };

        private const int DefaultMaxChars = 5000;

        public override async Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            string action = RequireString(arguments, "action");

            switch (action)
            {
                case "map": return await ActionMap(connect, arguments);
                case "expand": return ActionExpand(connect, arguments);
                case "read": return await ActionRead(connect, arguments);
                case "goto": return ActionGoto(connect, arguments);
                case "label": return ActionLabel(connect, arguments);
                default:
                    throw new ToolArgumentException(
                        $"无效 action: {action}，支持: map/expand/read/goto/label");
            }
        }

        // ═══════════════════════════════════════════════════
        //  map — 获取文档图
        // ═══════════════════════════════════════════════════

        private async Task<ToolExecutionResult> ActionMap(Connect connect, JObject arguments)
        {
            var doc = RequireActiveDocument(connect);
            bool deep = OptionalBool(arguments, "deep", false);
            bool forceRebuild = OptionalBool(arguments, "force_rebuild", false);

            if (forceRebuild)
                DocumentGraphCache.Instance.Invalidate(doc);

            var graph = await DocumentGraphCache.Instance.GetOrBuildAsync(doc, deep);
            return ToolExecutionResult.Ok(graph.ToGraphText());
        }

        // ═══════════════════════════════════════════════════
        //  expand — 展开章节
        // ═══════════════════════════════════════════════════

        private ToolExecutionResult ActionExpand(Connect connect, JObject arguments)
        {
            var doc = RequireActiveDocument(connect);
            string nodeIdOrLabel = RequireString(arguments, "node_id");

            // 先获取图以解析 node_id（支持 "selected" 关键词）
            var graph = DocumentGraphCache.Instance.GetOrBuildAsync(doc).Result;
            var node = ResolveNodeOrSelection(graph, nodeIdOrLabel, connect);
            if (node == null)
                throw new ToolArgumentException(
                    $"节点不存在: {nodeIdOrLabel}。请先调用 document_graph(map) 获取有效节点。");

            DocumentGraphCache.Instance.ExpandNode(doc, node.Id);

            // 重新获取节点（expand 后 ChildIds 已更新）
            node = graph.GetById(node.Id);

            var sb = new StringBuilder();
            sb.AppendLine($"已展开 [{node.Id}] {node.Title}，发现 {node.ChildIds.Count} 个子元素：");
            sb.AppendLine();

            foreach (var childId in node.ChildIds)
            {
                var child = graph.GetById(childId);
                if (child == null) continue;

                string icon = GetTypeIcon(child.Type);
                string expandable = (child.Type == DocNodeType.TextBlock && !child.Expanded)
                    ? " [可展开→段落]" : "";
                string preview = !string.IsNullOrEmpty(child.Preview)
                    ? $" \"{child.Preview}\""
                    : "";
                sb.AppendLine($"  [{child.Id}] {icon} {child.Title}{expandable}{preview}");
            }

            return ToolExecutionResult.Ok(sb.ToString());
        }

        // ═══════════════════════════════════════════════════
        //  read — 读取节点内容
        // ═══════════════════════════════════════════════════

        private async Task<ToolExecutionResult> ActionRead(Connect connect, JObject arguments)
        {
            string nodeId = OptionalString(arguments, "node_id");
            string filePath = OptionalString(arguments, "file_path");
            string headingName = OptionalString(arguments, "heading_name");
            int maxChars = OptionalInt(arguments, "max_chars", DefaultMaxChars);

            // 外部文件读取（不使用图，直接读取）
            if (!string.IsNullOrWhiteSpace(filePath))
                return ReadExternalDocument(connect, filePath, headingName, maxChars);

            var doc = RequireActiveDocument(connect);

            // 按节点 ID 读取
            if (!string.IsNullOrWhiteSpace(nodeId))
                return await ReadByNodeId(connect, doc, nodeId, maxChars);

            // 按标题名读取
            if (!string.IsNullOrWhiteSpace(headingName))
                return ReadByHeadingName(doc, headingName, maxChars);

            // 全文读取
            return ReadFullText(doc, maxChars);
        }

        private async Task<ToolExecutionResult> ReadByNodeId(
            Connect connect, NetOffice.WordApi.Document doc, string nodeIdOrLabel, int maxChars)
        {
            var graph = await DocumentGraphCache.Instance.GetOrBuildAsync(doc);
            var node = ResolveNodeOrSelection(graph, nodeIdOrLabel, connect);
            if (node == null)
                throw new ToolArgumentException(
                    $"节点不存在: {nodeIdOrLabel}。请先调用 document_graph(map) 获取有效节点。");

            if (node.AnchorLabel == null && (node.Meta == null
                || !node.Meta.ContainsKey("range_start") || !node.Meta.ContainsKey("range_end")))
                throw new ToolArgumentException($"节点 {node.Id} 无锚点且无位置元数据（可能是根节点）");

            var range = DocumentGraphCache.Instance.GetNodeRange(doc, node);

            var sb = new StringBuilder();
            sb.AppendLine($"═══ [{node.Id}] {node.Title} ═══");
            sb.AppendLine($"类型: {node.Type} | 锚点: {node.AnchorLabel}");

            // 导航上下文
            if (node.ChildIds.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("子节点:");
                foreach (var childId in node.ChildIds)
                {
                    var child = graph.GetById(childId);
                    if (child == null) continue;
                    sb.AppendLine($"  [{child.Id}] {GetTypeIcon(child.Type)} {child.Title}");
                }
            }

            if (node.PrevId != null || node.NextId != null)
            {
                sb.AppendLine();
                sb.Append("导航: ");
                if (node.PrevId != null)
                {
                    var prev = graph.GetById(node.PrevId);
                    sb.Append($"← [{prev?.Id}] {prev?.Title}  ");
                }
                if (node.NextId != null)
                {
                    var next = graph.GetById(node.NextId);
                    sb.Append($"→ [{next?.Id}] {next?.Title}");
                }
                sb.AppendLine();
            }

            // 内容
            sb.AppendLine();
            sb.AppendLine("───── 内容 ─────");

            string text = range.Text ?? "";
            bool truncated = text.Length > maxChars;
            if (truncated)
                text = text.Substring(0, maxChars);

            sb.Append(text);

            if (truncated)
                sb.AppendLine($"\n\n…（已截取前 {maxChars} 字符，共 {range.Text.Length} 字符）");

            return ToolExecutionResult.Ok(sb.ToString());
        }

        private ToolExecutionResult ReadByHeadingName(
            NetOffice.WordApi.Document doc, string headingName, int maxChars)
        {
            var heading = DocumentHelper.FindHeading(doc, headingName);
            if (heading == null)
                throw new ToolArgumentException($"未找到标题: {headingName}");

            var (start, end) = DocumentHelper.GetSectionRange(
                doc, heading.Paragraph, heading.Level, true);
            var range = doc.Range(start, end);

            string text = range.Text ?? "";
            bool truncated = text.Length > maxChars;
            if (truncated) text = text.Substring(0, maxChars);

            var sb = new StringBuilder();
            sb.AppendLine($"章节: {headingName}（{heading.Level}级）");
            sb.AppendLine($"字符数: {range.Text.Length}");
            sb.AppendLine();
            sb.Append(text);

            if (truncated)
                sb.AppendLine($"\n\n…（已截取前 {maxChars} 字符）");

            return ToolExecutionResult.Ok(sb.ToString());
        }

        private ToolExecutionResult ReadFullText(
            NetOffice.WordApi.Document doc, int maxChars)
        {
            string text = doc.Content.Text;
            bool truncated = text.Length > maxChars;
            if (truncated) text = text.Substring(0, maxChars);

            var sb = new StringBuilder();
            sb.AppendLine($"文档: {doc.Name} | 字符: {doc.Content.Text.Length}");
            sb.AppendLine();
            sb.Append(text);

            if (truncated)
                sb.AppendLine($"\n\n…（已截取前 {maxChars} 字符）");

            return ToolExecutionResult.Ok(sb.ToString());
        }

        private ToolExecutionResult ReadExternalDocument(
            Connect connect, string filePath, string headingName, int maxChars)
        {
            if (!System.IO.File.Exists(filePath))
                throw new ToolArgumentException($"文件不存在: {filePath}");

            var app = connect.WordApplication;
            var (doc, shouldClose) = DocumentHelper.GetOrOpenReadOnly(app, filePath);

            try
            {
                if (!string.IsNullOrWhiteSpace(headingName))
                    return ReadByHeadingName(doc, headingName, maxChars);
                return ReadFullText(doc, maxChars);
            }
            finally
            {
                if (shouldClose)
                    doc.Close(NetOffice.WordApi.Enums.WdSaveOptions.wdDoNotSaveChanges);
            }
        }

        // ═══════════════════════════════════════════════════
        //  goto — 光标跳到节点
        // ═══════════════════════════════════════════════════

        private ToolExecutionResult ActionGoto(Connect connect, JObject arguments)
        {
            var doc = RequireActiveDocument(connect);
            string nodeIdOrLabel = OptionalString(arguments, "node_id");
            string headingName = OptionalString(arguments, "heading_name");
            string position = OptionalString(arguments, "position", "start");

            NetOffice.WordApi.Range range;
            string targetDesc;

            if (!string.IsNullOrWhiteSpace(nodeIdOrLabel))
            {
                var graph = DocumentGraphCache.Instance.GetOrBuildAsync(doc).Result;
                var node = ResolveNodeOrSelection(graph, nodeIdOrLabel, connect);
                if (node == null)
                    throw new ToolArgumentException($"节点不存在: {nodeIdOrLabel}");
                if (node.AnchorLabel == null && (node.Meta == null
                    || !node.Meta.ContainsKey("range_start") || !node.Meta.ContainsKey("range_end")))
                    throw new ToolArgumentException($"节点 {node.Id} 无锚点且无位置元数据");

                range = DocumentGraphCache.Instance.GetNodeRange(doc, node);
                targetDesc = $"[{node.Id}] {node.Title}";
            }
            else if (!string.IsNullOrWhiteSpace(headingName))
            {
                var heading = DocumentHelper.FindHeading(doc, headingName);
                if (heading == null)
                    throw new ToolArgumentException($"未找到标题: {headingName}");
                range = heading.Paragraph.Range;
                targetDesc = headingName;
            }
            else
            {
                throw new ToolArgumentException("goto 需要 node_id 或 heading_name");
            }

            int targetPos = position == "end" ? range.End : range.Start;
            doc.Range(targetPos, targetPos).Select();

            return ToolExecutionResult.Ok(
                $"已导航到「{targetDesc}」的{(position == "end" ? "末尾" : "开头")}");
        }

        // ═══════════════════════════════════════════════════
        //  label — 给节点赋予别名
        // ═══════════════════════════════════════════════════

        private ToolExecutionResult ActionLabel(Connect connect, JObject arguments)
        {
            var doc = RequireActiveDocument(connect);
            string nodeIdOrLabel = RequireString(arguments, "node_id");
            string label = OptionalString(arguments, "label");

            var graph = DocumentGraphCache.Instance.GetOrBuildAsync(doc).Result;
            var node = ResolveNodeOrSelection(graph, nodeIdOrLabel, connect);
            if (node == null)
                throw new ToolArgumentException($"节点不存在: {nodeIdOrLabel}");

            string oldLabel = node.Label;

            if (string.IsNullOrWhiteSpace(label))
            {
                // 清除 label
                graph.SetLabel(node.Id, null);
                return ToolExecutionResult.Ok(
                    $"已清除节点 [{node.Id}] {node.Title} 的别名" +
                    (oldLabel != null ? $"（原: {oldLabel}）" : "") + "。");
            }

            // 设置 label
            graph.SetLabel(node.Id, label);
            return ToolExecutionResult.Ok(
                $"已为节点 [{node.Id}] {node.Title} 设置别名: \"{label}\"。" +
                $"\n后续可用 \"{label}\" 替代 \"{node.Id}\" 作为 node_id 参数。");
        }

        // ═══════════════════════════════════════════════════
        //  辅助
        // ═══════════════════════════════════════════════════

        /// <summary>
        /// 解析 node_id 参数。支持三种形式：
        /// 1. 真实节点 ID（如 "s01"）
        /// 2. label 别名（如 "intro"）
        /// 3. "selected" 关键词：按当前光标/选区位置查找最精确的图节点
        /// </summary>
        private static DocNode ResolveNodeOrSelection(
            DocumentGraph graph, string nodeIdOrLabel, Connect connect)
        {
            if (string.IsNullOrWhiteSpace(nodeIdOrLabel)) return null;

            // 处理 "selected" / "selection" 特殊关键词
            string lower = nodeIdOrLabel.Trim().ToLowerInvariant();
            if (lower == "selected" || lower == "selection" || lower == "current")
            {
                // 优先使用发送消息时捕获的光标快照（避免生成过程中用户移动光标）
                int pos;
                var snapshot = connect.SelectionSnapshot;
                if (snapshot != null)
                {
                    pos = snapshot.Start;
                }
                else
                {
                    // 无快照时回退到实时 Selection（直接调用 API 等场景）
                    var app = connect.WordApplication;
                    pos = app.Selection.Start;
                }
                var node = graph.FindNodeAtPosition(pos);
                if (node != null) return node;
                throw new ToolArgumentException(
                    $"光标位置 {pos} 未匹配到文档图节点。请先调用 document_graph(map) 构建文档图。");
            }

            return graph.ResolveNode(nodeIdOrLabel);
        }

        private static string GetTypeIcon(DocNodeType type)
        {
            switch (type)
            {
                case DocNodeType.Section: return "§";
                case DocNodeType.Table: return "📋";
                case DocNodeType.Image: return "🖼";
                case DocNodeType.TextBlock: return "📝";
                case DocNodeType.List: return "📌";
                default: return "•";
            }
        }
    }
}
