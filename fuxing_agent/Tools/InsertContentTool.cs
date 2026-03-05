using System;
using System.ComponentModel;
using FuXingAgent.Core;
using Word = Microsoft.Office.Interop.Word;

namespace FuXingAgent.Tools
{
    /// <summary>
    /// 统一内容插入工具，通过 type 参数区分插入类型。
    /// </summary>
    public class InsertContentTool
    {
        private readonly Connect _connect;
        public InsertContentTool(Connect connect) => _connect = connect;

        [Description("Insert content into document at cursor. " +
            "type=text: insert or append plain text (requires text). " +
            "type=image: insert picture (requires file_path). " +
            "type=table: insert table (requires rows, cols). " +
            "type=toc: insert or update table of contents. " +
            "type=caption: insert auto-numbered caption for nearest image/table (requires label, title). " +
            "type=cross_reference: insert auto-updating cross-reference field (requires ref_type, ref_item).")]
        public string insert_content(
            [Description("插入类型: text/image/table/toc/caption/cross_reference")] string type,

            // ── text ──
            [Description("文本内容（type=text 时必填）")] string text = null,
            [Description("文本插入位置: at_cursor/append（type=text，默认 at_cursor）")] string text_position = "at_cursor",

            // ── image ──
            [Description("图片文件路径（type=image 时必填）")] string file_path = null,
            [Description("宽度（厘米），等比缩放")] float? width_cm = null,
            [Description("高度（厘米），等比缩放")] float? height_cm = null,
            [Description("宽度（磅），等比缩放")] float? width = null,
            [Description("高度（磅），等比缩放")] float? height = null,
            [Description("对齐方式: left/center/right（type=image，默认 center）")] string alignment = "center",

            // ── table ──
            [Description("行数 1-500（type=table 时必填）")] int? rows = null,
            [Description("列数 1-63（type=table 时必填）")] int? cols = null,
            [Description("二维数据数组，填充单元格")] string[][] data = null,
            [Description("各列宽度（磅）")] float[] col_widths = null,
            [Description("是否应用默认格式（type=table，默认 true）")] bool auto_format = true,

            // ── toc ──
            [Description("目录操作: insert/update（type=toc，默认 insert）")] string toc_action = "insert",
            [Description("标题级别范围，如 '1-3'（type=toc，默认 1-3）")] string heading_levels = "1-3",

            // ── caption ──
            [Description("题注标签如 图/表/公式（type=caption 时必填）")] string label = null,
            [Description("题注标题文本（type=caption 时必填）")] string title = null,
            [Description("题注位置: above/below（默认：图下/表上）")] string caption_position = null,
            [Description("是否省略标签只保留编号")] bool exclude_label = false,

            // ── cross_reference ──
            [Description("引用类型: heading/bookmark/caption（type=cross_reference 时必填）")] string ref_type = null,
            [Description("引用目标（标题文本/书签名/题注如'图 1'）")] string ref_item = null,
            [Description("显示内容: text/number/page/above_below（默认 text）")] string ref_kind = "text",
            [Description("是否生成可点击超链接（默认 true）")] bool insert_as_link = true)
        {
            if (string.IsNullOrWhiteSpace(type))
                throw new ArgumentException("缺少 type 参数");

            switch (type.ToLowerInvariant())
            {
                case "text": return DoInsertText(text, text_position);
                case "image": return DoInsertImage(file_path, width_cm, height_cm, width, height, alignment);
                case "table": return DoInsertTable(rows, cols, data, col_widths, auto_format);
                case "toc": return DoInsertToc(toc_action, heading_levels);
                case "caption": return DoInsertCaption(label, title, caption_position, exclude_label);
                case "cross_reference": return DoInsertCrossReference(ref_type, ref_item, ref_kind, insert_as_link);
                default: throw new ArgumentException($"未知 type: {type}，支持: text/image/table/toc/caption/cross_reference");
            }
        }

        // ════════════════════════════════════════════════════════════
        //  type = text
        // ════════════════════════════════════════════════════════════

        private string DoInsertText(string text, string position)
        {
            if (string.IsNullOrEmpty(text))
                throw new ArgumentException("type=text 时 text 不能为空");

            var app = _connect.WordApplication;
            var doc = app.ActiveDocument ?? throw new InvalidOperationException("没有活动文档");

            text = text.Replace("\r\n", "\r").Replace("\n", "\r");

            using (WordHelper.BeginTrackRevisions(app))
            {
                switch (position)
                {
                    case "at_cursor":
                        app.Selection.TypeText(text);
                        return $"已在光标处插入 {text.Length} 字符";

                    case "append":
                        var range = doc.Content;
                        var endRange = doc.Range(range.End - 1, range.End - 1);
                        endRange.InsertAfter("\r" + text);
                        return $"已在文档末尾追加 {text.Length} 字符";

                    default:
                        throw new ArgumentException($"未知 text_position: {position}，支持: at_cursor/append");
                }
            }
        }

        // ════════════════════════════════════════════════════════════
        //  type = image
        // ════════════════════════════════════════════════════════════

        private string DoInsertImage(string filePath, float? widthCm, float? heightCm,
            float? widthPt, float? heightPt, string alignment)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("type=image 时 file_path 不能为空");
            if (!System.IO.File.Exists(filePath))
                throw new InvalidOperationException($"图片文件不存在: {filePath}");

            var app = _connect.WordApplication;
            var doc = app.ActiveDocument ?? throw new InvalidOperationException("没有活动文档");
            var sel = app.Selection;

            EnsureNewParagraph(sel);

            var shape = doc.InlineShapes.AddPicture(filePath, false, true, sel.Range);

            float? targetW = widthCm.HasValue ? widthCm.Value * 28.3465f : widthPt;
            float? targetH = heightCm.HasValue ? heightCm.Value * 28.3465f : heightPt;

            if (targetW.HasValue && !targetH.HasValue)
            {
                float ratio = targetW.Value / shape.Width;
                shape.Width = targetW.Value;
                shape.Height = shape.Height * ratio;
            }
            else if (!targetW.HasValue && targetH.HasValue)
            {
                float ratio = targetH.Value / shape.Height;
                shape.Height = targetH.Value;
                shape.Width = shape.Width * ratio;
            }
            else if (targetW.HasValue && targetH.HasValue)
            {
                shape.Width = targetW.Value;
                shape.Height = targetH.Value;
            }

            var para = shape.Range.Paragraphs[1];
            para.Alignment = WordHelper.ParseAlignment(alignment);
            para.Format.LeftIndent = 0;
            para.Format.FirstLineIndent = 0;

            return $"已插入图片 {System.IO.Path.GetFileName(filePath)} " +
                   $"({shape.Width:F0}×{shape.Height:F0} pt, {alignment})";
        }

        // ════════════════════════════════════════════════════════════
        //  type = table
        // ════════════════════════════════════════════════════════════

        private string DoInsertTable(int? rows, int? cols, string[][] data,
            float[] colWidths, bool autoFormat)
        {
            if (!rows.HasValue || !cols.HasValue)
                throw new ArgumentException("type=table 时 rows 和 cols 不能为空");
            if (rows < 1 || rows > 500) throw new ArgumentException("rows 须在 1-500 之间");
            if (cols < 1 || cols > 63) throw new ArgumentException("cols 须在 1-63 之间");

            var app = _connect.WordApplication;
            var doc = app.ActiveDocument ?? throw new InvalidOperationException("没有活动文档");
            var sel = app.Selection;

            EnsureNewParagraph(sel);

            int r = rows.Value, c = cols.Value;
            var table = doc.Tables.Add(sel.Range, r, c);

            if (data != null)
            {
                for (int ri = 0; ri < data.Length && ri < r; ri++)
                {
                    if (data[ri] == null) continue;
                    for (int ci = 0; ci < data[ri].Length && ci < c; ci++)
                    {
                        if (data[ri][ci] != null)
                            table.Cell(ri + 1, ci + 1).Range.Text = data[ri][ci];
                    }
                }
            }

            if (colWidths != null)
            {
                for (int ci = 0; ci < colWidths.Length && ci < c; ci++)
                {
                    if (colWidths[ci] > 0)
                        table.Columns[ci + 1].PreferredWidth = colWidths[ci];
                }
            }

            if (autoFormat)
            {
                try { table.set_Style("表格主题"); }
                catch
                {
                    try { table.set_Style("Table Grid"); }
                    catch { }
                }

                foreach (Word.Border border in table.Borders)
                {
                    border.LineStyle = Word.WdLineStyle.wdLineStyleSingle;
                    border.LineWidth = Word.WdLineWidth.wdLineWidth050pt;
                }

                table.Range.ParagraphFormat.Alignment = Word.WdParagraphAlignment.wdAlignParagraphCenter;
            }

            return $"已插入 {r}×{c} 表格" + (data != null ? "（含数据）" : "");
        }

        // ════════════════════════════════════════════════════════════
        //  type = toc
        // ════════════════════════════════════════════════════════════

        private string DoInsertToc(string action, string headingLevels)
        {
            var app = _connect.WordApplication;
            var doc = app.ActiveDocument ?? throw new InvalidOperationException("没有活动文档");

            if (action == "update")
            {
                if (doc.TablesOfContents.Count == 0)
                    return "文档中没有目录可更新";
                foreach (Word.TableOfContents toc in doc.TablesOfContents)
                    toc.Update();
                return $"已更新 {doc.TablesOfContents.Count} 个目录";
            }

            int upper = 1, lower = 3;
            if (!string.IsNullOrWhiteSpace(headingLevels))
            {
                var parts = headingLevels.Split('-');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0].Trim(), out int u) &&
                    int.TryParse(parts[1].Trim(), out int l))
                {
                    upper = Math.Max(1, Math.Min(u, 9));
                    lower = Math.Max(upper, Math.Min(l, 9));
                }
            }

            var sel = app.Selection;
            EnsureNewParagraph(sel);

            object useHeadingStyles = true;
            object upperLevel = upper;
            object lowerLevel = lower;
            doc.TablesOfContents.Add(sel.Range, ref useHeadingStyles,
                ref upperLevel, ref lowerLevel);

            return $"已插入目录（标题级别 {upper}-{lower}）";
        }

        // ════════════════════════════════════════════════════════════
        //  type = caption
        // ════════════════════════════════════════════════════════════

        private string DoInsertCaption(string label, string title, string position, bool excludeLabel)
        {
            if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("type=caption 时 label 和 title 不能为空");

            var app = _connect.WordApplication;
            var doc = app.ActiveDocument ?? throw new InvalidOperationException("没有活动文档");
            var sel = app.Selection;

            EnsureCaptionLabel(app, label);

            Word.InlineShape targetImage = FindNearestInlineShape(sel);
            Word.Table targetTable = (targetImage == null) ? FindNearestTable(sel) : null;

            if (position == null)
            {
                position = targetTable != null ? "above" : "below";
            }

            var pos = position == "above"
                ? Word.WdCaptionPosition.wdCaptionPositionAbove
                : Word.WdCaptionPosition.wdCaptionPositionBelow;

            object excludeObj = excludeLabel;

            if (targetImage != null)
            {
                targetImage.Range.Select();
                sel.InsertCaption(label, " " + title, null, pos, ref excludeObj);
                return $"已为图片插入题注: {label} - {title}（{position}）";
            }

            if (targetTable != null)
            {
                targetTable.Range.Select();
                sel.InsertCaption(label, " " + title, null, pos, ref excludeObj);
                return $"已为表格插入题注: {label} - {title}（{position}）";
            }

            sel.InsertCaption(label, " " + title, null, pos, ref excludeObj);
            return $"已在光标处插入题注: {label} - {title}（{position}）";
        }

        // ════════════════════════════════════════════════════════════
        //  type = cross_reference
        // ════════════════════════════════════════════════════════════

        private string DoInsertCrossReference(string refType, string refItem, string refKind, bool insertAsLink)
        {
            if (string.IsNullOrWhiteSpace(refType) || string.IsNullOrWhiteSpace(refItem))
                throw new ArgumentException("type=cross_reference 时 ref_type 和 ref_item 不能为空");

            var app = _connect.WordApplication;
            var doc = app.ActiveDocument ?? throw new InvalidOperationException("没有活动文档");
            var sel = app.Selection;

            switch (refType)
            {
                case "heading":
                    return InsertHeadingRef(doc, sel, refItem, refKind, insertAsLink);
                case "bookmark":
                    return InsertBookmarkRef(doc, sel, refItem, refKind, insertAsLink);
                case "caption":
                    return InsertCaptionRef(doc, sel, refItem, refKind, insertAsLink);
                default:
                    throw new ArgumentException($"未知 ref_type: {refType}");
            }
        }

        // ════════════════════════════════════════════════════════════
        //  私有辅助方法
        // ════════════════════════════════════════════════════════════

        private static void EnsureNewParagraph(Word.Selection sel)
        {
            string paraText = sel.Range.Paragraphs[1].Range.Text?.TrimEnd('\r', '\n', '\a') ?? "";
            if (paraText.Length > 0) sel.TypeParagraph();
        }

        private static void EnsureCaptionLabel(Word.Application app, string label)
        {
            foreach (Word.CaptionLabel cl in app.CaptionLabels)
                if (cl.Name == label) return;
            app.CaptionLabels.Add(label);
        }

        private static Word.InlineShape FindNearestInlineShape(Word.Selection sel)
        {
            if (sel.InlineShapes.Count > 0)
                return sel.InlineShapes[1];
            try
            {
                var para = sel.Range.Paragraphs[1];
                if (para.Range.InlineShapes.Count > 0)
                    return para.Range.InlineShapes[1];
                var prevPara = para.Previous();
                if (prevPara != null && prevPara.Range.InlineShapes.Count > 0)
                    return prevPara.Range.InlineShapes[1];
            }
            catch { }
            return null;
        }

        private static Word.Table FindNearestTable(Word.Selection sel)
        {
            if (sel.Tables.Count > 0) return sel.Tables[1];
            try
            {
                var para = sel.Range.Paragraphs[1];
                var prevPara = para.Previous();
                if (prevPara != null && prevPara.Range.Tables.Count > 0)
                    return prevPara.Range.Tables[1];
            }
            catch { }
            return null;
        }

        private static string InsertHeadingRef(Word.Document doc, Word.Selection sel,
            string refItem, string refKind, bool insertAsLink)
        {
            int targetIndex = -1;
            int idx = 0;
            foreach (Word.Paragraph para in doc.Paragraphs)
            {
                int level = (int)para.OutlineLevel;
                if (level >= 1 && level <= 9)
                {
                    idx++;
                    string text = para.Range.Text.Trim();
                    if (text.Equals(refItem, StringComparison.OrdinalIgnoreCase))
                    {
                        targetIndex = idx;
                        break;
                    }
                }
            }
            if (targetIndex < 0)
                throw new InvalidOperationException($"未找到标题: {refItem}");

            object refTypeObj = Word.WdReferenceType.wdRefTypeHeading;
            Word.WdReferenceKind refKindVal = ResolveRefKind(refKind);
            object refItemObj = targetIndex;
            object insertAsLinkObj = insertAsLink;
            object includePosObj = false;
            sel.InsertCrossReference(ref refTypeObj, refKindVal, ref refItemObj,
                ref insertAsLinkObj, ref includePosObj);

            return $"已插入标题交叉引用 {refItem}";
        }

        private static string InsertBookmarkRef(Word.Document doc, Word.Selection sel,
            string refItem, string refKind, bool insertAsLink)
        {
            if (!doc.Bookmarks.Exists(refItem))
                throw new InvalidOperationException($"书签不存在: {refItem}");

            object refTypeObj = Word.WdReferenceType.wdRefTypeBookmark;
            Word.WdReferenceKind refKindVal = ResolveRefKind(refKind);
            object refItemObj = refItem;
            object insertAsLinkObj = insertAsLink;
            object includePosObj = false;
            sel.InsertCrossReference(ref refTypeObj, refKindVal, ref refItemObj,
                ref insertAsLinkObj, ref includePosObj);

            return $"已插入书签交叉引用 {refItem}";
        }

        private static string InsertCaptionRef(Word.Document doc, Word.Selection sel,
            string refItem, string refKind, bool insertAsLink)
        {
            string[] parts = refItem.Split(new[] { ' ' }, 2);
            if (parts.Length < 2)
                throw new ArgumentException($"题注格式应为 '标签 编号'（如'图 1'），收到: {refItem}");

            string label = parts[0];
            if (!int.TryParse(parts[1].Trim(), out int number))
                throw new ArgumentException($"题注编号必须是数字: {parts[1]}");

            object refTypeObj = label;
            Word.WdReferenceKind refKindVal = ResolveRefKind(refKind);
            object refItemObj = number;
            object insertAsLinkObj = insertAsLink;
            object includePosObj = false;
            sel.InsertCrossReference(ref refTypeObj, refKindVal, ref refItemObj,
                ref insertAsLinkObj, ref includePosObj);

            return $"已插入题注交叉引用 {refItem}";
        }

        private static Word.WdReferenceKind ResolveRefKind(string refKind)
        {
            switch ((refKind ?? "text").ToLowerInvariant())
            {
                case "number": return Word.WdReferenceKind.wdOnlyLabelAndNumber;
                case "page": return Word.WdReferenceKind.wdPageNumber;
                case "above_below": return Word.WdReferenceKind.wdNumberRelativeContext;
                case "text":
                default: return Word.WdReferenceKind.wdEntireCaption;
            }
        }
    }
}
