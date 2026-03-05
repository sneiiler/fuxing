using System;
using System.ComponentModel;
using System.Text;
using Newtonsoft.Json.Linq;
using Word = Microsoft.Office.Interop.Word;

namespace FuXingAgent.Tools
{
    public class ReadContentTool
    {
        private readonly Connect _connect;
        public ReadContentTool(Connect connect) => _connect = connect;

        [Description("Read content from document. type=selection: get selected text. type=table: read table content (markdown/json).")]
        public string read_content(
            [Description("读取类型: selection/table")] string type = "selection",
            [Description("表格序号 1-based, null=光标处（type=table）")] int? table_index = null,
            [Description("表格输出格式: markdown/json（type=table）")] string format = "markdown")
        {
            var app = _connect.WordApplication;
            if (app?.Documents.Count == 0)
                throw new InvalidOperationException("没有活动文档");

            switch (type)
            {
                case "selection":
                    return ReadSelection(app);
                case "table":
                    return ReadTable(app, table_index, format);
                default:
                    throw new ArgumentException($"未知 type: {type}（可用 selection/table）");
            }
        }

        private static string ReadSelection(Word.Application app)
        {
            var sel = app.Selection;
            if (sel == null || sel.Start == sel.End)
                return "当前没有选中任何文本。";

            string text = sel?.Text;
            if (string.IsNullOrEmpty(text) || text.Trim().Length == 0)
                return "当前没有选中任何文本。";

            return $"选中文本（{text.Length} 字符）：\n{text}";
        }

        private static string ReadTable(Word.Application app, int? tableIndex, string format)
        {
            var doc = app.ActiveDocument;
            Word.Table table;

            if (tableIndex.HasValue)
            {
                if (tableIndex.Value < 1 || tableIndex.Value > doc.Tables.Count)
                    throw new InvalidOperationException($"表格索引超出范围（共 {doc.Tables.Count} 个表格）");
                table = doc.Tables[tableIndex.Value];
            }
            else
            {
                var sel = app.Selection;
                if (sel.Tables.Count == 0)
                    throw new InvalidOperationException("光标未在表格内，请指定 table_index");
                table = sel.Tables[1];
            }

            int rows = table.Rows.Count;
            int cols = table.Columns.Count;
            var cells = new string[rows, cols];

            for (int r = 1; r <= rows; r++)
                for (int c = 1; c <= cols; c++)
                {
                    try
                    {
                        string text = table.Cell(r, c).Range.Text ?? "";
                        cells[r - 1, c - 1] = text.TrimEnd('\r', '\n', '\a', '\x07').Trim();
                    }
                    catch { cells[r - 1, c - 1] = ""; }
                }

            if (format == "json")
                return BuildJsonOutput(cells, rows, cols);

            return BuildMarkdownOutput(cells, rows, cols);
        }

        private static string BuildMarkdownOutput(string[,] cells, int rows, int cols)
        {
            var colWidths = new int[cols];
            for (int c = 0; c < cols; c++)
            {
                colWidths[c] = 3;
                for (int r = 0; r < rows; r++)
                {
                    int len = Math.Min(cells[r, c].Length, 40);
                    if (len > colWidths[c]) colWidths[c] = len;
                }
            }

            var sb = new StringBuilder();
            for (int r = 0; r < rows; r++)
            {
                sb.Append("|");
                for (int c = 0; c < cols; c++)
                {
                    string cell = cells[r, c];
                    if (cell.Length > 40) cell = cell.Substring(0, 37) + "...";
                    sb.Append($" {cell.PadRight(colWidths[c])} |");
                }
                sb.AppendLine();

                if (r == 0)
                {
                    sb.Append("|");
                    for (int c = 0; c < cols; c++)
                        sb.Append($" {new string('-', colWidths[c])} |");
                    sb.AppendLine();
                }
            }
            return sb.ToString();
        }

        private static string BuildJsonOutput(string[,] cells, int rows, int cols)
        {
            var arr = new JArray();
            for (int r = 0; r < rows; r++)
            {
                var row = new JArray();
                for (int c = 0; c < cols; c++)
                    row.Add(cells[r, c]);
                arr.Add(row);
            }
            return arr.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
