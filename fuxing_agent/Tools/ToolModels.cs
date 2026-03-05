using System.Collections.Generic;
using System.ComponentModel;

namespace FuXingAgent.Tools
{
    // ════format_content 参数模型 ════

    public class FormatTarget
    {
        [Description("定位类型: selection/search/heading/heading_level/body_text")]
        public string type { get; set; }

        [Description("定位值（search=搜索文本, heading=标题注: heading_level=1-9 级别数字")]
        public string value { get; set; }
    }

    public class FontOptions
    {
        [Description("字体名称（同时设置中西文字体")]
        public string name { get; set; }

        [Description("西文字体")]
        public string name_ascii { get; set; }

        [Description("东亚字体")]
        public string name_far_east { get; set; }

        [Description("字号（磅")]
        public float? size { get; set; }

        [Description("粗体")]
        public bool? bold { get; set; }

        [Description("斜体")]
        public bool? italic { get; set; }

        [Description("下划")]
        public bool? underline { get; set; }

        [Description("删除")]
        public bool? strikethrough { get; set; }
    }

    public class ParagraphOptions
    {
        [Description("对齐方式: left/center/right/justify")]
        public string alignment { get; set; }

        [Description("段前间距（磅")]
        public float? space_before_pt { get; set; }

        [Description("段后间距（磅")]
        public float? space_after_pt { get; set; }

        [Description("首行缩进（磅")]
        public float? first_line_indent_pt { get; set; }

        [Description("左缩进（磅）")]
        public float? left_indent_pt { get; set; }

        [Description("行距倍数（如 1.5 = 1.5 倍行距）")]
        public float? line_spacing_multiple { get; set; }

        [Description("固定行距（磅")]
        public float? line_spacing_exact { get; set; }

        [Description("大纲级别（1-9, 10=正文档")]
        public int? outline_level { get; set; }
    }

    // ════batch_operations 参数模型 ════

    public class BatchOperation
    {
        [Description("工具名称")]
        public string tool { get; set; }

        [Description("工具参数")]
        public Dictionary<string, object> args { get; set; }
    }

    // ════format_table 参数模型 ════

    public class TableFontOptions
    {
        [Description("字体名称")]
        public string name { get; set; }

        [Description("字号（磅")]
        public float? size { get; set; }

        [Description("粗体")]
        public bool? bold { get; set; }

        [Description("斜体")]
        public bool? italic { get; set; }

        [Description("字体颜色（hex 如 #333333")]
        public string color { get; set; }
    }

    public class TableBorderOptions
    {
        [Description("边框颜色（hex")]
        public string color { get; set; }

        [Description("内部线宽（磅")]
        public float? inside_width { get; set; }

        [Description("外部线宽（磅")]
        public float? outside_width { get; set; }
    }

    public class TableHeaderOptions
    {
        [Description("表头粗体")]
        public bool? bold { get; set; }

        [Description("表头背景色（hex")]
        public string bg_color { get; set; }

        [Description("表头字体颜色（hex")]
        public string font_color { get; set; }

        [Description("表头对齐")]
        public string alignment { get; set; }
    }

    // ════ask_user 参数模型 ════

    public class AskUserOption
    {
        [Description("选项文本")]
        public string label { get; set; }

        [Description("选项描述")]
        public string description { get; set; }
    }
}