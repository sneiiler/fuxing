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
            "Set document page layout (only the properties you pass are changed; others remain unchanged).\n" +
            "- Margins in points (1cm ≈ 28.35pt). Common values: 2.54cm ≈ 72pt, 3.17cm ≈ 90pt\n" +
            "- paper_size: A3/A4/B5/Letter/Legal or custom (use page_width/page_height)\n" +
            "- orientation: portrait / landscape\n" +
            "- section_index: section number (1-based); omit to apply to all sections";

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
            var app = connect.WordApplication;
            var doc = app.ActiveDocument;

            int? sectionIdx = arguments["section_index"] != null ? (int?)arguments["section_index"] : null;

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
            if (args["top_margin"] != null) ps.TopMargin = (float)args["top_margin"];
            if (args["bottom_margin"] != null) ps.BottomMargin = (float)args["bottom_margin"];
            if (args["left_margin"] != null) ps.LeftMargin = (float)args["left_margin"];
            if (args["right_margin"] != null) ps.RightMargin = (float)args["right_margin"];
            if (args["gutter"] != null) ps.Gutter = (float)args["gutter"];

            if (args["orientation"] != null)
            {
                ps.Orientation = args["orientation"].ToString() == "landscape"
                    ? WdOrientation.wdOrientLandscape
                    : WdOrientation.wdOrientPortrait;
            }

            if (args["paper_size"] != null)
            {
                string size = args["paper_size"].ToString();
                switch (size.ToUpperInvariant())
                {
                    case "A3": ps.PaperSize = WdPaperSize.wdPaperA3; break;
                    case "A4": ps.PaperSize = WdPaperSize.wdPaperA4; break;
                    case "B5": ps.PaperSize = WdPaperSize.wdPaperB5; break;
                    case "LETTER": ps.PaperSize = WdPaperSize.wdPaperLetter; break;
                    case "LEGAL": ps.PaperSize = WdPaperSize.wdPaperLegal; break;
                    case "CUSTOM":
                        ps.PaperSize = WdPaperSize.wdPaperCustom;
                        if (args["page_width"] != null) ps.PageWidth = (float)args["page_width"];
                        if (args["page_height"] != null) ps.PageHeight = (float)args["page_height"];
                        break;
                    default:
                        throw new ArgumentException($"未知纸张大小: {size}");
                }
            }
        }
    }
}
