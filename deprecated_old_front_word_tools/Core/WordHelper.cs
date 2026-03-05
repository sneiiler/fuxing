using System;
using NetOffice.WordApi.Enums;

namespace FuXing
{
    /// <summary>
    /// Word COM 对象的通用转换辅助方法。
    /// 提取自 FormatContentTool 的 internal static 方法，
    /// 供 InsertImageTool / SetHeaderFooterTool 等多个工具共用。
    /// </summary>
    public static class WordHelper
    {
        /// <summary>将 left/center/right/justify 字符串转换为 WdParagraphAlignment</summary>
        public static WdParagraphAlignment ParseAlignment(string alignment)
        {
            switch (alignment.ToLowerInvariant())
            {
                case "left": return WdParagraphAlignment.wdAlignParagraphLeft;
                case "center": return WdParagraphAlignment.wdAlignParagraphCenter;
                case "right": return WdParagraphAlignment.wdAlignParagraphRight;
                case "justify": return WdParagraphAlignment.wdAlignParagraphJustify;
                default: throw new ArgumentException($"无效对齐方式: {alignment}");
            }
        }

        /// <summary>将 #RRGGBB 十六进制颜色字符串转换为 WdColor</summary>
        public static WdColor ParseHexColor(string hex)
        {
            hex = hex.TrimStart('#');
            if (hex.Length != 6)
                throw new ArgumentException($"无效颜色格式: #{hex}，需要 #RRGGBB");
            int r = Convert.ToInt32(hex.Substring(0, 2), 16);
            int g = Convert.ToInt32(hex.Substring(2, 2), 16);
            int b = Convert.ToInt32(hex.Substring(4, 2), 16);
            return (WdColor)(r | (g << 8) | (b << 16));
        }
    }
}
