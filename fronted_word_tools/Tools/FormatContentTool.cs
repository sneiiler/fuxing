using FuXing.Core;
using Newtonsoft.Json.Linq;
using System;
using System.Text;
using NetOffice.WordApi;
using NetOffice.WordApi.Enums;

namespace FuXing
{
    /// <summary>
    /// 统一格式化工具 —— 合并文本格式化、样式应用和样式创建。
    /// action="format"（默认）：对目标范围应用样式和/或直接格式
    /// action="create_style"：创建/更新自定义样式定义
    /// </summary>
    public class FormatContentTool : ToolBase
    {
        public override string Name => "format_content";
        public override string DisplayName => "格式化内容";
        public override ToolCategory Category => ToolCategory.Formatting;

        public override string Description =>
            "Format text/paragraphs or create custom styles. action=\"format\": apply to target range " +
            "(selection/search/heading/node/heading_level/body_text); " +
            "body_text targets ALL body-level paragraphs (including list items). " +
            "can combine style_name with font/paragraph overrides. action=\"create_style\": create or update a named paragraph style. " +
            "Units: 1cm≈28.35pt, Chinese 2-char indent≈28pt.";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["action"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray("format", "create_style"),
                    ["description"] = "操作类型（默认 format）"
                },

                // ── format 模式参数 ──
                ["target"] = new JObject
                {
                    ["type"] = "object",
                    ["description"] = "定位目标（action=format 时使用）",
                    ["properties"] = new JObject
                    {
                        ["type"] = new JObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JArray("selection", "search", "heading", "node", "heading_level", "body_text"),
                            ["description"] = "定位方式"
                        },
                        ["value"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "定位值：搜索文本/标题名/节点ID/标题级别"
                        }
                    },
                    ["required"] = new JArray("type")
                },
                ["style_name"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "样式名（如\"标题 1\"或wdStyleHeading1）"
                },

                // ── create_style 模式参数 ──
                ["name"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "样式名称（action=create_style 时必填）"
                },
                ["based_on"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "基于的样式名（默认\"正文\"）"
                },
                ["next_style"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "回车后切换到的下一个样式名"
                },
                ["style_type"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray("paragraph", "table"),
                    ["description"] = "样式类型（默认 paragraph）"
                },

                // ── 通用格式参数 ──
                ["font"] = new JObject
                {
                    ["type"] = "object",
                    ["description"] = "字体属性（均可选，未传的保持原样）",
                    ["properties"] = new JObject
                    {
                        ["name"] = new JObject { ["type"] = "string", ["description"] = "字体名" },
                        ["name_ascii"] = new JObject { ["type"] = "string", ["description"] = "西文字体名" },
                        ["name_far_east"] = new JObject { ["type"] = "string", ["description"] = "中文字体名" },
                        ["size"] = new JObject { ["type"] = "number", ["description"] = "字号（磅）" },
                        ["bold"] = new JObject { ["type"] = "boolean" },
                        ["italic"] = new JObject { ["type"] = "boolean" },
                        ["underline"] = new JObject { ["type"] = "boolean" },
                        ["strikethrough"] = new JObject { ["type"] = "boolean" },
                        ["color"] = new JObject { ["type"] = "string", ["description"] = "#RRGGBB 格式" }
                    }
                },
                ["paragraph"] = new JObject
                {
                    ["type"] = "object",
                    ["description"] = "段落属性（均可选，未传的保持原样）",
                    ["properties"] = new JObject
                    {
                        ["alignment"] = new JObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JArray("left", "center", "right", "justify")
                        },
                        ["spaceBefore"] = new JObject { ["type"] = "number", ["description"] = "段前间距（磅）" },
                        ["spaceAfter"] = new JObject { ["type"] = "number", ["description"] = "段后间距（磅）" },
                        ["lineSpacingMultiple"] = new JObject { ["type"] = "number", ["description"] = "行距倍数" },
                        ["lineSpacingExact"] = new JObject { ["type"] = "number", ["description"] = "固定行距（磅）" },
                        ["firstLineIndent"] = new JObject { ["type"] = "number", ["description"] = "首行缩进（磅）" },
                        ["leftIndent"] = new JObject { ["type"] = "number", ["description"] = "左缩进（磅）" },
                        ["outlineLevel"] = new JObject { ["type"] = "integer", ["description"] = "大纲级别 1-9，10=正文" }
                    }
                }
            }
        };

        public override System.Threading.Tasks.Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            string action = OptionalString(arguments, "action", "format");
            switch (action)
            {
                case "format":
                    return ExecuteFormat(connect, arguments);
                case "create_style":
                    return ExecuteCreateStyle(connect, arguments);
                default:
                    return System.Threading.Tasks.Task.FromResult(
                        ToolExecutionResult.Fail($"未知 action: {action}，可选: format, create_style"));
            }
        }

        // ═══════════════════════════════════════════════════
        //  action = "format"
        // ═══════════════════════════════════════════════════

        private System.Threading.Tasks.Task<ToolExecutionResult> ExecuteFormat(Connect connect, JObject arguments)
        {
            var targetObj = OptionalObject(arguments, "target");
            if (targetObj == null)
                return System.Threading.Tasks.Task.FromResult(
                    ToolExecutionResult.Fail("action=format 时缺少 target 参数"));

            string styleName = OptionalString(arguments, "style_name");
            var font = OptionalObject(arguments, "font");
            var paragraph = OptionalObject(arguments, "paragraph");

            if (string.IsNullOrWhiteSpace(styleName) && font == null && paragraph == null)
                return System.Threading.Tasks.Task.FromResult(
                    ToolExecutionResult.Fail("至少需要 style_name、font、paragraph 之一"));

            string targetType = targetObj["type"]?.ToString();
            string targetValue = targetObj["value"]?.ToString();

            var app = connect.WordApplication;
            var doc = app.ActiveDocument;

            object styleObj = null;
            if (!string.IsNullOrWhiteSpace(styleName))
                styleObj = ResolveStyleObject(doc, styleName);

            int count = 0;
            var summary = new StringBuilder();

            switch (targetType)
            {
                case "selection":
                    ApplyAll(app.Selection.Range, styleObj, font, paragraph);
                    count = 1;
                    summary.Append("已格式化当前选区");
                    break;

                case "search":
                    if (string.IsNullOrWhiteSpace(targetValue))
                        return System.Threading.Tasks.Task.FromResult(
                            ToolExecutionResult.Fail("search 模式需要 value 参数"));
                    count = FormatBySearch(doc, targetValue, styleObj, font, paragraph);
                    summary.Append($"已格式化搜索到的 {count} 处「{targetValue}」");
                    break;

                case "heading":
                    if (string.IsNullOrWhiteSpace(targetValue))
                        return System.Threading.Tasks.Task.FromResult(
                            ToolExecutionResult.Fail("heading 模式需要 value 参数"));
                    var headingRange = FindHeadingRange(doc, targetValue);
                    if (headingRange == null)
                        return System.Threading.Tasks.Task.FromResult(
                            ToolExecutionResult.Fail($"未找到标题: {targetValue}"));
                    ApplyAll(headingRange, styleObj, font, paragraph);
                    count = 1;
                    summary.Append($"已格式化标题「{targetValue}」");
                    break;

                case "node":
                    if (string.IsNullOrWhiteSpace(targetValue))
                        return System.Threading.Tasks.Task.FromResult(
                            ToolExecutionResult.Fail("node 模式需要 value 参数（节点 ID）"));
                    var nodeRange = ResolveNodeRange(doc, targetValue);
                    ApplyAll(nodeRange, styleObj, font, paragraph);
                    count = 1;
                    summary.Append($"已格式化节点 [{targetValue}]");
                    break;

                case "heading_level":
                    if (!int.TryParse(targetValue, out int level) || level < 1 || level > 9)
                        return System.Threading.Tasks.Task.FromResult(
                            ToolExecutionResult.Fail("heading_level 需要 1-9 的整数"));
                    foreach (Paragraph p in doc.Paragraphs)
                    {
                        if ((int)p.OutlineLevel == level)
                        {
                            ApplyAll(p.Range, styleObj, font, paragraph);
                            count++;
                        }
                    }
                    summary.Append($"已格式化 {count} 个 {level} 级标题");
                    break;

                case "body_text":
                    foreach (Paragraph p in doc.Paragraphs)
                    {
                        if ((int)p.OutlineLevel == (int)WdOutlineLevel.wdOutlineLevelBodyText)
                        {
                            ApplyAll(p.Range, styleObj, font, paragraph);
                            count++;
                        }
                    }
                    summary.Append($"已格式化 {count} 个正文段落（含列表项）");
                    break;

                default:
                    return System.Threading.Tasks.Task.FromResult(
                        ToolExecutionResult.Fail($"未知 target.type: {targetType}"));
            }

            if (count == 0)
                return System.Threading.Tasks.Task.FromResult(
                    ToolExecutionResult.Fail("未找到匹配的目标"));

            if (styleObj != null) summary.Append($"。样式: {styleName}");
            if (font != null) summary.Append($"。字体: {font.ToString(Newtonsoft.Json.Formatting.None)}");
            if (paragraph != null) summary.Append($"。段落: {paragraph.ToString(Newtonsoft.Json.Formatting.None)}");

            return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Ok(summary.ToString()));
        }

        // ═══════════════════════════════════════════════════
        //  action = "create_style"
        // ═══════════════════════════════════════════════════

        private System.Threading.Tasks.Task<ToolExecutionResult> ExecuteCreateStyle(Connect connect, JObject arguments)
        {
            string styleName = OptionalString(arguments, "name");
            if (string.IsNullOrWhiteSpace(styleName))
                return System.Threading.Tasks.Task.FromResult(
                    ToolExecutionResult.Fail("action=create_style 时缺少 name 参数"));

            string basedOn = OptionalString(arguments, "based_on", "正文");
            string nextStyle = OptionalString(arguments, "next_style");
            string styleType = OptionalString(arguments, "style_type", "paragraph");

            var app = connect.WordApplication;
            var doc = app.ActiveDocument;

            dynamic comStyles = ((dynamic)doc.UnderlyingObject).Styles;
            dynamic style = FindOrCreateStyle(comStyles, styleName, styleType);

            if (string.Equals(basedOn, "none", StringComparison.OrdinalIgnoreCase))
            {
                try { style.BaseStyle = ""; }
                catch { /* Word 可能不支持空基准 */ }
            }
            else
            {
                try
                {
                    if (Enum.TryParse<WdBuiltinStyle>(basedOn, ignoreCase: true, out var builtinBase))
                        style.BaseStyle = comStyles[(int)builtinBase];
                    else
                        style.BaseStyle = comStyles[basedOn];
                }
                catch { /* 基础样式不存在时忽略 */ }
            }

            if (!string.IsNullOrWhiteSpace(nextStyle))
            {
                try { style.NextParagraphStyle = comStyles[nextStyle]; }
                catch { /* 忽略 */ }
            }

            var font = OptionalObject(arguments, "font");
            if (font != null) ApplyFont(style.Font, font);

            var paragraph = OptionalObject(arguments, "paragraph");
            if (paragraph != null && styleType == "paragraph") ApplyParagraph(style.ParagraphFormat, paragraph);

            return System.Threading.Tasks.Task.FromResult(
                ToolExecutionResult.Ok($"已创建/更新样式「{styleName}」"));
        }

        // ═══════════════════════════════════════════════════
        //  共用格式化方法
        // ═══════════════════════════════════════════════════

        private void ApplyAll(Range range, object styleObj, JObject font, JObject paragraph)
        {
            if (styleObj != null)
                range.Style = styleObj;
            if (font != null)
                ApplyFont(range.Font, font);
            if (paragraph != null)
                ApplyParagraph(range.ParagraphFormat, paragraph);
        }

        private void ApplyFont(dynamic f, JObject font)
        {
            if (font["name"] != null) f.Name = font["name"].ToString();
            if (font["name_ascii"] != null) f.NameAscii = font["name_ascii"].ToString();
            if (font["name_far_east"] != null) f.NameFarEast = font["name_far_east"].ToString();
            if (font["size"] != null) f.Size = (float)font["size"];
            if (font["bold"] != null) f.Bold = (bool)font["bold"] ? 1 : 0;
            if (font["italic"] != null) f.Italic = (bool)font["italic"] ? 1 : 0;
            if (font["underline"] != null)
                f.Underline = (int)((bool)font["underline"]
                    ? WdUnderline.wdUnderlineSingle
                    : WdUnderline.wdUnderlineNone);
            if (font["strikethrough"] != null) f.StrikeThrough = (bool)font["strikethrough"] ? 1 : 0;
            if (font["color"] != null) f.Color = (int)WordHelper.ParseHexColor(font["color"].ToString());
        }

        private void ApplyParagraph(dynamic pf, JObject paragraph)
        {
            if (paragraph["alignment"] != null)
                pf.Alignment = (int)WordHelper.ParseAlignment(paragraph["alignment"].ToString());
            if (paragraph["spaceBefore"] != null)
                pf.SpaceBefore = (float)paragraph["spaceBefore"];
            if (paragraph["spaceAfter"] != null)
                pf.SpaceAfter = (float)paragraph["spaceAfter"];
            if (paragraph["lineSpacingMultiple"] != null)
            {
                pf.LineSpacingRule = (int)WdLineSpacing.wdLineSpaceMultiple;
                pf.LineSpacing = (float)paragraph["lineSpacingMultiple"] * 12f;
            }
            if (paragraph["lineSpacingExact"] != null)
            {
                pf.LineSpacingRule = (int)WdLineSpacing.wdLineSpaceExactly;
                pf.LineSpacing = (float)paragraph["lineSpacingExact"];
            }
            if (paragraph["firstLineIndent"] != null)
                pf.FirstLineIndent = (float)paragraph["firstLineIndent"];
            if (paragraph["leftIndent"] != null)
                pf.LeftIndent = (float)paragraph["leftIndent"];
            if (paragraph["outlineLevel"] != null)
                pf.OutlineLevel = (int)paragraph["outlineLevel"];
        }

        private int FormatBySearch(Document doc, string searchText,
            object styleObj, JObject font, JObject paragraph)
        {
            int count = 0;
            var range = doc.Content;
            range.Find.ClearFormatting();
            range.Find.Text = searchText;
            range.Find.Forward = true;
            range.Find.Wrap = WdFindWrap.wdFindStop;

            while (range.Find.Execute())
            {
                ApplyAll(range, styleObj, font, paragraph);
                count++;
                range.Start = range.End;
                range.End = doc.Content.End;
                range.Find.ClearFormatting();
                range.Find.Text = searchText;
                range.Find.Forward = true;
                range.Find.Wrap = WdFindWrap.wdFindStop;
            }
            return count;
        }

        private Range FindHeadingRange(Document doc, string headingName)
        {
            foreach (Paragraph para in doc.Paragraphs)
            {
                int level = (int)para.OutlineLevel;
                if (level >= 1 && level <= 9)
                {
                    if (para.Range.Text.Trim().Equals(headingName, StringComparison.OrdinalIgnoreCase))
                        return para.Range;
                }
            }
            return null;
        }

        private object ResolveStyleObject(Document doc, string styleName)
        {
            dynamic comStyles = ((dynamic)doc.UnderlyingObject).Styles;
            if (Enum.TryParse<WdBuiltinStyle>(styleName, ignoreCase: true, out var builtinStyle))
                return comStyles[(int)builtinStyle];
            return comStyles[styleName];
        }

        private dynamic FindOrCreateStyle(dynamic comStyles, string name, string styleType)
        {
            try { return comStyles[name]; }
            catch
            {
                int type = (int)(styleType == "table"
                    ? WdStyleType.wdStyleTypeTable
                    : WdStyleType.wdStyleTypeParagraph);
                return comStyles.Add(name, type);
            }
        }

        /// <summary>通过节点 ID 解析节点范围</summary>
        private Range ResolveNodeRange(Document doc, string nodeIdOrLabel)
        {
            var graph = DocumentGraphCache.Instance.GetOrBuildAsync(doc).Result;
            var node = graph.ResolveNode(nodeIdOrLabel);
            if (node == null)
                throw new ToolArgumentException(
                    $"节点不存在: {nodeIdOrLabel}。请先调用 document_graph(map) 获取有效节点，或检查 label 是否正确。");
            return DocumentGraphCache.Instance.GetNodeRange(doc, node);
        }
    }
}
