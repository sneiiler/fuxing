using System;
using System.Collections.Generic;
using NetOffice.WordApi;

namespace FuXing
{
    /// <summary>
    /// Word 文档结构操作的公共辅助类。
    /// 提取自 DeleteSectionTool / FormatContentTool / DocumentGraphTool
    /// 中重复出现的标题查找和章节范围计算逻辑。
    /// </summary>
    public static class DocumentHelper
    {
        // ═══════════════════════════════════════════════════════════════
        //  数据结构
        // ═══════════════════════════════════════════════════════════════

        /// <summary>表示文档中一个标题段落及其大纲级别</summary>
        public class HeadingInfo
        {
            public Paragraph Paragraph { get; set; }
            public int Level { get; set; }
            public string Text { get; set; }
        }

        // ═══════════════════════════════════════════════════════════════
        //  标题查找
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 在文档中按名称查找标题段落（不区分大小写）。
        /// 找不到时返回 null。
        /// </summary>
        public static HeadingInfo FindHeading(Document doc, string headingName)
        {
            foreach (Paragraph para in doc.Paragraphs)
            {
                int level = (int)para.OutlineLevel;
                if (level < 1 || level > 6) continue;

                string text = para.Range.Text.Trim();
                if (text.Equals(headingName, StringComparison.OrdinalIgnoreCase))
                {
                    return new HeadingInfo
                    {
                        Paragraph = para,
                        Level = level,
                        Text = text,
                    };
                }
            }
            return null;
        }

        /// <summary>
        /// 获取文档中所有标题段落（OutlineLevel 1-6）。
        /// </summary>
        public static List<HeadingInfo> GetAllHeadings(Document doc)
        {
            var result = new List<HeadingInfo>();
            foreach (Paragraph para in doc.Paragraphs)
            {
                int level = (int)para.OutlineLevel;
                if (level < 1 || level > 6) continue;

                result.Add(new HeadingInfo
                {
                    Paragraph = para,
                    Level = level,
                    Text = para.Range.Text.Trim(),
                });
            }
            return result;
        }

        // ═══════════════════════════════════════════════════════════════
        //  章节范围计算
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 计算某标题的章节结束位置（即下一个同级或更高级标题的起始位置）。
        /// 如果找不到后续同级标题，返回文档内容末尾位置。
        /// </summary>
        public static int FindSectionEnd(Document doc, Paragraph targetPara, int targetLevel)
        {
            bool passedTarget = false;
            foreach (Paragraph para in doc.Paragraphs)
            {
                if (para.Range.Start == targetPara.Range.Start)
                {
                    passedTarget = true;
                    continue;
                }

                if (!passedTarget) continue;

                int level = (int)para.OutlineLevel;
                if (level >= 1 && level <= targetLevel)
                    return para.Range.Start;
            }

            // Content.End 指向文档末尾之后，-1 避免越界
            return doc.Content.End - 1;
        }

        /// <summary>
        /// 获取指定标题的章节文本范围 [start, end)。
        /// includeHeading=true 时 start 为标题起始位置，否则为标题结束位置。
        /// </summary>
        public static (int Start, int End) GetSectionRange(
            Document doc, Paragraph targetPara, int targetLevel, bool includeHeading)
        {
            int start = includeHeading ? targetPara.Range.Start : targetPara.Range.End;
            int end = FindSectionEnd(doc, targetPara, targetLevel);
            return (start, end);
        }

        // ═══════════════════════════════════════════════════════════════
        //  外部文档操作
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 获取已打开的文档或以只读方式临时打开。
        /// 返回 (doc, shouldClose)：shouldClose=true 表示文档是本次临时打开的，调用方用完后应关闭。
        /// </summary>
        public static (Document Doc, bool ShouldClose) GetOrOpenReadOnly(Application app, string filePath)
        {
            string targetPath = System.IO.Path.GetFullPath(filePath).TrimEnd('\\');

            foreach (Document doc in app.Documents)
            {
                string openPath;
                try { openPath = System.IO.Path.GetFullPath(doc.FullName).TrimEnd('\\'); }
                catch { continue; }

                if (string.Equals(openPath, targetPath, StringComparison.OrdinalIgnoreCase))
                    return (doc, false);
            }

            var m = System.Type.Missing;
            var opened = app.Documents.Open(filePath, false, true, false, m, m, m, m, m, m, m, false);
            return (opened, true);
        }
    }
}
