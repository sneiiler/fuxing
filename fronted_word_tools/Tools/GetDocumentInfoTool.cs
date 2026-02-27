using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>获取当前文档的基本信息</summary>
    public class GetDocumentInfoTool : ToolBase
    {
        public override string Name => "get_document_info";
        public override string DisplayName => "获取文档信息";
        public override ToolCategory Category => ToolCategory.Query;

        public override string Description =>
            "Get basic information about the current Word document, including file name, page count, paragraph count, word count, etc.";

        public override JObject Parameters => null;

        public override Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
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
