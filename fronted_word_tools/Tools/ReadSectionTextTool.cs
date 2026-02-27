using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Missing = System.Type;

namespace FuXing
{
    /// <summary>读取文档中指定章节的文本内容</summary>
    public class ReadSectionTextTool : ToolBase
    {
        public override string Name => "read_section_text";
        public override string DisplayName => "读取章节文本";
        public override ToolCategory Category => ToolCategory.Query;

        public override string Description =>
            "Read the plain text content of a heading section in a document. " +
            "Can read from the active document or an external file. " +
            "When a heading name is specified, extracts content from that heading to the next same-level or higher-level heading; " +
            "without a heading name, returns full text (truncated to first 5000 chars). " +
            "Useful for AI analysis of sub-document sections and conflict/duplication detection during merging.";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["file_path"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "外部文档路径。为空则读取当前活动文档"
                },
                ["heading_name"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "要读取的章节标题名称（精确匹配）。为空则读取全文"
                }
            }
        };

        private const int MaxReturnChars = 5000;

        public override Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            string filePath = arguments?["file_path"]?.ToString();
            string headingName = arguments?["heading_name"]?.ToString();
            var app = connect.WordApplication;

            NetOffice.WordApi.Document doc;
            bool isExternalDoc = false;

            if (string.IsNullOrWhiteSpace(filePath))
            {
                if (app.Documents.Count == 0)
                    return Task.FromResult(ToolExecutionResult.Fail("没有打开的文档"));
                doc = app.ActiveDocument;
            }
            else
            {
                if (!System.IO.File.Exists(filePath))
                    return Task.FromResult(ToolExecutionResult.Fail($"文件不存在: {filePath}"));
                var m = Missing.Missing;
                doc = app.Documents.Open(filePath, false, true, false, m, m, m, m, m, m, m, false);
                isExternalDoc = true;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(headingName))
                    return ReadFullText(doc);

                return ReadHeadingSection(doc, headingName);
            }
            finally
            {
                if (isExternalDoc)
                    doc.Close(NetOffice.WordApi.Enums.WdSaveOptions.wdDoNotSaveChanges);
            }
        }

        private Task<ToolExecutionResult> ReadFullText(NetOffice.WordApi.Document doc)
        {
            string text = doc.Content.Text;
            bool truncated = text.Length > MaxReturnChars;
            if (truncated)
                text = text.Substring(0, MaxReturnChars);

            string result = $"文档: {doc.Name}\n字符数: {doc.Content.Text.Length}\n\n{text}";
            if (truncated)
                result += $"\n\n…（已截取前 {MaxReturnChars} 字符，全文共 {doc.Content.Text.Length} 字符）";

            return Task.FromResult(ToolExecutionResult.Ok(result));
        }

        private Task<ToolExecutionResult> ReadHeadingSection(NetOffice.WordApi.Document doc, string headingName)
        {
            // 查找匹配的标题段落
            NetOffice.WordApi.Paragraph targetPara = null;
            int targetLevel = -1;

            foreach (NetOffice.WordApi.Paragraph para in doc.Paragraphs)
            {
                int level = (int)para.OutlineLevel;
                if (level < 1 || level > 6)
                    continue;

                string text = para.Range.Text.Trim();
                if (text.Equals(headingName, StringComparison.OrdinalIgnoreCase))
                {
                    targetPara = para;
                    targetLevel = level;
                    break;
                }
            }

            if (targetPara == null)
                return Task.FromResult(ToolExecutionResult.Fail($"未找到标题: {headingName}"));

            // 从标题开始，找到下一个同级或更高级别标题作为结束位置
            int sectionStart = targetPara.Range.Start;
            // Content.End 指向文档末尾之后，直接传给 Range() 会越界
            int sectionEnd = doc.Content.End - 1;

            bool passedTarget = false;
            foreach (NetOffice.WordApi.Paragraph para in doc.Paragraphs)
            {
                if (para.Range.Start == sectionStart)
                {
                    passedTarget = true;
                    continue;
                }

                if (!passedTarget)
                    continue;

                int level = (int)para.OutlineLevel;
                if (level >= 1 && level <= targetLevel)
                {
                    sectionEnd = para.Range.Start;
                    break;
                }
            }

            var range = doc.Range(sectionStart, sectionEnd);
            string sectionText = range.Text;
            bool truncated = sectionText.Length > MaxReturnChars;
            if (truncated)
                sectionText = sectionText.Substring(0, MaxReturnChars);

            string result = $"文档: {doc.Name}\n章节: {headingName}（{targetLevel}级标题）\n字符数: {range.Text.Length}\n\n{sectionText}";
            if (truncated)
                result += $"\n\n…（已截取前 {MaxReturnChars} 字符，章节共 {range.Text.Length} 字符）";

            return Task.FromResult(ToolExecutionResult.Ok(result));
        }
    }
}
