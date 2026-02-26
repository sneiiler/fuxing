using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>格式化用户光标所在的表格</summary>
    public class FormatSelectedTableTool : ITool
    {
        public string Name => "format_selected_table";

        public string Description =>
            "格式化用户光标所在的表格。设置宋体12号、居中对齐、1pt外边框、表头加粗灰底。";

        public JObject Parameters => null;

        public Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            var app = connect.WordApplication;
            var selection = app.Selection;
            if (selection.Tables.Count == 0)
                return Task.FromResult(
                    ToolExecutionResult.Fail("当前光标不在表格内，请将光标移至表格中"));

            connect.FormatTablePublic(selection.Tables[1]);
            return Task.FromResult(
                ToolExecutionResult.Ok("选中表格格式化完成（宋体12号、居中对齐、表头加粗灰底）。"));
        }
    }
}
