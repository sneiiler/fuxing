using Newtonsoft.Json.Linq;
using System;
using NetOffice.WordApi;
using NetOffice.WordApi.Enums;

namespace FuXing
{
    /// <summary>设置文档页面布局（页边距、纸张大小、方向等）</summary>
    public class SetPageSetupTool : ToolBase
    {
        public override string Name => "set_page_setup";
        public override string DisplayName => "页面设置";
        public override ToolCategory Category => ToolCategory.PageLayout;

        public override string Description =>
            "Set page layout (only passed properties change). Margins in points (1cm≈28.35pt, 2.54cm≈72pt). " +
            "paper_size: A3/A4/B5/Letter/Legal/custom. section_index: omit for all sections.";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["top_margin"] = new JObject { ["type"] = "number", ["description"] = "上边距（磅）" },
                ["bottom_margin"] = new JObject { ["type"] = "number", ["description"] = "下边距（磅）" },
                ["left_margin"] = new JObject { ["type"] = "number", ["description"] = "左边距（磅）" },
                ["right_margin"] = new JObject { ["type"] = "number", ["description"] = "右边距（磅）" },
                ["gutter"] = new JObject { ["type"] = "number", ["description"] = "装订线宽度（磅）" },
                ["paper_size"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray("A3", "A4", "B5", "Letter", "Legal", "custom"),
                    ["description"] = "纸张大小"
                },
                ["page_width"] = new JObject { ["type"] = "number", ["description"] = "自定义纸张宽度（磅），paper_size=custom 时使用" },
                ["page_height"] = new JObject { ["type"] = "number", ["description"] = "自定义纸张高度（磅），paper_size=custom 时使用" },
                ["orientation"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray("portrait", "landscape"),
                    ["description"] = "页面方向"
                },
                ["section_index"] = new JObject { ["type"] = "integer", ["description"] = "指定节号（从1开始），不指定则全部节" }
            }
        };

        public override System.Threading.Tasks.Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            var doc = RequireActiveDocument(connect);

            int? sectionIdx = OptionalNullableInt(arguments, "section_index");

            if (sectionIdx.HasValue)
            {
                if (sectionIdx.Value < 1 || sectionIdx.Value > doc.Sections.Count)
                    return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Fail(
                        $"section_index {sectionIdx.Value} 超出范围（共 {doc.Sections.Count} 节）"));
                ApplyPageSetup(doc.Sections[sectionIdx.Value].PageSetup, arguments);
                return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Ok($"已设置第 {sectionIdx.Value} 节的页面布局"));
            }

            for (int i = 1; i <= doc.Sections.Count; i++)
                ApplyPageSetup(doc.Sections[i].PageSetup, arguments);

            return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Ok(
                $"已设置全部 {doc.Sections.Count} 节的页面布局"));
        }

        private void ApplyPageSetup(PageSetup ps, JObject args)
        {
            float? topMargin = OptionalNullableFloat(args, "top_margin");
            float? bottomMargin = OptionalNullableFloat(args, "bottom_margin");
            float? leftMargin = OptionalNullableFloat(args, "left_margin");
            float? rightMargin = OptionalNullableFloat(args, "right_margin");
            float? gutter = OptionalNullableFloat(args, "gutter");

            if (topMargin.HasValue) ps.TopMargin = topMargin.Value;
            if (bottomMargin.HasValue) ps.BottomMargin = bottomMargin.Value;
            if (leftMargin.HasValue) ps.LeftMargin = leftMargin.Value;
            if (rightMargin.HasValue) ps.RightMargin = rightMargin.Value;
            if (gutter.HasValue) ps.Gutter = gutter.Value;

            string orientation = OptionalString(args, "orientation");
            if (orientation != null)
            {
                ps.Orientation = orientation == "landscape"
                    ? WdOrientation.wdOrientLandscape
                    : WdOrientation.wdOrientPortrait;
            }

            string paperSize = OptionalString(args, "paper_size");
            if (paperSize != null)
            {
                switch (paperSize.ToUpperInvariant())
                {
                    case "A3": ps.PaperSize = WdPaperSize.wdPaperA3; break;
                    case "A4": ps.PaperSize = WdPaperSize.wdPaperA4; break;
                    case "B5": ps.PaperSize = WdPaperSize.wdPaperB5; break;
                    case "LETTER": ps.PaperSize = WdPaperSize.wdPaperLetter; break;
                    case "LEGAL": ps.PaperSize = WdPaperSize.wdPaperLegal; break;
                    case "CUSTOM":
                        ps.PaperSize = WdPaperSize.wdPaperCustom;
                        float? pageWidth = OptionalNullableFloat(args, "page_width");
                        float? pageHeight = OptionalNullableFloat(args, "page_height");
                        if (pageWidth.HasValue) ps.PageWidth = pageWidth.Value;
                        if (pageHeight.HasValue) ps.PageHeight = pageHeight.Value;
                        break;
                    default:
                        throw new ArgumentException($"未知纸张大小: {paperSize}");
                }
            }
        }
    }
}
