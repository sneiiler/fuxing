using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>获取当前文档的基本信息</summary>
    public class GetDocumentInfoTool : ITool
    {
        public string Name => "get_document_info";

        public string Description =>
            "获取当前 Word 文档的基本信息，包括文件名、页数、段落数、字数等。";

        public JObject Parameters => null;

        public Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            var app = connect.WordApplication;
            if (app.Documents.Count == 0)
                return Task.FromResult(ToolExecutionResult.Fail("没有打开的文档"));

            var doc = app.ActiveDocument;
            var info = new List<string>
            {
                $"文件名: {doc.Name}",
                $"页数: {doc.ComputeStatistics(NetOffice.WordApi.Enums.WdStatistic.wdStatisticPages)}",
                $"段落数: {doc.Paragraphs.Count}",
                $"字数: {doc.ComputeStatistics(NetOffice.WordApi.Enums.WdStatistic.wdStatisticWords)}",
                $"字符数: {doc.ComputeStatistics(NetOffice.WordApi.Enums.WdStatistic.wdStatisticCharacters)}",
                $"表格数: {doc.Tables.Count}",
                $"批注数: {doc.Comments.Count}"
            };

            return Task.FromResult(
                ToolExecutionResult.Ok(string.Join("\n", info)));
        }
    }
}
