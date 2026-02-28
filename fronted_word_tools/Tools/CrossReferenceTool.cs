using Newtonsoft.Json.Linq;
using System;
using System.Text;
using NetOffice.WordApi;
using NetOffice.WordApi.Enums;

namespace FuXing
{
    /// <summary>
    /// 在文档中插入交叉引用（动态域代码），
    /// 支持引用标题、书签、题注等，实现"见图1"、"参考第2.1节"等自动更新引用。
    /// </summary>
    public class CrossReferenceTool : ToolBase
    {
        public override string Name => "cross_reference";
        public override string DisplayName => "交叉引用";
        public override ToolCategory Category => ToolCategory.Structure;

        public override string Description =>
            "Insert auto-updating cross-reference field at cursor. " +
            "ref_type: heading/bookmark/caption. ref_item: target text (heading text, bookmark name, or caption like \"图 1\"). " +
            "ref_kind: text/number/page/above_below. insert_as_link: clickable hyperlink (default true).";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["ref_type"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray("heading", "bookmark", "caption"),
                    ["description"] = "引用类型"
                },
                ["ref_item"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "引用目标（标题文本/书签名/题注编号如 \"图 1\"）"
                },
                ["ref_kind"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray("text", "number", "page", "above_below"),
                    ["description"] = "显示内容类型（默认 text）"
                },
                ["insert_as_link"] = new JObject
                {
                    ["type"] = "boolean",
                    ["description"] = "是否生成可点击的超链接（默认 true）"
                }
            },
            ["required"] = new JArray("ref_type", "ref_item")
        };

        public override System.Threading.Tasks.Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            var doc = RequireActiveDocument(connect);
            var app = connect.WordApplication;

            string refType = RequireString(arguments, "ref_type");
            string refItem = RequireString(arguments, "ref_item");
            string refKind = OptionalString(arguments, "ref_kind", "text");
            bool insertAsLink = OptionalBool(arguments, "insert_as_link", true);

            switch (refType)
            {
                case "heading":
                    return InsertHeadingRef(app, doc, refItem, refKind, insertAsLink);

                case "bookmark":
                    return InsertBookmarkRef(app, doc, refItem, refKind, insertAsLink);

                case "caption":
                    return InsertCaptionRef(app, doc, refItem, refKind, insertAsLink);

                default:
                    throw new ToolArgumentException($"未知 ref_type: {refType}，可选: heading, bookmark, caption");
            }
        }

        // ═══════════════════════════════════════════════════
        //  引用标题
        // ═══════════════════════════════════════════════════

        private System.Threading.Tasks.Task<ToolExecutionResult> InsertHeadingRef(
            Application app, Document doc, string headingText, string refKind, bool asLink)
        {
            // 查找匹配的标题并获取其在标题列表中的索引（1-based）
            int headingIndex = -1;
            int currentIndex = 0;

            foreach (Paragraph para in doc.Paragraphs)
            {
                int level = (int)para.OutlineLevel;
                if (level >= 1 && level <= 9)
                {
                    currentIndex++;
                    if (para.Range.Text.Trim().Equals(headingText, StringComparison.OrdinalIgnoreCase))
                    {
                        headingIndex = currentIndex;
                        break;
                    }
                }
            }

            if (headingIndex < 0)
                throw new ToolArgumentException($"未找到标题: {headingText}");

            WdReferenceKind kind = ResolveRefKind(refKind, "heading");

            app.Selection.InsertCrossReference(
                WdReferenceType.wdRefTypeHeading,
                kind,
                headingIndex,
                asLink,
                false,
                false,
                " ");

            return System.Threading.Tasks.Task.FromResult(
                ToolExecutionResult.Ok($"已插入交叉引用 → 标题「{headingText}」（显示: {refKind}）"));
        }

        // ═══════════════════════════════════════════════════
        //  引用书签
        // ═══════════════════════════════════════════════════

        private System.Threading.Tasks.Task<ToolExecutionResult> InsertBookmarkRef(
            Application app, Document doc, string bookmarkName, string refKind, bool asLink)
        {
            if (!doc.Bookmarks.Exists(bookmarkName))
                throw new ToolArgumentException($"书签不存在: {bookmarkName}");

            WdReferenceKind kind = ResolveRefKind(refKind, "bookmark");

            app.Selection.InsertCrossReference(
                WdReferenceType.wdRefTypeBookmark,
                kind,
                bookmarkName,
                asLink,
                false,
                false,
                " ");

            return System.Threading.Tasks.Task.FromResult(
                ToolExecutionResult.Ok($"已插入交叉引用 → 书签「{bookmarkName}」（显示: {refKind}）"));
        }

        // ═══════════════════════════════════════════════════
        //  引用题注
        // ═══════════════════════════════════════════════════

        private System.Threading.Tasks.Task<ToolExecutionResult> InsertCaptionRef(
            Application app, Document doc, string captionText, string refKind, bool asLink)
        {
            // 解析题注标签和编号，如 "图 1" → label="图", number=1
            string label = null;
            int targetNumber = -1;

            int lastSpace = captionText.LastIndexOf(' ');
            if (lastSpace > 0)
            {
                label = captionText.Substring(0, lastSpace).Trim();
                if (int.TryParse(captionText.Substring(lastSpace + 1).Trim(), out int num))
                    targetNumber = num;
            }

            if (string.IsNullOrEmpty(label) || targetNumber < 1)
                throw new ToolArgumentException(
                    $"无法解析题注引用 \"{captionText}\"，请使用格式如「图 1」、「表 2」");

            // 通过 CaptionLabels 确定引用类型字符串
            // Word 的交叉引用 API 对题注使用标签名作为 ReferenceType
            WdReferenceKind kind = ResolveRefKind(refKind, "caption");

            app.Selection.InsertCrossReference(
                label,
                kind,
                targetNumber,
                asLink,
                false,
                false,
                " ");

            return System.Threading.Tasks.Task.FromResult(
                ToolExecutionResult.Ok($"已插入交叉引用 → 题注「{captionText}」（显示: {refKind}）"));
        }

        // ═══════════════════════════════════════════════════
        //  辅助方法
        // ═══════════════════════════════════════════════════

        private static WdReferenceKind ResolveRefKind(string refKind, string refType)
        {
            switch (refKind)
            {
                case "text":
                    return refType == "caption"
                        ? WdReferenceKind.wdEntireCaption
                        : WdReferenceKind.wdContentText;

                case "number":
                    return refType == "heading"
                        ? WdReferenceKind.wdNumberFullContext
                        : WdReferenceKind.wdOnlyLabelAndNumber;

                case "page":
                    return WdReferenceKind.wdPageNumber;

                case "above_below":
                    return WdReferenceKind.wdPosition;

                default:
                    return WdReferenceKind.wdContentText;
            }
        }
    }
}
