using System;
using System.Collections.Generic;
using NetOffice.WordApi;

namespace FuXing
{
    /// <summary>
    /// Word 文档结构操作的公共辅助类。
    /// 提取自 DeleteSectionTool / NavigateToHeadingTool / MergeDocumentSectionTool / ReadSectionTextTool
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
        /// 以只读方式打开外部文档。调用方负责在不需要时关闭。
        /// </summary>
        public static Document OpenReadOnly(Application app, string filePath)
        {
            var m = System.Type.Missing;
            return app.Documents.Open(
                filePath, false, true, false, m, m, m, m, m, m, m, false);
        }
    }
}
