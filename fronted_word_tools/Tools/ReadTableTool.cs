using FuXing.Core;
using Newtonsoft.Json.Linq;
using System.Text;
using NetOffice.WordApi;

namespace FuXing
{
    /// <summary>
    /// 将 Word 表格结构化读取为 Markdown 表格或 JSON 数组，
    /// 解决 Range.Text 将表格压平为纯文本、丢失行列结构的问题。
    /// </summary>
    public class ReadTableTool : ToolBase
    {
        public override string Name => "read_table";
        public override string DisplayName => "读取表格";
        public override ToolCategory Category => ToolCategory.Query;

        public override string Description =>
            "Read table as structured Markdown or JSON. table_index: 1-based (omit = at cursor). " +
            "node_id: target Table node from document_graph (overrides table_index).";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["table_index"] = new JObject
                {
                    ["type"] = "integer",
                    ["description"] = "表格序号（从1开始），不指定则读取光标所在表格"
                },
                ["node_id"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "目标 Table 节点 ID（从 document_graph expand 获取，优先于 table_index）"
                },
                ["format"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray("markdown", "json"),
                    ["description"] = "输出格式（默认 markdown）"
                }
            }
        };

        public override System.Threading.Tasks.Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            var doc = RequireActiveDocument(connect);
            int tableCount = doc.Tables.Count;

            if (tableCount == 0)
                throw new ToolArgumentException("文档中没有表格");

            string format = OptionalString(arguments, "format", "markdown");
            string nodeId = OptionalString(arguments, "node_id");

            Table table;
            int tableNum;

            if (!string.IsNullOrWhiteSpace(nodeId))
            {
                // 通过图节点 ID 定位
                table = ResolveTableByNodeId(doc, nodeId);
                // 找到该表格的序号
                tableNum = 0;
                for (int i = 1; i <= tableCount; i++)
                {
                    if (doc.Tables[i].Range.Start == table.Range.Start)
                    { tableNum = i; break; }
                }
            }
            else
            {
            int? idx = arguments?["table_index"]?.Type == JTokenType.Integer
                ? (int?)arguments["table_index"] : null;

            if (idx.HasValue)
            {
                if (idx.Value < 1 || idx.Value > tableCount)
                    throw new ToolArgumentException($"table_index {idx.Value} 超出范围（共 {tableCount} 个表格）");
                table = doc.Tables[idx.Value];
                tableNum = idx.Value;
            }
            else
            {
                var selection = connect.WordApplication.Selection;
                if (selection.Tables.Count == 0)
                    throw new ToolArgumentException("未指定 table_index 且光标不在表格内");
                table = selection.Tables[1];

                // 找到该表格的序号
                tableNum = 0;
                for (int i = 1; i <= tableCount; i++)
                {
                    if (doc.Tables[i].Range.Start == table.Range.Start)
                    {
                        tableNum = i;
                        break;
                    }
                }
            }
            } // end else (non-nodeId path)

            int rows = table.Rows.Count;
            int cols = table.Columns.Count;

            // 读取所有单元格内容
            string[,] cells = new string[rows, cols];
            for (int r = 1; r <= rows; r++)
            {
                for (int c = 1; c <= cols; c++)
                {
                    try
                    {
                        string cellText = table.Cell(r, c).Range.Text;
                        // Word 表格 Cell.Range.Text 末尾带 \r\a，需要清理
                        cellText = cellText.TrimEnd('\r', '\n', '\a', '\x07');
                        cells[r - 1, c - 1] = cellText;
                    }
                    catch
                    {
                        // 合并单元格可能导致访问异常
                        cells[r - 1, c - 1] = "";
                    }
                }
            }

            string result;
            if (format == "json")
                result = BuildJson(cells, rows, cols);
            else
                result = BuildMarkdown(cells, rows, cols);

            var sb = new StringBuilder();
            sb.AppendLine($"表格 #{tableNum}（{rows}行 × {cols}列）");
            sb.AppendLine();
            sb.Append(result);

            return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Ok(sb.ToString()));
        }

        private string BuildMarkdown(string[,] cells, int rows, int cols)
        {
            var sb = new StringBuilder();

            // 计算每列的最大宽度（用于对齐）
            int[] widths = new int[cols];
            for (int c = 0; c < cols; c++)
            {
                widths[c] = 3; // 最小宽度
                for (int r = 0; r < rows; r++)
                {
                    int len = (cells[r, c] ?? "").Length;
                    if (len > widths[c]) widths[c] = len;
                }
                if (widths[c] > 40) widths[c] = 40; // 限制最大宽度
            }

            // 表头行
            sb.Append("|");
            for (int c = 0; c < cols; c++)
            {
                string cell = cells[0, c] ?? "";
                if (cell.Length > 40) cell = cell.Substring(0, 37) + "...";
                sb.Append($" {cell.PadRight(widths[c])} |");
            }
            sb.AppendLine();

            // 分隔行
            sb.Append("|");
            for (int c = 0; c < cols; c++)
                sb.Append($" {new string('-', widths[c])} |");
            sb.AppendLine();

            // 数据行
            for (int r = 1; r < rows; r++)
            {
                sb.Append("|");
                for (int c = 0; c < cols; c++)
                {
                    string cell = cells[r, c] ?? "";
                    if (cell.Length > 40) cell = cell.Substring(0, 37) + "...";
                    sb.Append($" {cell.PadRight(widths[c])} |");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private string BuildJson(string[,] cells, int rows, int cols)
        {
            var arr = new JArray();
            for (int r = 0; r < rows; r++)
            {
                var row = new JArray();
                for (int c = 0; c < cols; c++)
                    row.Add(cells[r, c] ?? "");
                arr.Add(row);
            }
            return arr.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        /// <summary>通过节点 ID 定位表格</summary>
        private Table ResolveTableByNodeId(Document doc, string nodeIdOrLabel)
        {
            var graph = DocumentGraphCache.Instance.GetOrBuildAsync(doc).Result;
            var node = graph.ResolveNode(nodeIdOrLabel);
            if (node == null)
                throw new ToolArgumentException(
                    $"节点不存在: {nodeIdOrLabel}。请先调用 document_graph(map) + expand 获取表格节点。");
            if (node.Type != DocNodeType.Table)
                throw new ToolArgumentException(
                    $"节点 [{node.Id}] 类型为 {node.Type}，不是 Table 节点");
            if (node.AnchorLabel == null)
                throw new ToolArgumentException($"节点 {node.Id} 无锚点");

            var range = DocumentGraphCache.Instance.Anchors.GetRange(doc, node.AnchorLabel);

            foreach (Table table in doc.Tables)
            {
                if (table.Range.Start == range.Start)
                    return table;
            }

            throw new ToolArgumentException(
                $"无法定位节点 [{nodeIdOrLabel}] 对应的表格（可能文档已被编辑，请重建图）");
        }
    }
}
