using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using FuXingAgent.Agents;
using FuXingAgent.Core;
using Word = Microsoft.Office.Interop.Word;

namespace FuXingAgent.Workflows
{
    public class MergeDocumentWorkflow
    {
        private const string WorkflowName = "merge_document_workflow";
        private const string WorkflowDisplayName = "Document Merge Workflow";
        private const int TotalSteps = 3;

        private readonly Connect _connect;

        public MergeDocumentWorkflow(Connect connect) => _connect = connect;

        [Description("Merge external document content into a target heading section using a fixed workflow: 1) validate source file and target heading exist, 2) execute merge in Track Changes mode, 3) generate verification report. Can merge entire file or specific source section.")]
        public string merge_document_workflow(
            [Description("Source file path")] string source_file_path,
            [Description("Target heading in main document")] string target_heading,
            [Description("Optional source heading")] string source_heading = null,
            [Description("Whether to clear existing target content first")] bool replace_existing = true,
            [Description("Whether to skip the source heading paragraph itself")] bool exclude_source_heading = true)
        {
            WorkflowProgressReporter.StartWorkflow(WorkflowName, WorkflowDisplayName, TotalSteps);

            try
            {
                // 全部是 COM 操作，整体 marshal 到 STA 线程
                return StaHelper.RunOnSta(() =>
                {
                    var app = _connect.WordApplication;
                    var mainDoc = app.ActiveDocument ?? throw new InvalidOperationException("No active document");

                    WorkflowProgressReporter.StartStep(WorkflowName, 1, TotalSteps, "Validate Inputs", "Check source file and target heading.");
                    var validation = ValidateInputs(app, mainDoc, source_file_path, target_heading, source_heading);
                    DebugLogger.Instance.LogInfo($"[MergeDocumentWorkflow] Step1 验证通过: {validation}");
                    WorkflowProgressReporter.FinishStep(WorkflowName, 1, TotalSteps, "Validate Inputs", true, validation);

                    WorkflowProgressReporter.StartStep(WorkflowName, 2, TotalSteps, "Execute Merge", "Merge content in track changes mode.");
                    int beforeCharCount = mainDoc.Content.End;
                    string mergeResult = ExecuteMerge(app, mainDoc, source_file_path, target_heading,
                        source_heading, replace_existing, exclude_source_heading);
                    int afterCharCount = mainDoc.Content.End;
                    DebugLogger.Instance.LogInfo("[MergeDocumentWorkflow] Step2 合并完成");
                    WorkflowProgressReporter.FinishStep(WorkflowName, 2, TotalSteps, "Execute Merge", true, $"Chars: {beforeCharCount} -> {afterCharCount}");

                    WorkflowProgressReporter.StartStep(WorkflowName, 3, TotalSteps, "Build Report", "Generate final merge report.");
                    string report = BuildReport(mainDoc, source_file_path, target_heading, source_heading,
                        replace_existing, beforeCharCount, afterCharCount, mergeResult);
                    DebugLogger.Instance.LogInfo("[MergeDocumentWorkflow] Step3 报告生成");
                    WorkflowProgressReporter.FinishStep(WorkflowName, 3, TotalSteps, "Build Report", true, "Merge report created.");
                    WorkflowProgressReporter.FinishWorkflow(WorkflowName, WorkflowDisplayName, TotalSteps, true, "Document merge completed.");

                    return report;
                });
            }
            catch (Exception ex)
            {
                WorkflowProgressReporter.FinishWorkflow(WorkflowName, WorkflowDisplayName, TotalSteps, false, ex.Message);
                throw;
            }
        }

        private static string ValidateInputs(
            Word.Application app, Word.Document mainDoc,
            string sourceFilePath, string targetHeading, string sourceHeading)
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath))
                throw new ArgumentException("Missing source_file_path");

            if (!File.Exists(sourceFilePath))
                throw new InvalidOperationException($"Source file not found: {sourceFilePath}");

            string ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();
            if (ext != ".docx" && ext != ".doc")
                throw new InvalidOperationException($"Unsupported file type: {ext}");

            if (string.IsNullOrWhiteSpace(targetHeading))
                throw new ArgumentException("Missing target_heading");

            int targetLevel;
            int sectionEnd = FindSectionEnd(mainDoc, targetHeading, out targetLevel);
            if (sectionEnd < 0)
                throw new InvalidOperationException($"Target heading not found: {targetHeading}");

            if (!string.IsNullOrWhiteSpace(sourceHeading))
                ValidateSourceHeading(app, sourceFilePath, sourceHeading);

            return $"Source={Path.GetFileName(sourceFilePath)}, Target={targetHeading}(L{targetLevel})";
        }

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
                    throw new InvalidOperationException($"Source heading not found: {sourceHeading}");
            }
            finally
            {
                object doNotSave = Word.WdSaveOptions.wdDoNotSaveChanges;
                sourceDoc.Close(ref doNotSave);
            }
        }

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

        private static string BuildReport(
            Word.Document mainDoc, string sourceFilePath, string targetHeading,
            string sourceHeading, bool replaceExisting,
            int beforeCharCount, int afterCharCount, string mergeResult)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Document Merge Workflow Report ===");
            sb.AppendLine($"Main document: {mainDoc.Name}");
            sb.AppendLine($"Source file: {Path.GetFileName(sourceFilePath)}");
            sb.AppendLine($"Target section: {targetHeading}");
            if (!string.IsNullOrWhiteSpace(sourceHeading))
                sb.AppendLine($"Source section: {sourceHeading}");
            sb.AppendLine($"Replace existing: {(replaceExisting ? "yes" : "no")}");
            sb.AppendLine($"Character delta: {beforeCharCount} -> {afterCharCount} ({afterCharCount - beforeCharCount})");
            sb.AppendLine();
            sb.AppendLine(mergeResult);
            return sb.ToString();
        }

        private static string MergeWholeFile(Word.Document mainDoc, int insertPos,
            string sourceFilePath, int deletedChars)
        {
            var insertRange = mainDoc.Range(insertPos, insertPos);
            int beforeEnd = mainDoc.Content.End;

            insertRange.InsertFile(sourceFilePath);

            int insertedChars = mainDoc.Content.End - beforeEnd;
            AddMergeComment(mainDoc, insertPos, sourceFilePath, null);

            string extra = deletedChars > 0 ? $" (cleared {deletedChars} chars first)" : string.Empty;
            return $"Merged the entire file (inserted {insertedChars} chars).{extra}\nSource: {Path.GetFileName(sourceFilePath)}";
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
                    throw new InvalidOperationException($"Source heading not found: {sourceHeading}");

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

                string extra = deletedChars > 0 ? $" (cleared {deletedChars} chars first)" : string.Empty;
                string skipNote = excludeSourceHeading ? " (source heading skipped)" : string.Empty;
                return $"Merged section '{sourceHeading}' ({sectionLength} chars).{skipNote}{extra}\nSource: {Path.GetFileName(sourceFilePath)}";
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
            string comment = $"[Merge] {time}\nSource: {fileName}";
            if (!string.IsNullOrWhiteSpace(sourceHeading))
                comment += $"\nSection: {sourceHeading}";

            var anchorRange = doc.Range(insertPos, insertPos);
            anchorRange.Expand(Word.WdUnits.wdParagraph);
            doc.Comments.Add(anchorRange, comment);
        }

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
