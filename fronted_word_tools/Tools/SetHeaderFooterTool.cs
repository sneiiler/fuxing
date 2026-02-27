using Newtonsoft.Json.Linq;
using System;
using System.Text.RegularExpressions;
using NetOffice.WordApi;
using NetOffice.WordApi.Enums;

namespace FuXing
{
    /// <summary>设置页眉和页脚内容</summary>
    public class SetHeaderFooterTool : ToolBase
    {
        public override string Name => "set_header_footer";
        public override string DisplayName => "设置页眉页脚";
        public override ToolCategory Category => ToolCategory.PageLayout;

        public override string Description =>
            "Set header or footer text content and formatting.\n" +
            "- type: header / footer\n" +
            "- text: content (use {PAGE} and {NUMPAGES} to insert page number and total pages fields)\n" +
            "- alignment: left/center/right (default: center)\n" +
            "- font_name / font_size: font settings (default: SimSun/9pt)\n" +
            "- section_index: section number; omit for all sections\n" +
            "- page_type: primary=default(odd pages), first_page=first page, even_pages=even pages";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["type"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray("header", "footer"),
                    ["description"] = "设置页眉还是页脚"
                },
                ["text"] = new JObject { ["type"] = "string", ["description"] = "文本内容（{PAGE}=当前页码, {NUMPAGES}=总页数）" },
                ["alignment"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray("left", "center", "right"),
                    ["description"] = "对齐方式（默认 center）"
                },
                ["font_name"] = new JObject { ["type"] = "string", ["description"] = "字体名（默认 宋体）" },
                ["font_size"] = new JObject { ["type"] = "number", ["description"] = "字号（默认 9）" },
                ["section_index"] = new JObject { ["type"] = "integer", ["description"] = "节号（从1开始），不指定则全部节" },
                ["page_type"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray("primary", "first_page", "even_pages"),
                    ["description"] = "页面类型（默认 primary）"
                }
            },
            ["required"] = new JArray("type", "text")
        };

        public override System.Threading.Tasks.Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            string hfType = arguments["type"]?.ToString();
            string text = arguments["text"]?.ToString();

            if (string.IsNullOrWhiteSpace(hfType))
                return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Fail("缺少 type 参数"));
            if (text == null)
                return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Fail("缺少 text 参数"));

            string alignment = arguments["alignment"]?.ToString() ?? "center";
            string fontName = arguments["font_name"]?.ToString() ?? "宋体";
            float fontSize = arguments["font_size"] != null ? (float)arguments["font_size"] : 9f;
            string pageType = arguments["page_type"]?.ToString() ?? "primary";

            var app = connect.WordApplication;
            var doc = app.ActiveDocument;

            WdHeaderFooterIndex hfIndex;
            switch (pageType)
            {
                case "first_page": hfIndex = WdHeaderFooterIndex.wdHeaderFooterFirstPage; break;
                case "even_pages": hfIndex = WdHeaderFooterIndex.wdHeaderFooterEvenPages; break;
                default: hfIndex = WdHeaderFooterIndex.wdHeaderFooterPrimary; break;
            }

            int? sectionIdx = arguments["section_index"] != null ? (int?)arguments["section_index"] : null;

            if (sectionIdx.HasValue)
            {
                if (sectionIdx.Value < 1 || sectionIdx.Value > doc.Sections.Count)
                    return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Fail(
                        $"section_index {sectionIdx.Value} 超出范围（共 {doc.Sections.Count} 节）"));
                SetContent(doc.Sections[sectionIdx.Value], hfType, hfIndex, text, alignment, fontName, fontSize);
                return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Ok(
                    $"已设置第 {sectionIdx.Value} 节的{(hfType == "header" ? "页眉" : "页脚")}"));
            }

            for (int i = 1; i <= doc.Sections.Count; i++)
                SetContent(doc.Sections[i], hfType, hfIndex, text, alignment, fontName, fontSize);

            return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Ok(
                $"已设置全部 {doc.Sections.Count} 节的{(hfType == "header" ? "页眉" : "页脚")}"));
        }

        private void SetContent(Section section, string hfType, WdHeaderFooterIndex hfIndex,
            string text, string alignment, string fontName, float fontSize)
        {
            HeaderFooter hf = hfType == "header"
                ? section.Headers[hfIndex]
                : section.Footers[hfIndex];

            var range = hf.Range;
            range.Text = "";

            if (text.Contains("{PAGE}") || text.Contains("{NUMPAGES}"))
            {
                var parts = text.Split(new[] { "{PAGE}", "{NUMPAGES}" }, StringSplitOptions.None);
                var tokens = Regex.Matches(text, @"\{PAGE\}|\{NUMPAGES\}");

                int tokenIdx = 0;
                for (int i = 0; i < parts.Length; i++)
                {
                    if (!string.IsNullOrEmpty(parts[i]))
                        range.InsertAfter(parts[i]);

                    if (tokenIdx < tokens.Count)
                    {
                        var insertRange = hf.Range;
                        insertRange.Start = insertRange.End;

                        if (tokens[tokenIdx].Value == "{PAGE}")
                            insertRange.Fields.Add(insertRange, WdFieldType.wdFieldPage);
                        else
                            insertRange.Fields.Add(insertRange, WdFieldType.wdFieldNumPages);

                        tokenIdx++;
                    }
                }
            }
            else
            {
                range.Text = text;
            }

            range = hf.Range;
            range.Font.Name = fontName;
            range.Font.Size = fontSize;
            range.ParagraphFormat.Alignment = WordHelper.ParseAlignment(alignment);
        }
    }
}
