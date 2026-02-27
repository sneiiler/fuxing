using Newtonsoft.Json.Linq;
using NetOffice.WordApi;
using NetOffice.WordApi.Enums;

namespace FuXing
{
    /// <summary>插入或更新文档目录</summary>
    public class InsertTocTool : ToolBase
    {
        public override string Name => "insert_toc";
        public override string DisplayName => "插入目录";
        public override ToolCategory Category => ToolCategory.Structure;

        public override string Description =>
            "Insert or update a Table of Contents in the document.\n" +
            "- action: insert=insert new TOC at cursor, update=update existing TOC\n" +
            "- heading_levels: range of heading levels to include, e.g. \"1-3\" (default \"1-3\")\n" +
            "- For insertion, position cursor at the desired TOC location (typically after title page or at beginning)";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["action"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray("insert", "update"),
                    ["description"] = "insert=插入新目录, update=更新已有目录（默认 insert）"
                },
                ["heading_levels"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "标题级别范围，如 \"1-3\"（默认 \"1-3\"）"
                }
            }
        };

        public override System.Threading.Tasks.Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            string action = arguments?["action"]?.ToString() ?? "insert";
            string headingLevels = arguments?["heading_levels"]?.ToString() ?? "1-3";

            var app = connect.WordApplication;
            var doc = app.ActiveDocument;

            if (action == "update")
            {
                if (doc.TablesOfContents.Count == 0)
                    return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Fail("文档中没有目录，请先插入目录"));

                for (int i = 1; i <= doc.TablesOfContents.Count; i++)
                    doc.TablesOfContents[i].Update();

                return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Ok(
                    $"已更新 {doc.TablesOfContents.Count} 个目录"));
            }

            // 解析级别范围
            int lowerLevel = 1, upperLevel = 3;
            var parts = headingLevels.Split('-');
            if (parts.Length == 2)
            {
                int.TryParse(parts[0].Trim(), out lowerLevel);
                int.TryParse(parts[1].Trim(), out upperLevel);
            }
            if (lowerLevel < 1) lowerLevel = 1;
            if (upperLevel > 9) upperLevel = 9;

            var range = app.Selection.Range;

            // 使用位置参数: Add(range, useHeadingStyles, upperHeadingLevel, lowerHeadingLevel)
            doc.TablesOfContents.Add(range, true, lowerLevel, upperLevel);

            return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Ok(
                $"已在光标位置插入目录（{lowerLevel}-{upperLevel} 级标题）"));
        }
    }
}
