using FuXing.SubAgents;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Missing = System.Type;

namespace FuXing
{
    /// <summary>
    /// 统一的文档章节读取工具，合并原 ReadSectionTextTool 和 GetNodeDetailTool。
    /// 支持两种定位方式（节点 ID / 标题名称）和两种目标（当前文档 / 外部文件）。
    /// </summary>
    public class ReadDocumentSectionTool : ToolBase
    {
        public override string Name => "read_document_section";
        public override string DisplayName => "读取文档章节";
        public override ToolCategory Category => ToolCategory.Query;

        public override string Description =>
            "Read document section content. Locate by node_id (from get_document_map, with AST context) " +
            "or heading_name (exact match). Omit both for full text. file_path: read external .docx instead of active document.";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["node_id"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "节点 ID（从 get_document_map 获取，仅用于当前活动文档）"
                },
                ["heading_name"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "章节标题名称（精确匹配），适用于当前文档和外部文件"
                },
                ["file_path"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "外部 .docx 文件路径。为空则读取当前活动文档"
                },
                ["max_chars"] = new JObject
                {
                    ["type"] = "integer",
                    ["description"] = "最大返回字符数（默认 5000）"
                }
            }
        };

        private const int DefaultMaxChars = 5000;

        public override async Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            string nodeId = OptionalString(arguments, "node_id");
            string headingName = OptionalString(arguments, "heading_name");
            string filePath = OptionalString(arguments, "file_path");
            int maxChars = OptionalInt(arguments, "max_chars", DefaultMaxChars);

            bool isExternal = !string.IsNullOrWhiteSpace(filePath);

            // node_id 不能用于外部文件
            if (isExternal && !string.IsNullOrWhiteSpace(nodeId))
                throw new ToolArgumentException("node_id 仅适用于当前活动文档，不能与 file_path 同时使用");

            // node_id 和 heading_name 不能同时指定
            if (!string.IsNullOrWhiteSpace(nodeId) && !string.IsNullOrWhiteSpace(headingName))
                throw new ToolArgumentException("node_id 和 heading_name 不能同时指定，请选择一种定位方式");

            // ── 路由到具体实现 ──

            if (!string.IsNullOrWhiteSpace(nodeId))
                return await ReadByNodeId(connect, nodeId, maxChars);

            if (isExternal)
                return ReadExternalDocument(connect, filePath, headingName, maxChars);

            // 当前文档 + heading_name 或全文
            var doc = RequireActiveDocument(connect);
            if (!string.IsNullOrWhiteSpace(headingName))
                return ReadHeadingSection(doc, headingName, maxChars);

            return ReadFullText(doc, maxChars);
        }

        // ═══════════════════════════════════════════════════
        //  模式 1: 通过 AST 节点 ID 读取（仅当前文档）
        // ═══════════════════════════════════════════════════

        private async Task<ToolExecutionResult> ReadByNodeId(Connect connect, string nodeId, int maxChars)
        {
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

            // 兄弟节点列表
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
            int endPos = node.CharEnd == -1 ? doc.Content.End - 1 : node.CharEnd;
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

        // ═══════════════════════════════════════════════════
        //  模式 2: 读取外部文档
        // ═══════════════════════════════════════════════════

        private ToolExecutionResult ReadExternalDocument(Connect connect, string filePath, string headingName, int maxChars)
        {
            if (!System.IO.File.Exists(filePath))
                throw new ToolArgumentException($"文件不存在: {filePath}");

            var app = connect.WordApplication;
            var m = Missing.Missing;
            var doc = app.Documents.Open(filePath, false, true, false, m, m, m, m, m, m, m, false);

            try
            {
                if (!string.IsNullOrWhiteSpace(headingName))
                    return ReadHeadingSection(doc, headingName, maxChars);

                return ReadFullText(doc, maxChars);
            }
            finally
            {
                doc.Close(NetOffice.WordApi.Enums.WdSaveOptions.wdDoNotSaveChanges);
            }
        }

        // ═══════════════════════════════════════════════════
        //  模式 3: 通过标题名称定位
        // ═══════════════════════════════════════════════════

        private ToolExecutionResult ReadHeadingSection(NetOffice.WordApi.Document doc, string headingName, int maxChars)
        {
            NetOffice.WordApi.Paragraph targetPara = null;
            int targetLevel = -1;

            foreach (NetOffice.WordApi.Paragraph para in doc.Paragraphs)
            {
                int level = (int)para.OutlineLevel;
                if (level < 1 || level > 6) continue;

                string text = para.Range.Text.Trim();
                if (text.Equals(headingName, StringComparison.OrdinalIgnoreCase))
                {
                    targetPara = para;
                    targetLevel = level;
                    break;
                }
            }

            if (targetPara == null)
                throw new ToolArgumentException($"未找到标题: {headingName}");

            int sectionStart = targetPara.Range.Start;
            int sectionEnd = doc.Content.End - 1;

            bool passedTarget = false;
            foreach (NetOffice.WordApi.Paragraph para in doc.Paragraphs)
            {
                if (para.Range.Start == sectionStart) { passedTarget = true; continue; }
                if (!passedTarget) continue;

                int level = (int)para.OutlineLevel;
                if (level >= 1 && level <= targetLevel)
                {
                    sectionEnd = para.Range.Start;
                    break;
                }
            }

            var range = doc.Range(sectionStart, sectionEnd);
            string sectionText = range.Text;
            bool truncated = sectionText.Length > maxChars;
            if (truncated)
                sectionText = sectionText.Substring(0, maxChars);

            var sb = new StringBuilder();
            sb.AppendLine($"文档: {doc.Name}");
            sb.AppendLine($"章节: {headingName}（{targetLevel}级标题）");
            sb.AppendLine($"字符数: {range.Text.Length}");
            sb.AppendLine();
            sb.Append(sectionText);

            if (truncated)
                sb.AppendLine($"\n\n…（已截取前 {maxChars} 字符，章节共 {range.Text.Length} 字符）");

            return ToolExecutionResult.Ok(sb.ToString());
        }

        // ═══════════════════════════════════════════════════
        //  模式 4: 全文读取
        // ═══════════════════════════════════════════════════

        private ToolExecutionResult ReadFullText(NetOffice.WordApi.Document doc, int maxChars)
        {
            string text = doc.Content.Text;
            bool truncated = text.Length > maxChars;
            if (truncated)
                text = text.Substring(0, maxChars);

            var sb = new StringBuilder();
            sb.AppendLine($"文档: {doc.Name}");
            sb.AppendLine($"字符数: {doc.Content.Text.Length}");
            sb.AppendLine();
            sb.Append(text);

            if (truncated)
                sb.AppendLine($"\n\n…（已截取前 {maxChars} 字符，全文共 {doc.Content.Text.Length} 字符）");

            return ToolExecutionResult.Ok(sb.ToString());
        }

        // ═══════════════════════════════════════════════════
        //  辅助方法
        // ═══════════════════════════════════════════════════

        private static List<DocumentAstNode> FindSiblings(DocumentMap map, DocumentAstNode target)
        {
            return FindSiblingsRecursive(map.Root, target)
                   ?? new List<DocumentAstNode>();
        }

        private static List<DocumentAstNode> FindSiblingsRecursive(DocumentAstNode parent, DocumentAstNode target)
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
