using NetOffice.WordApi;
using NetOffice.WordApi.Enums;
using System;
using System.Diagnostics;
using System.Text;

namespace FuXing
{
    /// <summary>
    /// 自动采集光标/选区上下文，用于在发送给 LLM 前注入到用户消息中。
    /// 让 LLM 在第一轮就拥有足够的位置感知能力，减少无效工具调用。
    /// </summary>
    public static class CursorContextProvider
    {
        /// <summary>
        /// 采集当前光标/选区上下文并构造结构化前缀。
        /// 返回 null 表示无法获取（无活动文档等），调用方应跳过注入。
        /// </summary>
        public static string BuildContextPrefix(Application app)
        {
            try
            {
                if (app == null) return null;
                var doc = app.ActiveDocument;
                if (doc == null) return null;

                var sel = app.Selection;
                if (sel == null) return null;

                // ── 有选中文本 → 附加选中内容 ──
                bool hasSelection = sel.Start != sel.End;
                if (hasSelection)
                    return BuildSelectionContext(sel);

                // ── 无选中 → 附加光标位置上下文 ──
                return BuildCursorContext(doc, sel);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CursorContextProvider] 采集失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>构造选中文本上下文前缀</summary>
        private static string BuildSelectionContext(Selection sel)
        {
            string text = sel.Text?.TrimEnd('\r', '\n');
            if (string.IsNullOrEmpty(text)) return null;
            return BuildSelectionContextFromText(text);
        }

        /// <summary>
        /// 从已知的选中文本构造上下文前缀（不需要 COM 访问）。
        /// 供 UI 层在用户显式附着选中文本时调用。
        /// </summary>
        public static string BuildSelectionContextFromText(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            // 截断过长的选中文本（避免 token 膨胀）
            const int maxLen = 2000;
            bool truncated = text.Length > maxLen;
            if (truncated)
                text = text.Substring(0, maxLen);

            var sb = new StringBuilder();
            sb.AppendLine($"[用户附加了选中文本作为上下文（{text.Length}字符{(truncated ? "，已截断" : "")}）]");
            sb.AppendLine(text);
            return sb.ToString();
        }

        /// <summary>构造光标位置上下文前缀（极简模式）</summary>
        private static string BuildCursorContext(Document doc, Selection sel)
        {
            int totalParas = doc.Paragraphs.Count;
            if (totalParas == 0) return null;

            // 找到光标所在段落的索引（1-based）
            var cursorPara = sel.Paragraphs[1];
            if (cursorPara == null) return null;

            int cursorStart = cursorPara.Range.Start;
            int cursorParaIndex = FindParagraphIndex(doc, cursorStart);
            if (cursorParaIndex < 1) return null;

            string cursorText = cursorPara.Range.Text?.TrimEnd('\r', '\n') ?? "";

            // 查找所属章节标题（向上找最近的标题段落）
            string sectionHeading = FindNearestHeading(doc, cursorParaIndex);

            var sb = new StringBuilder();
            sb.AppendLine("[光标位置上下文]");
            sb.AppendLine($"位置：第 {cursorParaIndex}/{totalParas} 段" +
                          (sectionHeading != null ? $"  所属章节：{sectionHeading}" : ""));

            // 光标段落内容
            if (string.IsNullOrWhiteSpace(cursorText))
                sb.AppendLine($"光标段落：（空段落）");
            else
                sb.AppendLine($"光标段落：{Truncate(cursorText, 300)}");

            // 前文（最多3段）
            var beforeText = GatherSurroundingParagraphs(doc, cursorParaIndex, totalParas, before: true, count: 3);
            if (!string.IsNullOrEmpty(beforeText))
            {
                sb.AppendLine("前文摘要：");
                sb.Append(beforeText);
            }

            // 后文（最多3段）
            var afterText = GatherSurroundingParagraphs(doc, cursorParaIndex, totalParas, before: false, count: 3);
            if (!string.IsNullOrEmpty(afterText))
            {
                sb.AppendLine("后文摘要：");
                sb.Append(afterText);
            }

            return sb.ToString();
        }

        /// <summary>采集光标前或后的段落摘要</summary>
        private static string GatherSurroundingParagraphs(
            Document doc, int cursorParaIndex, int totalParas, bool before, int count)
        {
            var sb = new StringBuilder();
            int collected = 0;

            if (before)
            {
                // 从光标前一段向上收集
                for (int i = cursorParaIndex - 1; i >= 1 && collected < count; i--)
                {
                    string text = GetParagraphText(doc, i);
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    // 前插到开头（保持正序）
                    sb.Insert(0, $"  （第{i}段）{Truncate(text, 150)}\n");
                    collected++;
                }
            }
            else
            {
                // 从光标后一段向下收集
                for (int i = cursorParaIndex + 1; i <= totalParas && collected < count; i++)
                {
                    string text = GetParagraphText(doc, i);
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    sb.AppendLine($"  （第{i}段）{Truncate(text, 150)}");
                    collected++;
                }
            }

            return sb.ToString();
        }

        /// <summary>向上查找最近的标题段落</summary>
        private static string FindNearestHeading(Document doc, int fromParaIndex)
        {
            for (int i = fromParaIndex; i >= 1; i--)
            {
                try
                {
                    var para = doc.Paragraphs[i];
                    int level = (int)para.OutlineLevel;
                    if (level >= 1 && level <= 6)
                    {
                        string text = para.Range.Text?.TrimEnd('\r', '\n');
                        if (!string.IsNullOrWhiteSpace(text))
                            return text;
                    }
                }
                catch { break; }
            }
            return null;
        }

        /// <summary>通过 Range.Start 对比找到段落索引</summary>
        private static int FindParagraphIndex(Document doc, int rangeStart)
        {
            int count = doc.Paragraphs.Count;
            for (int i = 1; i <= count; i++)
            {
                try
                {
                    if (doc.Paragraphs[i].Range.Start == rangeStart)
                        return i;
                    // 如果已经超过目标位置，提前退出
                    if (doc.Paragraphs[i].Range.Start > rangeStart)
                        return i - 1 > 0 ? i - 1 : 1;
                }
                catch { break; }
            }
            return -1;
        }

        /// <summary>安全获取指定段落的文本</summary>
        private static string GetParagraphText(Document doc, int index)
        {
            try
            {
                return doc.Paragraphs[index].Range.Text?.TrimEnd('\r', '\n');
            }
            catch
            {
                return null;
            }
        }

        /// <summary>截断文本到指定长度</summary>
        private static string Truncate(string text, int maxLen)
        {
            if (text == null) return "";
            if (text.Length <= maxLen) return text;
            return text.Substring(0, maxLen) + "…";
        }
    }
}
