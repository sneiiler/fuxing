using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>格式化指定表格（按序号或光标位置）</summary>
    public class FormatTableTool : ToolBase
    {
        public override string Name => "format_table";
        public override string DisplayName => "格式化表格";
        public override ToolCategory Category => ToolCategory.Formatting;

        public override string Description =>
            "Format tables in the document. Applies SimSun 12pt, centered alignment, 1pt outer borders, bold gray header row.\n" +
            "- table_index: table number (1-based); omit to format the table at cursor position\n" +
            "- Set to 0 to format all tables in the document";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["table_index"] = new JObject
                {
                    ["type"] = "integer",
                    ["description"] = "表格序号（从1开始），0=全部表格，不指定=光标所在表格"
                }
            }
        };

        public override Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            var app = connect.WordApplication;
            var doc = app.ActiveDocument;
            int tableCount = doc.Tables.Count;

            if (tableCount == 0)
                return Task.FromResult(ToolExecutionResult.Fail("文档中没有表格"));

            int? idx = arguments?["table_index"]?.Type == JTokenType.Integer
                ? (int?)arguments["table_index"] : null;

            // 格式化所有表格
            if (idx.HasValue && idx.Value == 0)
            {
                for (int i = 1; i <= tableCount; i++)
                    connect.FormatTablePublic(doc.Tables[i]);
                return Task.FromResult(
                    ToolExecutionResult.Ok($"已格式化文档中全部 {tableCount} 个表格"));
            }

            // 按序号格式化
            if (idx.HasValue)
            {
                if (idx.Value < 1 || idx.Value > tableCount)
                    return Task.FromResult(ToolExecutionResult.Fail(
                        $"table_index {idx.Value} 超出范围（共 {tableCount} 个表格）"));
                connect.FormatTablePublic(doc.Tables[idx.Value]);
                return Task.FromResult(
                    ToolExecutionResult.Ok($"已格式化第 {idx.Value} 个表格（共 {tableCount} 个）"));
            }

            // 默认：光标所在表格
            var selection = app.Selection;
            if (selection.Tables.Count == 0)
                return Task.FromResult(
                    ToolExecutionResult.Fail("未指定 table_index 且光标不在表格内"));

            connect.FormatTablePublic(selection.Tables[1]);
            return Task.FromResult(
                ToolExecutionResult.Ok("已格式化光标所在的表格"));
        }
    }
}
