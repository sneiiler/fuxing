using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using FuXingAgent.Agents;
using FuXingAgent.Core;
using Word = Microsoft.Office.Interop.Word;

namespace FuXingAgent.Workflows
{
    /// <summary>
    /// 合稿工作流 — 固定三步流水线：验证输入 → 执行合并 → 生成报告。
    /// 每一步都保证确定性执行，出错可追溯。
    /// </summary>
    public class MergeDocumentWorkflow
    {
        private readonly Connect _connect;
        public MergeDocumentWorkflow(Connect connect) => _connect = connect;

        [Description("Merge external document content into a target heading section using a fixed workflow: " +
            "1) validate source file and target heading exist, 2) execute merge in Track Changes mode, " +
            "3) generate verification report. Can merge entire file or specific source section.")]
        public string merge_document_workflow(
            [Description("源文件路径（待合入的子文档）")] string source_file_path,
            [Description("主文档中的目标标题，内容将合入该标题章节末尾")] string target_heading,
            [Description("源文件中要提取的章节标题（空则合入整个文件）")] string source_heading = null,
            [Description("合入前是否先清空目标章节的旧内容")] bool replace_existing = true,
            [Description("是否跳过源标题段落本身（避免重复标题）")] bool exclude_source_heading = true)
        {
            var app = _connect.WordApplication;
            var mainDoc = app.ActiveDocument ?? throw new InvalidOperationException("没有活动文档");

            // ═══ Step 1: 验证输入 ═══
            var validation = ValidateInputs(app, mainDoc, source_file_path, target_heading, source_heading);
            DebugLogger.Instance.LogInfo($"[MergeDocumentWorkflow] Step1 验证通过: {validation}");

            // ═══ Step 2: 执行合并 ═══
            int beforeCharCount = mainDoc.Content.End;
            string mergeResult = ExecuteMerge(app, mainDoc, source_file_path, target_heading,
                source_heading, replace_existing, exclude_source_heading);
            int afterCharCount = mainDoc.Content.End;
            DebugLogger.Instance.LogInfo($"[MergeDocumentWorkflow] Step2 合并完成");

            // ═══ Step 3: 生成报告 ═══
            string report = BuildReport(mainDoc, source_file_path, target_heading, source_heading,
                replace_existing, beforeCharCount, afterCharCount, mergeResult);
            DebugLogger.Instance.LogInfo($"[MergeDocumentWorkflow] Step3 报告生成");

            return report;
        }

        /// <summary>Step 1: 验证所有输入参数的合法性</summary>
        private static string ValidateInputs(
            Word.Application app, Word.Document mainDoc,
            string sourceFilePath, string targetHeading, string sourceHeading)
        {
            // 验证源文件
            if (string.IsNullOrWhiteSpace(sourceFilePath))
                throw new ArgumentException("缺少 source_file_path 参数");

            if (!File.Exists(sourceFilePath))
                throw new InvalidOperationException($"源文件不存在: {sourceFilePath}");

            string ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();
            if (ext != ".docx" && ext != ".doc")
                throw new InvalidOperationException($"不支持的文件格式: {ext}（仅支持 .docx, .doc）");

            // 验证目标标题
            if (string.IsNullOrWhiteSpace(targetHeading))
                throw new ArgumentException("缺少 target_heading 参数");

            int targetLevel;
            int sectionEnd = FindSectionEnd(mainDoc, targetHeading, out targetLevel);
            if (sectionEnd < 0)
                throw new InvalidOperationException($"主文档中未找到标题: {targetHeading}");

            // 验证源章节（如果指定了）
            if (!string.IsNullOrWhiteSpace(sourceHeading))
            {
                ValidateSourceHeading(app, sourceFilePath, sourceHeading);
            }

            return $"源文件={Path.GetFileName(sourceFilePath)}, 目标标题={targetHeading}(L{targetLevel})";
        }

        /// <summary>打开源文件验证章节标题是否存在</summary>
        private static void ValidateSourceHeading(Word.Application app, string sourceFilePath, string sourceHeading)
        {
            object readOnlyObj = true;
            object fileNameObj = sourceFilePath;
            object missing = Type.Missing;

            Word.Document sourceDoc = app.Documents.Open(
                ref fileNameObj, ref missing, ref readOnlyObj,
                ref missing, ref missing, ref missing, ref missing, ref missing,
                ref missing, ref missing, ref missing, ref missing,
                ref missing, ref missing, ref missing, ref missing);

            try
            {
                var range = FindSectionRange(sourceDoc, sourceHeading);
                if (range == null)
                    throw new InvalidOperationException($"源文件中未找到标题: {sourceHeading}");
            }
            finally
            {
                object doNotSave = Word.WdSaveOptions.wdDoNotSaveChanges;
                sourceDoc.Close(ref doNotSave);
            }
        }

        /// <summary>Step 2: 执行实际的合并操作</summary>
        private static string ExecuteMerge(
            Word.Application app, Word.Document mainDoc,
            string sourceFilePath, string targetHeading,
            string sourceHeading, bool replaceExisting, bool excludeSourceHeading)
        {
            int targetLevel;
            int insertPos = FindSectionEnd(mainDoc, targetHeading, out targetLevel);

            int deletedChars = 0;
            if (replaceExisting)
            {
                int contentStart = FindHeadingEnd(mainDoc, targetHeading);
                if (contentStart > 0 && contentStart < insertPos)
                {
                    var oldRange = mainDoc.Range(contentStart, insertPos);
                    deletedChars = oldRange.Text.Length;
                    oldRange.Delete();
                    insertPos = FindSectionEnd(mainDoc, targetHeading, out targetLevel);
                }
            }

            using (WordHelper.BeginTrackRevisions(app))
            {
                if (string.IsNullOrWhiteSpace(sourceHeading))
                    return MergeWholeFile(mainDoc, insertPos, sourceFilePath, deletedChars);

                return MergeSection(app, mainDoc, insertPos, sourceFilePath,
                    sourceHeading, excludeSourceHeading, deletedChars);
            }
        }

        /// <summary>Step 3: 生成合并报告</summary>
        private static string BuildReport(
            Word.Document mainDoc, string sourceFilePath, string targetHeading,
            string sourceHeading, bool replaceExisting,
            int beforeCharCount, int afterCharCount, string mergeResult)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 合稿工作流执行报告 ===");
            sb.AppendLine($"主文档: {mainDoc.Name}");
            sb.AppendLine($"源文件: {Path.GetFileName(sourceFilePath)}");
            sb.AppendLine($"目标章节: {targetHeading}");
            if (!string.IsNullOrWhiteSpace(sourceHeading))
                sb.AppendLine($"源章节: {sourceHeading}");
            sb.AppendLine($"替换旧内容: {(replaceExisting ? "是" : "否")}");
            sb.AppendLine($"文档字符变化: {beforeCharCount} -> {afterCharCount} (差值: {afterCharCount - beforeCharCount})");
            sb.AppendLine();
            sb.AppendLine(mergeResult);
            return sb.ToString();
        }

        // ═══════════════════════════════════════════════════════════════
        //  合并操作实现
        // ═══════════════════════════════════════════════════════════════

        private static string MergeWholeFile(Word.Document mainDoc, int insertPos,
            string sourceFilePath, int deletedChars)
        {
            var insertRange = mainDoc.Range(insertPos, insertPos);
            int beforeEnd = mainDoc.Content.End;

            insertRange.InsertFile(sourceFilePath);

            int insertedChars = mainDoc.Content.End - beforeEnd;
            AddMergeComment(mainDoc, insertPos, sourceFilePath, null);

            string extra = deletedChars > 0 ? $"（合入前已清空旧内容 {deletedChars} 字符）" : "";
            return $"已将整个文件合入主文档（插入字符数: {insertedChars}）。{extra}\n" +
                   $"源文件: {Path.GetFileName(sourceFilePath)}";
        }

        private static string MergeSection(Word.Application app, Word.Document mainDoc,
            int insertPos, string sourceFilePath, string sourceHeading,
            bool excludeSourceHeading, int deletedChars)
        {
            object readOnlyObj = true;
            object fileNameObj = sourceFilePath;
            object missing = Type.Missing;

            Word.Document sourceDoc = app.Documents.Open(
                ref fileNameObj, ref missing, ref readOnlyObj,
                ref missing, ref missing, ref missing, ref missing, ref missing,
                ref missing, ref missing, ref missing, ref missing,
                ref missing, ref missing, ref missing, ref missing);

            try
            {
                var sectionRange = FindSectionRange(sourceDoc, sourceHeading);
                if (sectionRange == null)
                    throw new InvalidOperationException($"源文件中未找到标题: {sourceHeading}");

                if (excludeSourceHeading)
                {
                    int headingEnd = FindHeadingEnd(sourceDoc, sourceHeading);
                    if (headingEnd > sectionRange.Start && headingEnd < sectionRange.End)
                        sectionRange = sourceDoc.Range(headingEnd, sectionRange.End);
                }

                int sectionLength = sectionRange.Text.Length;
                sectionRange.Copy();

                var insertRange = mainDoc.Range(insertPos, insertPos);
                insertRange.Paste();

                AddMergeComment(mainDoc, insertPos, sourceFilePath, sourceHeading);

                string extra = deletedChars > 0 ? $"（合入前已清空旧内容 {deletedChars} 字符）" : "";
                string skipNote = excludeSourceHeading ? "（已跳过源标题段落）" : "";
                return $"已将章节「{sourceHeading}」合入主文档" +
                       $"（章节字符数: {sectionLength}）。{skipNote}{extra}\n" +
                       $"源文件: {Path.GetFileName(sourceFilePath)}";
            }
            finally
            {
                object doNotSave = Word.WdSaveOptions.wdDoNotSaveChanges;
                sourceDoc.Close(ref doNotSave);
            }
        }

        private static void AddMergeComment(Word.Document doc, int insertPos,
            string sourceFilePath, string sourceHeading)
        {
            string fileName = Path.GetFileName(sourceFilePath);
            string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            string comment = $"[合入] {time}\n来源: {fileName}";
            if (!string.IsNullOrWhiteSpace(sourceHeading))
                comment += $"\n章节: {sourceHeading}";

            var anchorRange = doc.Range(insertPos, insertPos);
            anchorRange.Expand(Word.WdUnits.wdParagraph);
            doc.Comments.Add(anchorRange, comment);
        }

        // ═══════════════════════════════════════════════════════════════
        //  文档结构查找
        // ═══════════════════════════════════════════════════════════════

        private static int FindHeadingEnd(Word.Document doc, string headingName)
        {
            foreach (Word.Paragraph para in doc.Paragraphs)
            {
                int level = (int)para.OutlineLevel;
                if (level >= 1 && level <= 6 &&
                    para.Range.Text.Trim().Equals(headingName, StringComparison.OrdinalIgnoreCase))
                    return para.Range.End;
            }
            return -1;
        }

        private static int FindSectionEnd(Word.Document doc, string headingName, out int headingLevel)
        {
            headingLevel = -1;
            Word.Paragraph targetPara = null;

            foreach (Word.Paragraph para in doc.Paragraphs)
            {
                int level = (int)para.OutlineLevel;
                if (level >= 1 && level <= 6 &&
                    para.Range.Text.Trim().Equals(headingName, StringComparison.OrdinalIgnoreCase))
                {
                    targetPara = para;
                    headingLevel = level;
                    break;
                }
            }

            if (targetPara == null) return -1;

            bool passedTarget = false;
            foreach (Word.Paragraph para in doc.Paragraphs)
            {
                if (para.Range.Start == targetPara.Range.Start) { passedTarget = true; continue; }
                if (!passedTarget) continue;
                int level = (int)para.OutlineLevel;
                if (level >= 1 && level <= headingLevel)
                    return para.Range.Start;
            }

            return doc.Content.End - 1;
        }

        private static Word.Range FindSectionRange(Word.Document doc, string headingName)
        {
            Word.Paragraph targetPara = null;
            int targetLevel = -1;

            foreach (Word.Paragraph para in doc.Paragraphs)
            {
                int level = (int)para.OutlineLevel;
                if (level >= 1 && level <= 6 &&
                    para.Range.Text.Trim().Equals(headingName, StringComparison.OrdinalIgnoreCase))
                {
                    targetPara = para;
                    targetLevel = level;
                    break;
                }
            }

            if (targetPara == null) return null;

            int sectionStart = targetPara.Range.Start;
            int sectionEnd = doc.Content.End - 1;

            bool passedTarget = false;
            foreach (Word.Paragraph para in doc.Paragraphs)
            {
                if (para.Range.Start == sectionStart) { passedTarget = true; continue; }
                if (!passedTarget) continue;
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
