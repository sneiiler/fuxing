using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>格式化文档中的所有表格</summary>
    public class FormatAllTablesTool : ITool
    {
        public string Name => "format_all_tables";

        public string Description =>
            "格式化文档中的所有表格。统一设置宋体12号、居中对齐、1pt外边框、表头加粗灰底。";

        public JObject Parameters => null;

        public Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            var app = connect.WordApplication;
            if (app.Documents.Count == 0)
                return Task.FromResult(ToolExecutionResult.Fail("没有打开的文档"));

            var activeDoc = app.ActiveDocument;
            int count = activeDoc.Tables.Count;
            if (count == 0)
                return Task.FromResult(ToolExecutionResult.Fail("文档中没有表格"));

            for (int i = 1; i <= count; i++)
                connect.FormatTablePublic(activeDoc.Tables[i]);

            return Task.FromResult(
                ToolExecutionResult.Ok($"已格式化文档中全部 {count} 个表格。"));
        }
    }
}
