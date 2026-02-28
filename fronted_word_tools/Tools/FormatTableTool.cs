using Newtonsoft.Json.Linq;
using System.Text;
using NetOffice.WordApi;
using NetOffice.WordApi.Enums;

namespace FuXing
{
    /// <summary>
    /// 通用表格格式化工具，支持自定义字体、边框、底纹、对齐方式等参数。
    /// 不传具体格式参数时使用内置默认样式。
    /// </summary>
    public class FormatTableTool : ToolBase
    {
        public override string Name => "format_table";
        public override string DisplayName => "格式化表格";
        public override ToolCategory Category => ToolCategory.Formatting;

        public override string Description =>
            "Format document tables. table_index: 1-based (0=all, omit=at cursor). " +
            "Customize font, alignment, row_height, borders, header style, shading. " +
            "No style params = default format (SimSun 12pt, centered, 0.5pt borders, bold gray header).";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["table_index"] = new JObject
                {
                    ["type"] = "integer",
                    ["description"] = "表格序号（1开始），0=全部，不指定=光标所在表格"
                },
                ["font"] = new JObject
                {
                    ["type"] = "object",
                    ["description"] = "表格正文字体",
                    ["properties"] = new JObject
                    {
                        ["name"] = new JObject { ["type"] = "string", ["description"] = "字体名" },
                        ["size"] = new JObject { ["type"] = "number", ["description"] = "字号（磅）" },
                        ["bold"] = new JObject { ["type"] = "boolean" },
                        ["italic"] = new JObject { ["type"] = "boolean" },
                        ["color"] = new JObject { ["type"] = "string", ["description"] = "#RRGGBB" }
                    }
                },
                ["alignment"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray("left", "center", "right", "justify"),
                    ["description"] = "单元格文本对齐方式"
                },
                ["row_height"] = new JObject
                {
                    ["type"] = "number",
                    ["description"] = "最小行高（磅）"
                },
                ["borders"] = new JObject
                {
                    ["type"] = "object",
                    ["description"] = "边框设置",
                    ["properties"] = new JObject
                    {
                        ["inside_width"] = new JObject { ["type"] = "number", ["description"] = "内部边框线宽（磅）" },
                        ["outside_width"] = new JObject { ["type"] = "number", ["description"] = "外部边框线宽（磅）" },
                        ["color"] = new JObject { ["type"] = "string", ["description"] = "边框颜色 #RRGGBB，omit=自动" }
                    }
                },
                ["header"] = new JObject
                {
                    ["type"] = "object",
                    ["description"] = "表头行（第一行）样式",
                    ["properties"] = new JObject
                    {
                        ["bold"] = new JObject { ["type"] = "boolean" },
                        ["bg_color"] = new JObject { ["type"] = "string", ["description"] = "表头底纹颜色 #RRGGBB" },
                        ["font_color"] = new JObject { ["type"] = "string", ["description"] = "表头字体颜色 #RRGGBB" },
                        ["alignment"] = new JObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JArray("left", "center", "right"),
                            ["description"] = "表头对齐方式"
                        }
                    }
                },
                ["shading_bg_color"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "表格正文底纹颜色 #RRGGBB，不指定则清除底纹"
                }
            }
        };

        public override System.Threading.Tasks.Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            var doc = RequireActiveDocument(connect);
            int tableCount = doc.Tables.Count;

            if (tableCount == 0)
                throw new ToolArgumentException("文档中没有表格");

            int? idx = arguments?["table_index"]?.Type == JTokenType.Integer
                ? (int?)arguments["table_index"] : null;

            bool hasCustomStyle = arguments?["font"] != null
                || arguments?["alignment"] != null
                || arguments?["row_height"] != null
                || arguments?["borders"] != null
                || arguments?["header"] != null
                || arguments?["shading_bg_color"] != null;

            var summary = new StringBuilder();

            if (idx.HasValue && idx.Value == 0)
            {
                for (int i = 1; i <= tableCount; i++)
                    FormatSingleTable(connect, doc.Tables[i], arguments, hasCustomStyle);
                summary.Append($"已格式化文档中全部 {tableCount} 个表格");
            }
            else if (idx.HasValue)
            {
                if (idx.Value < 1 || idx.Value > tableCount)
                    throw new ToolArgumentException($"table_index {idx.Value} 超出范围（共 {tableCount} 个表格）");
                FormatSingleTable(connect, doc.Tables[idx.Value], arguments, hasCustomStyle);
                summary.Append($"已格式化第 {idx.Value} 个表格（共 {tableCount} 个）");
            }
            else
            {
                var selection = connect.WordApplication.Selection;
                if (selection.Tables.Count == 0)
                    throw new ToolArgumentException("未指定 table_index 且光标不在表格内");
                FormatSingleTable(connect, selection.Tables[1], arguments, hasCustomStyle);
                summary.Append("已格式化光标所在的表格");
            }

            summary.Append(hasCustomStyle ? "（自定义样式）" : "（默认样式）");
            return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Ok(summary.ToString()));
        }

        private void FormatSingleTable(Connect connect, Table table, JObject arguments, bool hasCustomStyle)
        {
            if (!hasCustomStyle)
            {
                connect.FormatTablePublic(table);
                return;
            }

            var font = OptionalObject(arguments, "font");
            string alignment = OptionalString(arguments, "alignment");
            var borders = OptionalObject(arguments, "borders");
            var header = OptionalObject(arguments, "header");
            string shadingBg = OptionalString(arguments, "shading_bg_color");
            float? rowHeight = arguments?["row_height"]?.Type == JTokenType.Float || arguments?["row_height"]?.Type == JTokenType.Integer
                ? (float?)arguments["row_height"].Value<float>() : null;

            // ── 1. 表格正文字体 ──
            if (font != null)
            {
                if (font["name"] != null)
                {
                    table.Range.Font.Name = font["name"].ToString();
                    table.Range.Font.NameFarEast = font["name"].ToString();
                }
                if (font["size"] != null) table.Range.Font.Size = font["size"].Value<float>();
                if (font["bold"] != null) table.Range.Font.Bold = font["bold"].Value<bool>() ? 1 : 0;
                if (font["italic"] != null) table.Range.Font.Italic = font["italic"].Value<bool>() ? 1 : 0;
                if (font["color"] != null) table.Range.Font.Color = WordHelper.ParseHexColor(font["color"].ToString());
            }

            // ── 2. 对齐方式 ──
            if (!string.IsNullOrEmpty(alignment))
                table.Range.ParagraphFormat.Alignment = WordHelper.ParseAlignment(alignment);

            // ── 3. 行高 ──
            if (rowHeight.HasValue)
            {
                table.Rows.HeightRule = WdRowHeightRule.wdRowHeightAtLeast;
                table.Rows.Height = rowHeight.Value;
            }

            // ── 4. 底纹 ──
            if (shadingBg != null)
            {
                table.Shading.BackgroundPatternColor = WordHelper.ParseHexColor(shadingBg);
                table.Shading.Texture = WdTextureIndex.wdTextureNone;
            }
            else
            {
                table.Shading.BackgroundPatternColor = WdColor.wdColorAutomatic;
                table.Shading.Texture = WdTextureIndex.wdTextureNone;
            }

            // ── 5. 边框 ──
            if (borders != null)
            {
                WdColor borderColor = borders["color"] != null
                    ? WordHelper.ParseHexColor(borders["color"].ToString())
                    : WdColor.wdColorAutomatic;

                if (borders["inside_width"] != null)
                {
                    var width = ParseLineWidth(borders["inside_width"].Value<double>());
                    table.Borders.InsideLineStyle = WdLineStyle.wdLineStyleSingle;
                    table.Borders.InsideLineWidth = width;
                    table.Borders.InsideColor = borderColor;
                }

                if (borders["outside_width"] != null)
                {
                    var width = ParseLineWidth(borders["outside_width"].Value<double>());
                    table.Borders.OutsideLineStyle = WdLineStyle.wdLineStyleSingle;
                    table.Borders.OutsideLineWidth = width;
                    table.Borders.OutsideColor = borderColor;
                }
            }

            // ── 6. 表头行 ──
            if (header != null && table.Rows.Count > 0)
            {
                var headerRow = table.Rows[1];

                if (header["bold"] != null)
                    headerRow.Range.Font.Bold = header["bold"].Value<bool>() ? 1 : 0;

                if (header["bg_color"] != null)
                {
                    headerRow.Shading.BackgroundPatternColor = WordHelper.ParseHexColor(header["bg_color"].ToString());
                    headerRow.Shading.Texture = WdTextureIndex.wdTextureNone;
                }

                if (header["font_color"] != null)
                    headerRow.Range.Font.Color = WordHelper.ParseHexColor(header["font_color"].ToString());

                if (header["alignment"] != null)
                    headerRow.Range.ParagraphFormat.Alignment = WordHelper.ParseAlignment(header["alignment"].ToString());

                headerRow.Cells.VerticalAlignment = WdCellVerticalAlignment.wdCellAlignVerticalCenter;
            }
        }

        /// <summary>将磅值映射到 WdLineWidth 枚举最接近的值</summary>
        private static WdLineWidth ParseLineWidth(double pt)
        {
            if (pt <= 0.25) return WdLineWidth.wdLineWidth025pt;
            if (pt <= 0.50) return WdLineWidth.wdLineWidth050pt;
            if (pt <= 0.75) return WdLineWidth.wdLineWidth075pt;
            if (pt <= 1.00) return WdLineWidth.wdLineWidth100pt;
            if (pt <= 1.50) return WdLineWidth.wdLineWidth150pt;
            if (pt <= 2.25) return WdLineWidth.wdLineWidth225pt;
            if (pt <= 3.00) return WdLineWidth.wdLineWidth300pt;
            if (pt <= 4.50) return WdLineWidth.wdLineWidth450pt;
            return WdLineWidth.wdLineWidth600pt;
        }
    }
}
