using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>将外部文档（或其中某个章节）合入当前文档的指定位置</summary>
    public class MergeDocumentSectionTool : ToolBase
    {
        public override string Name => "merge_document_section";
        public override string DisplayName => "合入文档章节";
        public override ToolCategory Category => ToolCategory.Structure;
        public override bool RequiresApproval => true;

        public override string Description =>
            "Merge external document content into a target heading section (preserving formatting, Track Changes mode). " +
            "Can merge entire file or specific source section. " +
            "Defaults: replace_existing=true, exclude_source_heading=true.";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["source_file_path"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "源文件路径（待合入的子文档）"
                },
                ["target_heading"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "主文档中的目标标题，内容将合入该标题章节的末尾"
                },
                ["source_heading"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "源文件中要提取的章节标题。为空则合入整个文件"
                },
                ["replace_existing"] = new JObject
                {
                    ["type"] = "boolean",
                    ["description"] = "合入前是否先清空目标章节的旧内容（默认 true）。" +
                        "true=先删除目标标题下的现有内容再合入；false=在章节末尾追加"
                },
                ["exclude_source_heading"] = new JObject
                {
                    ["type"] = "boolean",
                    ["description"] = "合入源章节时，是否跳过源标题段落本身（默认 true）。" +
                        "避免产生重复标题"
                }
            },
            ["required"] = new JArray("source_file_path", "target_heading")
        };

        public override Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            string sourceFilePath = RequireString(arguments, "source_file_path");
            string targetHeading = RequireString(arguments, "target_heading");
            string sourceHeading = OptionalString(arguments, "source_heading");
            bool replaceExisting = OptionalBool(arguments, "replace_existing", true);
            bool excludeSourceHeading = OptionalBool(arguments, "exclude_source_heading", true);

            if (!System.IO.File.Exists(sourceFilePath))
                return Task.FromResult(ToolExecutionResult.Fail($"源文件不存在: {sourceFilePath}"));

            var app = connect.WordApplication;
            var mainDoc = RequireActiveDocument(connect);

            // 在主文档中定位目标章节末尾
            int insertPos = FindSectionEnd(mainDoc, targetHeading, out int targetLevel);
            if (insertPos < 0)
                return Task.FromResult(ToolExecutionResult.Fail($"主文档中未找到标题: {targetHeading}"));

            // 若 replaceExisting=true，先清空目标标题下的现有内容（保留标题本身）
            int deletedChars = 0;
            if (replaceExisting)
            {
                int contentStart = FindHeadingEnd(mainDoc, targetHeading);
                if (contentStart > 0 && contentStart < insertPos)
                {
                    var oldRange = mainDoc.Range(contentStart, insertPos);
                    deletedChars = oldRange.Text.Length;
                    oldRange.Delete();
                    // 重新定位插入点（内容删除后位置变化）
                    insertPos = FindSectionEnd(mainDoc, targetHeading, out targetLevel);
                }
            }

            // 开启审阅追踪
            using (BeginTrackRevisions(connect))
            {
                if (string.IsNullOrWhiteSpace(sourceHeading))
                    return MergeWholeFile(mainDoc, insertPos, sourceFilePath, deletedChars);

                return MergeSection(app, mainDoc, insertPos, sourceFilePath, sourceHeading, excludeSourceHeading, deletedChars);
            }
        }

        /// <summary>合入整个文件（使用 InsertFile，性能最优且完美保留格式）</summary>
        private Task<ToolExecutionResult> MergeWholeFile(
            NetOffice.WordApi.Document mainDoc, int insertPos, string sourceFilePath, int deletedChars)
        {
            var insertRange = mainDoc.Range(insertPos, insertPos);
            int beforeEnd = mainDoc.Content.End;

            insertRange.InsertFile(sourceFilePath);

            int afterEnd = mainDoc.Content.End;
            int insertedChars = afterEnd - beforeEnd;

            // 在合入内容的起始位置添加批注
            AddMergeComment(mainDoc, insertPos, sourceFilePath, null);

            string extra = deletedChars > 0 ? $"（合入前已清空旧内容 {deletedChars} 字符）" : "";
            string mainDocName = mainDoc.Name ?? "(未知)";
            return Task.FromResult(
                ToolExecutionResult.Ok(
                    $"已将整个文件合入主文档「{mainDocName}」（插入位置: {insertPos}，插入字符数: {insertedChars}）。{extra}\n" +
                    $"源文件: {System.IO.Path.GetFileName(sourceFilePath)}"));
        }

        /// <summary>合入源文件中指定章节（通过 Copy/Paste 保留格式）</summary>
        private Task<ToolExecutionResult> MergeSection(
            NetOffice.WordApi.Application app,
            NetOffice.WordApi.Document mainDoc,
            int insertPos,
            string sourceFilePath,
            string sourceHeading,
            bool excludeSourceHeading,
            int deletedChars)
        {
            var (sourceDoc, shouldCloseSourceDoc) = DocumentHelper.GetOrOpenReadOnly(app, sourceFilePath);

            try
            {
                // 在源文档中定位章节范围
                var sectionRange = FindSectionRange(sourceDoc, sourceHeading);
                if (sectionRange == null)
                    return Task.FromResult(ToolExecutionResult.Fail($"源文件中未找到标题: {sourceHeading}"));

                // 若排除源标题本身，将范围起点移到标题段落之后
                if (excludeSourceHeading)
                {
                    int headingEnd = FindSourceHeadingEnd(sourceDoc, sourceHeading);
                    if (headingEnd > sectionRange.Start && headingEnd < sectionRange.End)
                        sectionRange = sourceDoc.Range(headingEnd, sectionRange.End);
                }

                int sectionLength = sectionRange.Text.Length;

                // 复制源章节内容
                sectionRange.Copy();

                // 在主文档中粘贴
                var insertRange = mainDoc.Range(insertPos, insertPos);
                insertRange.Paste();

                // 在合入内容的起始位置添加批注
                AddMergeComment(mainDoc, insertPos, sourceFilePath, sourceHeading);

                string extra = deletedChars > 0 ? $"（合入前已清空旧内容 {deletedChars} 字符）" : "";
                string skipNote = excludeSourceHeading ? "（已跳过源标题段落）" : "";
                string mainDocName = mainDoc.Name ?? "(未知)";
                return Task.FromResult(
                    ToolExecutionResult.Ok(
                        $"已将章节「{sourceHeading}」合入主文档「{mainDocName}」（插入位置: {insertPos}，章节字符数: {sectionLength}）。{skipNote}{extra}\n" +
                        $"源文件: {System.IO.Path.GetFileName(sourceFilePath)}"));
            }
            finally
            {
                if (shouldCloseSourceDoc)
                    sourceDoc.Close(NetOffice.WordApi.Enums.WdSaveOptions.wdDoNotSaveChanges);
            }
        }



        /// <summary>在合入内容起始位置添加批注，标注合入时间和来源</summary>
        private void AddMergeComment(
            NetOffice.WordApi.Document doc, int insertPos, string sourceFilePath, string sourceHeading)
        {
            string fileName = System.IO.Path.GetFileName(sourceFilePath);
            string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            string comment = $"[合入] {time}\n来源: {fileName}";
            if (!string.IsNullOrWhiteSpace(sourceHeading))
                comment += $"\n章节: {sourceHeading}";

            // 选取合入内容的第一个段落作为批注锚点
            var anchorRange = doc.Range(insertPos, insertPos);
            // 扩展到该段落末尾，使批注附着在第一个段落上
            anchorRange.Expand(NetOffice.WordApi.Enums.WdUnits.wdParagraph);

            doc.Comments.Add(anchorRange, comment);
        }

        /// <summary>查找指定标题段落的结束位置（即标题文字之后、正文之前）</summary>
        private int FindHeadingEnd(NetOffice.WordApi.Document doc, string headingName)
        {
            foreach (NetOffice.WordApi.Paragraph para in doc.Paragraphs)
            {
                int level = (int)para.OutlineLevel;
                if (level < 1 || level > 6)
                    continue;

                string text = para.Range.Text.Trim();
                if (text.Equals(headingName, StringComparison.OrdinalIgnoreCase))
                    return para.Range.End;
            }
            return -1;
        }

        /// <summary>在源文档中查找标题段落结束位置</summary>
        private int FindSourceHeadingEnd(NetOffice.WordApi.Document doc, string headingName)
        {
            return FindHeadingEnd(doc, headingName);
        }

        /// <summary>在文档中查找目标标题的章节末尾位置</summary>
        private int FindSectionEnd(NetOffice.WordApi.Document doc, string headingName, out int headingLevel)
        {
            headingLevel = -1;
            NetOffice.WordApi.Paragraph targetPara = null;

            foreach (NetOffice.WordApi.Paragraph para in doc.Paragraphs)
            {
                int level = (int)para.OutlineLevel;
                if (level < 1 || level > 6)
                    continue;

                string text = para.Range.Text.Trim();
                if (text.Equals(headingName, StringComparison.OrdinalIgnoreCase))
                {
                    targetPara = para;
                    headingLevel = level;
                    break;
                }
            }

            if (targetPara == null)
                return -1;

            // 找下一个同级或更高级别标题的起始位置
            bool passedTarget = false;
            foreach (NetOffice.WordApi.Paragraph para in doc.Paragraphs)
            {
                if (para.Range.Start == targetPara.Range.Start)
                {
                    passedTarget = true;
                    continue;
                }

                if (!passedTarget)
                    continue;

                int level = (int)para.OutlineLevel;
                if (level >= 1 && level <= headingLevel)
                    return para.Range.Start;
            }

            // Content.End 指向文档末尾之后，直接传给 Range() 会越界
            return doc.Content.End - 1;
        }

        /// <summary>在文档中查找指定标题章节的 Range</summary>
        private NetOffice.WordApi.Range FindSectionRange(NetOffice.WordApi.Document doc, string headingName)
        {
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
                return null;

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

            return doc.Range(sectionStart, sectionEnd);
        }
    }
}
