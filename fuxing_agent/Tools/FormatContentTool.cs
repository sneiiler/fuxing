using System;
using System.ComponentModel;
using FuXingAgent.Core;
using Word = Microsoft.Office.Interop.Word;

namespace FuXingAgent.Tools
{
    public class FormatContentTool
    {
        private readonly Connect _connect;
        public FormatContentTool(Connect connect) => _connect = connect;

        [Description("Unified formatting tool. action=format: apply style/font/paragraph to target. action=create_style: create or update a named style. action=format_table: format table style. target.type: selection/search/heading/heading_level/body_text.")]
        public string format_content(
            [Description("操作类型: format/create_style/format_table")] string action = "format",
            [Description("定位目标（format 模式）")] FormatTarget target = null,
            [Description("要应用的样式")] string style_name = null,
            [Description("字体设置")] FontOptions font = null,
            [Description("段落设置")] ParagraphOptions paragraph = null,
            [Description("新样式名（create_style 模式）")] string name = null,
            [Description("基础样式（create_style 模式）")] string based_on = "正文",
            [Description("后续段落样式（create_style 模式）")] string next_style = null,
            [Description("表格序号 1-based, 0=全部, null=光标处（format_table 模式）")] int? table_index = null,
            [Description("表格字体（format_table 模式）")] TableFontOptions table_font = null,
            [Description("表格对齐: left/center/right/justify（format_table 模式）")] string table_alignment = null,
            [Description("最小行高 磅（format_table 模式）")] float? row_height = null,
            [Description("表格边框（format_table 模式）")] TableBorderOptions borders = null,
            [Description("表头样式（format_table 模式）")] TableHeaderOptions header = null,
            [Description("整表底纹颜色 hex（format_table 模式）")] string shading_bg_color = null)
        {
            var app = _connect.WordApplication;
            var doc = app.ActiveDocument ?? throw new InvalidOperationException("没有活动文档");

            if (action == "create_style")
                return ExecuteCreateStyle(doc, name, based_on, next_style, font, paragraph);

            if (action == "format_table")
                return ExecuteFormatTable(app, doc, table_index, table_font, table_alignment,
                    row_height, borders, header, shading_bg_color);

            return ExecuteFormat(app, doc, target, style_name, font, paragraph);
        }

        private static string ExecuteFormat(Word.Application app, Word.Document doc,
            FormatTarget target, string styleName, FontOptions font, ParagraphOptions paragraph)
        {
            if (target == null)
                throw new ArgumentException("action=format 时缺少 target");
            if (string.IsNullOrWhiteSpace(styleName) && font == null && paragraph == null)
                throw new ArgumentException("至少需要 style_name、font、paragraph 之一");

            object styleObj = null;
            if (!string.IsNullOrWhiteSpace(styleName))
                styleObj = ResolveStyleObject(doc, styleName);

            int count = 0;
            switch (target.type)
            {
                case "selection":
                    ApplyAll(app.Selection.Range, styleObj, font, paragraph);
                    count = 1;
                    break;

                case "search":
                    if (string.IsNullOrWhiteSpace(target.value))
                        throw new ArgumentException("search 模式需要 target.value");
                    count = FormatBySearch(doc, target.value, styleObj, font, paragraph);
                    break;

                case "heading":
                    if (string.IsNullOrWhiteSpace(target.value))
                        throw new ArgumentException("heading 模式需要 target.value");
                    var headingRange = FindHeadingRange(doc, target.value);
                    if (headingRange == null)
                        throw new InvalidOperationException($"未找到标题: {target.value}");
                    ApplyAll(headingRange, styleObj, font, paragraph);
                    count = 1;
                    break;

                case "heading_level":
                    if (!int.TryParse(target.value, out int level) || level < 1 || level > 9)
                        throw new ArgumentException("heading_level 需要 1-9 的整数");
                    foreach (Word.Paragraph p in doc.Paragraphs)
                        if ((int)p.OutlineLevel == level) { ApplyAll(p.Range, styleObj, font, paragraph); count++; }
                    break;

                case "body_text":
                    foreach (Word.Paragraph p in doc.Paragraphs)
                        if ((int)p.OutlineLevel == (int)Word.WdOutlineLevel.wdOutlineLevelBodyText) { ApplyAll(p.Range, styleObj, font, paragraph); count++; }
                    break;

                default:
                    throw new ArgumentException($"未知 target.type: {target.type}");
            }

            return count == 0 ? "未找到匹配的目标" : $"已完成格式化，命中 {count} 处";
        }

        private static string ExecuteCreateStyle(Word.Document doc, string name,
            string basedOn, string nextStyle, FontOptions font, ParagraphOptions paragraph)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("create_style 缺少 name");

            var style = FindOrCreateStyle(doc, name);

            if (!string.Equals(basedOn, "none", StringComparison.OrdinalIgnoreCase))
            {
                object baseObj = ResolveStyleObject(doc, basedOn) ?? (object)basedOn;
                style.set_BaseStyle(ref baseObj);
            }

            if (!string.IsNullOrWhiteSpace(nextStyle))
            {
                object nextObj = (object)nextStyle;
                style.set_NextParagraphStyle(ref nextObj);
            }

            if (font != null) ApplyFont(style.Font, font);
            if (paragraph != null) ApplyParagraph(style.ParagraphFormat, paragraph);

            return $"已创建/更新样式：{name}";
        }

        private static void ApplyAll(Word.Range range, object styleObj, FontOptions font, ParagraphOptions paragraph)
        {
            if (styleObj != null) range.set_Style(styleObj);
            if (font != null) ApplyFont(range.Font, font);
            if (paragraph != null) ApplyParagraph(range.ParagraphFormat, paragraph);
        }

        private static void ApplyFont(Word.Font f, FontOptions opt)
        {
            if (opt.name != null) f.NameFarEast = opt.name;
            if (opt.name_ascii != null) f.NameAscii = opt.name_ascii;
            if (opt.name_far_east != null) f.NameFarEast = opt.name_far_east;
            if (opt.size.HasValue) f.Size = opt.size.Value;
            if (opt.bold.HasValue) f.Bold = opt.bold.Value ? 1 : 0;
            if (opt.italic.HasValue) f.Italic = opt.italic.Value ? 1 : 0;
            if (opt.underline.HasValue) f.Underline = opt.underline.Value ? Word.WdUnderline.wdUnderlineSingle : Word.WdUnderline.wdUnderlineNone;
            if (opt.strikethrough.HasValue) f.StrikeThrough = opt.strikethrough.Value ? 1 : 0;
        }

        private static void ApplyParagraph(Word.ParagraphFormat pf, ParagraphOptions opt)
        {
            if (opt.alignment != null) pf.Alignment = ParseAlignment(opt.alignment);
            if (opt.space_before_pt.HasValue) pf.SpaceBefore = opt.space_before_pt.Value;
            if (opt.space_after_pt.HasValue) pf.SpaceAfter = opt.space_after_pt.Value;
            if (opt.first_line_indent_pt.HasValue) pf.FirstLineIndent = opt.first_line_indent_pt.Value;
            if (opt.left_indent_pt.HasValue) pf.LeftIndent = opt.left_indent_pt.Value;

            if (opt.line_spacing_multiple.HasValue)
            {
                pf.LineSpacingRule = Word.WdLineSpacing.wdLineSpaceMultiple;
                pf.LineSpacing = opt.line_spacing_multiple.Value * 12f;
            }

            if (opt.line_spacing_exact.HasValue)
            {
                pf.LineSpacingRule = Word.WdLineSpacing.wdLineSpaceExactly;
                pf.LineSpacing = opt.line_spacing_exact.Value;
            }

            if (opt.outline_level.HasValue) pf.OutlineLevel = ParseOutlineLevel(opt.outline_level.Value);
        }

        private static int FormatBySearch(Word.Document doc, string searchText,
            object styleObj, FontOptions font, ParagraphOptions paragraph)
        {
            int count = 0;
            var range = doc.Content;
            range.Find.ClearFormatting();
            object findObj = searchText;
            object missing = Type.Missing;
            object forwardObj = true;
            object wrapObj = Word.WdFindWrap.wdFindStop;

            while (range.Find.Execute(ref findObj, ref missing, ref missing, ref missing,
                ref missing, ref missing, ref forwardObj, ref wrapObj,
                ref missing, ref missing, ref missing, ref missing,
                ref missing, ref missing, ref missing))
            {
                ApplyAll(range, styleObj, font, paragraph);
                count++;
                int newStart = range.End;
                if (newStart >= doc.Content.End) break;
                range.SetRange(newStart, doc.Content.End);
            }
            return count;
        }

        private static Word.Range FindHeadingRange(Word.Document doc, string headingName)
        {
            foreach (Word.Paragraph para in doc.Paragraphs)
            {
                int level = (int)para.OutlineLevel;
                if (level >= 1 && level <= 9 &&
                    para.Range.Text.Trim().Equals(headingName, StringComparison.OrdinalIgnoreCase))
                    return para.Range;
            }
            return null;
        }

        private static object ResolveStyleObject(Word.Document doc, string styleName)
        {
            try { return doc.Styles[styleName]; }
            catch
            {
                if (Enum.TryParse(styleName, true, out Word.WdBuiltinStyle builtin))
                    return doc.Styles[builtin];
                return null;
            }
        }

        private static Word.Style FindOrCreateStyle(Word.Document doc, string styleName)
        {
            try { return doc.Styles[styleName]; }
            catch { return doc.Styles.Add(styleName, Word.WdStyleType.wdStyleTypeParagraph); }
        }

        private static Word.WdParagraphAlignment ParseAlignment(string alignment)
        {
            switch ((alignment ?? "").Trim().ToLowerInvariant())
            {
                case "left": return Word.WdParagraphAlignment.wdAlignParagraphLeft;
                case "center": return Word.WdParagraphAlignment.wdAlignParagraphCenter;
                case "right": return Word.WdParagraphAlignment.wdAlignParagraphRight;
                default: return Word.WdParagraphAlignment.wdAlignParagraphJustify;
            }
        }

        private static Word.WdOutlineLevel ParseOutlineLevel(int level)
        {
            if (level >= 1 && level <= 9)
                return (Word.WdOutlineLevel)level;
            return Word.WdOutlineLevel.wdOutlineLevelBodyText;
        }

        // ═══════════════════════════════════════════════════
        //  format_table 逻辑
        // ═══════════════════════════════════════════════════

        private static string ExecuteFormatTable(Word.Application app, Word.Document doc,
            int? tableIndex, TableFontOptions font, string alignment, float? rowHeight,
            TableBorderOptions borders, TableHeaderOptions header, string shadingBg)
        {
            bool hasCustom = font != null || alignment != null || rowHeight.HasValue
                             || borders != null || header != null || shadingBg != null;

            if (tableIndex == 0)
            {
                if (doc.Tables.Count == 0) return "文档中没有表格";
                int count = 0;
                foreach (Word.Table t in doc.Tables)
                {
                    FormatSingleTable(t, hasCustom, font, alignment, rowHeight, borders, header, shadingBg);
                    count++;
                }
                return $"已格式化全部 {count} 个表格";
            }

            Word.Table table;
            if (tableIndex.HasValue)
            {
                if (tableIndex.Value < 1 || tableIndex.Value > doc.Tables.Count)
                    throw new InvalidOperationException($"表格索引超出范围（共 {doc.Tables.Count} 个）");
                table = doc.Tables[tableIndex.Value];
            }
            else
            {
                if (app.Selection.Tables.Count == 0)
                    throw new InvalidOperationException("光标未在表格内");
                table = app.Selection.Tables[1];
            }

            FormatSingleTable(table, hasCustom, font, alignment, rowHeight, borders, header, shadingBg);
            return "已格式化表格";
        }

        private static void FormatSingleTable(Word.Table table, bool hasCustom,
            TableFontOptions font, string alignment, float? rowHeight,
            TableBorderOptions borders, TableHeaderOptions header, string shadingBg)
        {
            if (!hasCustom)
            {
                try { table.set_Style("Table Grid"); } catch { }
                table.Range.Font.NameFarEast = "宋体";
                table.Range.Font.Size = 12;
                table.Range.ParagraphFormat.Alignment = Word.WdParagraphAlignment.wdAlignParagraphCenter;
                FormatTableHeader(table, true, "#D9E2F3", null, null);
                return;
            }

            if (font != null)
            {
                if (font.name != null) { table.Range.Font.NameFarEast = font.name; table.Range.Font.NameAscii = font.name; }
                if (font.size.HasValue) table.Range.Font.Size = font.size.Value;
                if (font.bold.HasValue) table.Range.Font.Bold = font.bold.Value ? 1 : 0;
                if (font.italic.HasValue) table.Range.Font.Italic = font.italic.Value ? 1 : 0;
                if (font.color != null) table.Range.Font.Color = WordHelper.ParseHexColor(font.color);
            }

            if (alignment != null)
                table.Range.ParagraphFormat.Alignment = WordHelper.ParseAlignment(alignment);

            if (rowHeight.HasValue)
                foreach (Word.Row row in table.Rows)
                {
                    row.HeightRule = Word.WdRowHeightRule.wdRowHeightAtLeast;
                    row.Height = rowHeight.Value;
                }

            if (borders != null)
            {
                var color = borders.color != null ? WordHelper.ParseHexColor(borders.color) : Word.WdColor.wdColorAutomatic;
                if (borders.inside_width.HasValue)
                {
                    SetBorder(table.Borders[Word.WdBorderType.wdBorderHorizontal], borders.inside_width.Value, color);
                    SetBorder(table.Borders[Word.WdBorderType.wdBorderVertical], borders.inside_width.Value, color);
                }
                if (borders.outside_width.HasValue)
                {
                    SetBorder(table.Borders[Word.WdBorderType.wdBorderTop], borders.outside_width.Value, color);
                    SetBorder(table.Borders[Word.WdBorderType.wdBorderBottom], borders.outside_width.Value, color);
                    SetBorder(table.Borders[Word.WdBorderType.wdBorderLeft], borders.outside_width.Value, color);
                    SetBorder(table.Borders[Word.WdBorderType.wdBorderRight], borders.outside_width.Value, color);
                }
            }

            if (header != null)
                FormatTableHeader(table, header.bold ?? true, header.bg_color, header.font_color, header.alignment);

            if (shadingBg != null)
                table.Range.Shading.BackgroundPatternColor = WordHelper.ParseHexColor(shadingBg);
        }

        private static void FormatTableHeader(Word.Table table, bool bold, string bgColor, string fontColor, string alignment)
        {
            if (table.Rows.Count < 1) return;
            var headerRow = table.Rows[1];
            headerRow.Range.Font.Bold = bold ? 1 : 0;
            if (bgColor != null)
                headerRow.Range.Shading.BackgroundPatternColor = WordHelper.ParseHexColor(bgColor);
            if (fontColor != null)
                headerRow.Range.Font.Color = WordHelper.ParseHexColor(fontColor);
            if (alignment != null)
                headerRow.Range.ParagraphFormat.Alignment = WordHelper.ParseAlignment(alignment);
        }

        private static void SetBorder(Word.Border border, float widthPt, Word.WdColor color)
        {
            border.LineStyle = Word.WdLineStyle.wdLineStyleSingle;
            border.Color = color;
            if (widthPt <= 0.5f) border.LineWidth = Word.WdLineWidth.wdLineWidth025pt;
            else if (widthPt <= 1f) border.LineWidth = Word.WdLineWidth.wdLineWidth050pt;
            else if (widthPt <= 1.5f) border.LineWidth = Word.WdLineWidth.wdLineWidth100pt;
            else border.LineWidth = Word.WdLineWidth.wdLineWidth150pt;
        }
    }
}