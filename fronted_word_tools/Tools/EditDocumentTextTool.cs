using FuXing.Core;
using Newtonsoft.Json.Linq;
using NetOffice.WordApi.Enums;
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WordDocument = NetOffice.WordApi.Document;
using WordParagraph = NetOffice.WordApi.Paragraph;
using WordRange = NetOffice.WordApi.Range;
using WordTable = NetOffice.WordApi.Table;
using Task = System.Threading.Tasks.Task;

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
            "Insert, replace, or append text. Automatically parses Markdown and inserts rich text with default formatting loaded from Skill directory. " +
            "Actions: insert_at_cursor (default), replace_selection, " +
            "append_to_document, insert_at_node (insert at node start/end via node_id from document_graph), " +
            "replace_node_content (replace content of a leaf node like TextBlock/Heading/Paragraph — NOT allowed on Section or Document nodes).";

        private static readonly Regex MarkdownPattern = new Regex(
            @"(^\s{0,3}#{1,6}\s)|(^\s*[-*+]\s+)|(^\s*\d+\.\s+)|(```)|(^\s*>\s+)|(\[[^\]]+\]\([^)]+\))|(^\s*\|.*\|\s*$)",
            RegexOptions.Multiline | RegexOptions.Compiled);

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
            var doc = RequireActiveDocument(connect);
            if (ShouldRenderMarkdown(text))
                return InsertMarkdownAtCursor(connect, doc, text);

            text = NormalizeNewlines(text);
            var sel = connect.WordApplication.Selection;
            int posBefore = sel.Start;

            using (BeginTrackRevisions(connect))
            {
                sel.TypeText(text);
            }

            int posAfter = connect.WordApplication.Selection.Start;
            if (posAfter == posBefore)
                throw new ToolArgumentException(
                    "TypeText 调用后光标位置未变化，文本可能未成功插入。" +
                    "请检查光标是否在受保护区域或 ContentControl 内。");

            DocumentGraphCache.Instance.RefreshHash(doc);
            return Task.FromResult(
                ToolExecutionResult.Ok($"已在光标位置插入 {text.Length} 个字符。"));
        }

        private Task<ToolExecutionResult> ReplaceSelection(Connect connect, string text)
        {
            var doc = RequireActiveDocument(connect);
            if (ShouldRenderMarkdown(text))
                return ReplaceSelectionWithMarkdown(connect, doc, text);

            text = NormalizeNewlines(text);
            var selection = connect.WordApplication.Selection;
            if (string.IsNullOrEmpty(selection?.Text?.Trim()))
                throw new ToolArgumentException("没有选中的文本");

            int oldLen = selection.Text.Length;
            using (BeginTrackRevisions(connect))
            {
                selection.TypeText(text);
            }
            DocumentGraphCache.Instance.RefreshHash(doc);
            return Task.FromResult(
                ToolExecutionResult.Ok($"已将选中文本（{oldLen}字符）替换为新文本（{text.Length}字符）。"));
        }

        private Task<ToolExecutionResult> AppendToDocument(Connect connect, WordDocument doc, string text)
        {
            if (ShouldRenderMarkdown(text))
                return AppendMarkdownToDocument(connect, doc, text);

            text = NormalizeNewlines(text);
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
            Connect connect, WordDocument doc, JObject arguments, string text)
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

            if (ShouldRenderMarkdown(text))
                return InsertMarkdownAtNode(connect, doc, node, position, targetPos, text);

            text = NormalizeNewlines(text);

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
            Connect connect, WordDocument doc, JObject arguments, string text)
        {
            string nodeId = RequireString(arguments, "node_id");

            var graph = DocumentGraphCache.Instance.GetOrBuildAsync(doc).Result;
            var node = graph.ResolveNode(nodeId);
            if (node == null)
                throw new ToolArgumentException(
                    $"节点不存在: {nodeId}。请先调用 document_graph(map) 获取有效节点，或检查 label 是否正确。");

            // Section/Document 节点包含子结构，禁止整体替换
            if (node.Type == DocNodeType.Section || node.Type == DocNodeType.Document)
                throw new ToolArgumentException(
                    $"节点 [{node.Id}] 是 {node.Type} 类型，包含子节点（标题、文本块、表格等）。" +
                    "请指定具体子节点 ID 进行操作，避免破坏文档结构。");

            if (node.AnchorLabel == null)
                throw new ToolArgumentException($"节点 {node.Id} 无锚点");

            var range = DocumentGraphCache.Instance.Anchors.GetRange(doc, node.AnchorLabel);
            int oldLen = (range.Text ?? "").Length;

            if (ShouldRenderMarkdown(text))
                return ReplaceNodeContentWithMarkdown(connect, doc, node, range, oldLen, text);

            text = NormalizeNewlines(text);

            using (BeginTrackRevisions(connect))
            {
                range.Text = text;
            }

            DocumentGraphCache.Instance.RefreshHash(doc);

            return Task.FromResult(
                ToolExecutionResult.Ok(
                    $"已替换节点 [{node.Id}] {node.Title} 的内容（{oldLen}→{text.Length} 字符）。"));
        }

        /// <summary>
        /// 将文本中的 LF (\n) 统一转换为 CR (\r)，确保 Word TypeText / Range.Text
        /// 能正确创建段落标记而非手动换行。
        /// </summary>
        private static string NormalizeNewlines(string text)
        {
            return text.Replace("\r\n", "\r").Replace("\n", "\r");
        }

        private static bool ShouldRenderMarkdown(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return MarkdownPattern.IsMatch(text);
        }

        private System.Threading.Tasks.Task<ToolExecutionResult> InsertMarkdownAtCursor(Connect connect, WordDocument doc, string markdown)
        {
            var sel = connect.WordApplication.Selection;
            int start = sel.Start;

            using (BeginTrackRevisions(connect))
            {
                var range = doc.Range(start, start);
                InsertMarkdownWithDefaultFormat(connect, doc, range, markdown);
            }

            DocumentGraphCache.Instance.RefreshHash(doc);
            return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Ok("已在光标位置插入 Markdown 富文本。"));
        }

        private System.Threading.Tasks.Task<ToolExecutionResult> ReplaceSelectionWithMarkdown(Connect connect, WordDocument doc, string markdown)
        {
            var selection = connect.WordApplication.Selection;
            if (string.IsNullOrEmpty(selection?.Text?.Trim()))
                throw new ToolArgumentException("没有选中的文本");

            int oldLen = selection.Text.Length;
            int start = selection.Range.Start;
            int end = selection.Range.End;

            using (BeginTrackRevisions(connect))
            {
                doc.Range(start, end).Delete();
                var insertRange = doc.Range(start, start);
                InsertMarkdownWithDefaultFormat(connect, doc, insertRange, markdown);
            }

            DocumentGraphCache.Instance.RefreshHash(doc);
            return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Ok($"已将选中文本（{oldLen}字符）替换为 Markdown 富文本。"));
        }

        private System.Threading.Tasks.Task<ToolExecutionResult> AppendMarkdownToDocument(Connect connect, WordDocument doc, string markdown)
        {
            int endPos = doc.Content.End - 1;
            using (BeginTrackRevisions(connect))
            {
                var range = doc.Range(endPos, endPos);
                InsertMarkdownWithDefaultFormat(connect, doc, range, markdown);
            }

            DocumentGraphCache.Instance.RefreshHash(doc);
            return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Ok("已在文档末尾追加 Markdown 富文本。"));
        }

        private System.Threading.Tasks.Task<ToolExecutionResult> InsertMarkdownAtNode(Connect connect, WordDocument doc, DocNode node, string position, int targetPos, string markdown)
        {
            using (BeginTrackRevisions(connect))
            {
                var insertRange = doc.Range(targetPos, targetPos);
                InsertMarkdownWithDefaultFormat(connect, doc, insertRange, markdown);
            }

            DocumentGraphCache.Instance.RefreshHash(doc);
            string posDesc = position == "start" ? "开头" : "末尾";
            return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Ok($"已在节点 [{node.Id}] {node.Title} 的{posDesc}插入 Markdown 富文本。"));
        }

        private System.Threading.Tasks.Task<ToolExecutionResult> ReplaceNodeContentWithMarkdown(Connect connect, WordDocument doc, DocNode node, WordRange range, int oldLen, string markdown)
        {
            int start = range.Start;
            int end = range.End;

            using (BeginTrackRevisions(connect))
            {
                doc.Range(start, end).Delete();
                var insertRange = doc.Range(start, start);
                InsertMarkdownWithDefaultFormat(connect, doc, insertRange, markdown);
            }

            DocumentGraphCache.Instance.RefreshHash(doc);
            return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Ok($"已替换节点 [{node.Id}] {node.Title} 的内容（{oldLen}→Markdown 富文本）。"));
        }

        private static void InsertMarkdownWithDefaultFormat(Connect connect, WordDocument doc, WordRange insertRange, string markdown)
        {
            string tempDocx = null;
            try
            {
                tempDocx = MarkdownWordRenderer.RenderToTempDocx(markdown);
                insertRange.InsertFile(tempDocx);
                ApplyDefaultFormat(connect, doc, insertRange);
            }
            finally
            {
                MarkdownWordRenderer.TryDeleteTempFile(tempDocx);
            }
        }

        private static void ApplyDefaultFormat(Connect connect, WordDocument doc, WordRange insertedRange)
        {
            var profile = MarkdownWordRenderer.LoadDefaultFormatProfile();

            foreach (WordParagraph paragraph in insertedRange.Paragraphs)
            {
                ApplyParagraphStyle(doc, paragraph, profile);
                ApplyParagraphBodyFormat(paragraph, profile);
            }

            foreach (WordTable table in insertedRange.Tables)
            {
                if (!string.IsNullOrWhiteSpace(profile.TableStyle))
                    ApplyStyleByNameOrThrow(doc, table.Range, profile.TableStyle);

                if (!string.IsNullOrWhiteSpace(profile.BodyFontName))
                    table.Range.Font.Name = profile.BodyFontName;
                if (profile.BodyFontSize.HasValue)
                    table.Range.Font.Size = profile.BodyFontSize.Value;
            }

            connect.WordApplication.Selection.SetRange(insertedRange.End, insertedRange.End);
        }

        private static void ApplyParagraphStyle(WordDocument doc, WordParagraph paragraph, MarkdownWordRenderer.MarkdownDefaultFormatProfile profile)
        {
            var outlineLevel = paragraph.OutlineLevel;

            if (outlineLevel == WdOutlineLevel.wdOutlineLevel1 && !string.IsNullOrWhiteSpace(profile.Heading1Style))
            {
                ApplyStyleByNameOrThrow(doc, paragraph.Range, profile.Heading1Style);
                return;
            }
            if (outlineLevel == WdOutlineLevel.wdOutlineLevel2 && !string.IsNullOrWhiteSpace(profile.Heading2Style))
            {
                ApplyStyleByNameOrThrow(doc, paragraph.Range, profile.Heading2Style);
                return;
            }
            if (outlineLevel == WdOutlineLevel.wdOutlineLevel3 && !string.IsNullOrWhiteSpace(profile.Heading3Style))
            {
                ApplyStyleByNameOrThrow(doc, paragraph.Range, profile.Heading3Style);
                return;
            }
            if (outlineLevel == WdOutlineLevel.wdOutlineLevel4 && !string.IsNullOrWhiteSpace(profile.Heading4Style))
            {
                ApplyStyleByNameOrThrow(doc, paragraph.Range, profile.Heading4Style);
                return;
            }
            if (outlineLevel == WdOutlineLevel.wdOutlineLevel5 && !string.IsNullOrWhiteSpace(profile.Heading5Style))
            {
                ApplyStyleByNameOrThrow(doc, paragraph.Range, profile.Heading5Style);
                return;
            }
            if (outlineLevel == WdOutlineLevel.wdOutlineLevel6 && !string.IsNullOrWhiteSpace(profile.Heading6Style))
            {
                ApplyStyleByNameOrThrow(doc, paragraph.Range, profile.Heading6Style);
                return;
            }

            if (!string.IsNullOrWhiteSpace(profile.BodyStyle))
                ApplyStyleByNameOrThrow(doc, paragraph.Range, profile.BodyStyle);
        }

        private static void ApplyParagraphBodyFormat(WordParagraph paragraph, MarkdownWordRenderer.MarkdownDefaultFormatProfile profile)
        {
            if (!string.IsNullOrWhiteSpace(profile.BodyFontName))
                paragraph.Range.Font.Name = profile.BodyFontName;
            if (profile.BodyFontSize.HasValue)
                paragraph.Range.Font.Size = profile.BodyFontSize.Value;

            if (!string.IsNullOrWhiteSpace(profile.BodyAlignment))
                paragraph.Range.ParagraphFormat.Alignment = WordHelper.ParseAlignment(profile.BodyAlignment);

            if (!string.IsNullOrWhiteSpace(profile.BodyLineSpacingRule))
                paragraph.Range.ParagraphFormat.LineSpacingRule = ParseLineSpacingRule(profile.BodyLineSpacingRule);
        }

        private static void ApplyStyleByNameOrThrow(WordDocument doc, WordRange range, string styleName)
        {
            try
            {
                range.Style = styleName;
            }
            catch
            {
                throw new ToolArgumentException($"默认格式样式不存在: {styleName}");
            }
        }

        private static WdLineSpacing ParseLineSpacingRule(string value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "single": return WdLineSpacing.wdLineSpaceSingle;
                case "1.5":
                case "onepointfive": return WdLineSpacing.wdLineSpace1pt5;
                case "double": return WdLineSpacing.wdLineSpaceDouble;
                case "atleast": return WdLineSpacing.wdLineSpaceAtLeast;
                case "exactly": return WdLineSpacing.wdLineSpaceExactly;
                case "multiple": return WdLineSpacing.wdLineSpaceMultiple;
                default: throw new ToolArgumentException($"默认格式 body_line_spacing_rule 无效: {value}");
            }
        }
    }
}
