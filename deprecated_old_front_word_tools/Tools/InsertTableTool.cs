using Newtonsoft.Json.Linq;
using System;
using NetOffice.WordApi;
using NetOffice.WordApi.Enums;

namespace FuXing
{
    /// <summary>从零创建表格，支持设置内容和基本格式</summary>
    public class InsertTableTool : ToolBase
    {
        public override string Name => "insert_table";
        public override string DisplayName => "插入表格";
        public override ToolCategory Category => ToolCategory.Structure;

        public override string Description =>
            "Insert table at cursor. data: 2D array for cell content. " +
            "auto_format: apply default styling (true by default). col_widths: optional column widths in points.";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["rows"] = new JObject { ["type"] = "integer", ["description"] = "行数" },
                ["cols"] = new JObject { ["type"] = "integer", ["description"] = "列数" },
                ["data"] = new JObject
                {
                    ["type"] = "array",
                    ["description"] = "二维数组，按行填充，如 [[\"A1\",\"B1\"],[\"A2\",\"B2\"]]",
                    ["items"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject { ["type"] = "string" }
                    }
                },
                ["auto_format"] = new JObject { ["type"] = "boolean", ["description"] = "是否应用默认格式（默认 true）" },
                ["col_widths"] = new JObject
                {
                    ["type"] = "array",
                    ["description"] = "列宽数组（磅值），不指定则自动分配",
                    ["items"] = new JObject { ["type"] = "number" }
                }
            },
            ["required"] = new JArray("rows", "cols")
        };

        public override System.Threading.Tasks.Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            int rows = RequireInt(arguments, "rows");
            int cols = RequireInt(arguments, "cols");

            if (rows < 1 || rows > 500)
                return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Fail("rows 必须在 1-500 之间"));
            if (cols < 1 || cols > 63)
                return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Fail("cols 必须在 1-63 之间"));

            var doc = RequireActiveDocument(connect);
            var app = connect.WordApplication;

            EnsureNewParagraphIfNeeded(app);
            var range = app.Selection.Range;

            var table = doc.Tables.Add(range, rows, cols);

            // 填充数据
            var data = OptionalArray(arguments, "data");
            if (data != null)
            {
                for (int r = 0; r < data.Count && r < rows; r++)
                {
                    var row = data[r] as JArray;
                    if (row == null) continue;
                    for (int c = 0; c < row.Count && c < cols; c++)
                    {
                        string cellText = row[c]?.ToString() ?? "";
                        table.Cell(r + 1, c + 1).Range.Text = cellText;
                    }
                }
            }

            // 设置列宽
            var colWidths = OptionalArray(arguments, "col_widths");
            if (colWidths != null)
            {
                for (int c = 0; c < colWidths.Count && c < cols; c++)
                    table.Columns[c + 1].Width = colWidths[c].Value<float>();
            }

            // 自动格式化
            bool autoFormat = OptionalBool(arguments, "auto_format", true);
            if (autoFormat)
                connect.FormatTablePublic(table);

            int cellCount = data != null ? Math.Min(rows, data.Count) * cols : 0;
            return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Ok(
                $"已插入 {rows}×{cols} 表格" +
                (data != null ? $"，填充了 {cellCount} 个单元格" : "") +
                (autoFormat ? "，已应用默认格式" : "")));
        }
    }
}
