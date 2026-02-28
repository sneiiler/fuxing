using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace FuXing.SubAgents
{
    // ═══════════════════════════════════════════════════════════════
    //  文档结构提取器
    //
    //  双模式设计：
    //  1. 快速模式（ExtractOutlineOnly）：只读取具有大纲级别的标题段落，
    //     跳过所有格式属性，用于高效构建文档 Map。
    //  2. 深度模式（Extract）：提取每个段落的格式元数据（字号、加粗、
    //     对齐等），供 LLM 推断标题层级，适用于未使用标题样式的文档。
    // ═══════════════════════════════════════════════════════════════

    /// <summary>快速模式提取的大纲标题项</summary>
    public class OutlineHeading
    {
        /// <summary>段落序号（1-based）</summary>
        public int ParaIndex { get; set; }

        /// <summary>大纲级别（1-9）</summary>
        public int Level { get; set; }

        /// <summary>标题文本</summary>
        public string Text { get; set; }

        /// <summary>段落在文档中的字符偏移位置</summary>
        public int StartPosition { get; set; }
    }

    /// <summary>快速模式大纲提取结果</summary>
    public class OutlineOnlyResult
    {
        /// <summary>文档名</summary>
        public string DocumentName { get; set; }

        /// <summary>总段落数</summary>
        public int TotalParagraphs { get; set; }

        /// <summary>检测到的大纲标题列表（按段落顺序排列）</summary>
        public List<OutlineHeading> Headings { get; set; } = new List<OutlineHeading>();
    }

    /// <summary>单个段落的结构描述</summary>
    public class ParagraphMeta
    {
        /// <summary>段落序号（1-based）</summary>
        public int Index { get; set; }

        /// <summary>所用样式名称（如 "标题 1"、"正文"）</summary>
        public string StyleName { get; set; }

        /// <summary>大纲级别（1-9 为标题级别，10 = 正文 / 未设置）</summary>
        public int OutlineLevel { get; set; }

        /// <summary>字号（pt），-1 表示段落内混合字号</summary>
        public float FontSize { get; set; }

        /// <summary>是否加粗（null = 段落内混合）</summary>
        public bool? Bold { get; set; }

        /// <summary>是否斜体（null = 段落内混合）</summary>
        public bool? Italic { get; set; }

        /// <summary>对齐方式：左对齐 / 居中 / 右对齐 / 两端对齐</summary>
        public string Alignment { get; set; }

        /// <summary>段落文本（截断到 MaxTextLength）</summary>
        public string Text { get; set; }

        /// <summary>段落在文档中的字符偏移位置</summary>
        public int StartPosition { get; set; }

        /// <summary>段落文本是否为空白</summary>
        public bool IsBlank { get; set; }

        /// <summary>是否为表格内段落</summary>
        public bool IsInTable { get; set; }

        /// <summary>是否为列表段落（有编号或项目符号）</summary>
        public bool IsListItem { get; set; }

        /// <summary>列表级别（0-based，-1 表示非列表）</summary>
        public int ListLevel { get; set; }

        /// <summary>
        /// 格式化为单行描述，例如：
        /// [段落 #3] [字体:14pt, 加粗, 左对齐] [样式:标题 1] 1. 研究背景
        /// </summary>
        public string ToDescriptionLine()
        {
            var tags = new List<string>();

            // 字体信息
            var fontParts = new List<string>();
            if (FontSize > 0) fontParts.Add($"{FontSize}pt");
            if (Bold == true) fontParts.Add("加粗");
            if (Italic == true) fontParts.Add("斜体");
            if (!string.IsNullOrEmpty(Alignment)) fontParts.Add(Alignment);
            if (fontParts.Count > 0)
                tags.Add($"字体:{string.Join(", ", fontParts)}");

            // 样式信息
            if (!string.IsNullOrEmpty(StyleName))
                tags.Add($"样式:{StyleName}");

            // 大纲级别（仅标题级别时显示）
            if (OutlineLevel >= 1 && OutlineLevel <= 9)
                tags.Add($"大纲:{OutlineLevel}级");

            // 列表
            if (IsListItem)
                tags.Add($"列表:L{ListLevel}");

            // 表格
            if (IsInTable)
                tags.Add("表格内");

            string tagStr = tags.Count > 0 ? $" [{string.Join("] [", tags)}]" : "";
            string textStr = IsBlank ? "(空行)" : Text;

            return $"[段落 #{Index}]{tagStr} {textStr}";
        }
    }

    /// <summary>文档结构提取结果</summary>
    public class DocumentStructure
    {
        /// <summary>文档名</summary>
        public string DocumentName { get; set; }

        /// <summary>总段落数</summary>
        public int TotalParagraphs { get; set; }

        /// <summary>所有段落的元数据列表</summary>
        public List<ParagraphMeta> Paragraphs { get; set; } = new List<ParagraphMeta>();

        /// <summary>文档中出现的所有字号（降序排列），用于推断层级</summary>
        public List<float> DistinctFontSizes { get; set; } = new List<float>();

        /// <summary>文档中出现的所有样式名称</summary>
        public List<string> DistinctStyles { get; set; } = new List<string>();

        /// <summary>
        /// 生成供 LLM 阅读的结构化文本表示。
        /// 包含统计摘要 + 完整段落列表。
        /// </summary>
        public string ToStructuredText()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"═══ 文档结构分析: {DocumentName} ═══");
            sb.AppendLine($"总段落数: {TotalParagraphs}");
            sb.AppendLine($"出现的字号: {string.Join(", ", DistinctFontSizes.Select(s => s + "pt"))}");
            sb.AppendLine($"出现的样式: {string.Join(", ", DistinctStyles)}");
            sb.AppendLine();
            sb.AppendLine("───── 段落列表 ─────");

            foreach (var p in Paragraphs)
                sb.AppendLine(p.ToDescriptionLine());

            return sb.ToString();
        }
    }

    /// <summary>
    /// 从 Word COM 对象提取文档的段落级结构元数据。
    /// 必须在 STA 线程（UI 线程）上调用。
    /// </summary>
    public static class DocumentStructureExtractor
    {
        /// <summary>段落文本截断长度</summary>
        private const int MaxTextLength = 120;

        /// <summary>标题文本截断长度（快速模式）</summary>
        private const int MaxHeadingTextLength = 200;

        // ═══════════════════════════════════════════════════════════════
        //  快速模式：仅提取大纲级别标题
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 快速提取：只遍历段落的 OutlineLevel，
        /// 仅采集具有大纲级别（1-9）的标题段落的文本和位置。
        /// 跳过所有正文段落的格式属性（字号、加粗、对齐等），
        /// 最大限度减少 COM 调用开销。
        /// </summary>
        public static OutlineOnlyResult ExtractOutlineOnly(NetOffice.WordApi.Document doc)
        {
            var sw = Stopwatch.StartNew();

            var result = new OutlineOnlyResult
            {
                DocumentName = doc.Name,
                TotalParagraphs = doc.Paragraphs.Count
            };

            int index = 0;

            foreach (NetOffice.WordApi.Paragraph para in doc.Paragraphs)
            {
                index++;

                // 仅读取大纲级别 — 这是每段最低开销的 COM 属性
                int outlineLevel = 10;
                try { outlineLevel = (int)para.OutlineLevel; }
                catch { continue; }

                // 非标题段落直接跳过，不读取任何其他属性
                if (outlineLevel < 1 || outlineLevel > 9)
                    continue;

                // 标题段落：读取文本和位置
                var range = para.Range;
                string text = range.Text?.TrimEnd('\r', '\n', '\a') ?? "";

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                result.Headings.Add(new OutlineHeading
                {
                    ParaIndex = index,
                    Level = outlineLevel,
                    Text = text.Length > MaxHeadingTextLength
                        ? text.Substring(0, MaxHeadingTextLength) + "…"
                        : text,
                    StartPosition = range.Start
                });
            }

            sw.Stop();
            Debug.WriteLine($"[Extractor] 快速模式完成: {result.Headings.Count} 个标题, " +
                            $"{result.TotalParagraphs} 总段落, 耗时 {sw.ElapsedMilliseconds}ms");

            return result;
        }

        // ═══════════════════════════════════════════════════════════════
        //  深度模式：提取全量段落格式元数据（供 LLM 推断标题）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 深度提取：遍历所有段落，采集字号、加粗、对齐、样式等格式元数据。
        /// 用于文档未使用标准标题样式时，提供给 LLM 推断标题层级。
        /// </summary>
        public static DocumentStructure Extract(NetOffice.WordApi.Document doc)
        {
            var sw = Stopwatch.StartNew();

            var result = new DocumentStructure
            {
                DocumentName = doc.Name,
                TotalParagraphs = doc.Paragraphs.Count
            };

            var fontSizes = new HashSet<float>();
            var styles = new HashSet<string>();
            int index = 0;

            foreach (NetOffice.WordApi.Paragraph para in doc.Paragraphs)
            {
                index++;
                var range = para.Range;
                var font = range.Font;

                string text = range.Text?.TrimEnd('\r', '\n', '\a') ?? "";
                bool isBlank = string.IsNullOrWhiteSpace(text);

                // 字号：Range.Font.Size 在多字号时返回 9999999
                float fontSize = -1;
                try
                {
                    float rawSize = font.Size;
                    fontSize = (rawSize > 0 && rawSize < 1000) ? rawSize : -1;
                }
                catch { }

                // 加粗：-1 = true, 0 = false, 9999999 = 混合
                bool? bold = null;
                try
                {
                    int rawBold = font.Bold;
                    if (rawBold == -1) bold = true;
                    else if (rawBold == 0) bold = false;
                }
                catch { }

                // 斜体
                bool? italic = null;
                try
                {
                    int rawItalic = font.Italic;
                    if (rawItalic == -1) italic = true;
                    else if (rawItalic == 0) italic = false;
                }
                catch { }

                // 对齐方式
                string alignment = "左对齐";
                try
                {
                    switch (para.Alignment)
                    {
                        case NetOffice.WordApi.Enums.WdParagraphAlignment.wdAlignParagraphCenter:
                            alignment = "居中"; break;
                        case NetOffice.WordApi.Enums.WdParagraphAlignment.wdAlignParagraphRight:
                            alignment = "右对齐"; break;
                        case NetOffice.WordApi.Enums.WdParagraphAlignment.wdAlignParagraphJustify:
                            alignment = "两端对齐"; break;
                        default:
                            alignment = "左对齐"; break;
                    }
                }
                catch { }

                // 样式名
                string styleName = "";
                try { styleName = ((dynamic)para.Style).NameLocal?.ToString() ?? ""; }
                catch { }

                // 大纲级别
                int outlineLevel = 10;
                try { outlineLevel = (int)para.OutlineLevel; }
                catch { }

                // 列表
                bool isListItem = false;
                int listLevel = -1;
                try
                {
                    var listFormat = para.Range.ListFormat;
                    if (listFormat.ListType != NetOffice.WordApi.Enums.WdListType.wdListNoNumbering)
                    {
                        isListItem = true;
                        listLevel = listFormat.ListLevelNumber - 1;
                    }
                }
                catch { }

                // 是否在表格内
                bool isInTable = false;
                try
                {
                    var info = range.get_Information(NetOffice.WordApi.Enums.WdInformation.wdWithInTable);
                    isInTable = info is bool b && b;
                }
                catch { }

                if (fontSize > 0) fontSizes.Add(fontSize);
                if (!string.IsNullOrEmpty(styleName)) styles.Add(styleName);

                result.Paragraphs.Add(new ParagraphMeta
                {
                    Index = index,
                    StyleName = styleName,
                    OutlineLevel = outlineLevel,
                    FontSize = fontSize,
                    Bold = bold,
                    Italic = italic,
                    Alignment = alignment,
                    Text = isBlank ? "" : Truncate(text, MaxTextLength),
                    StartPosition = range.Start,
                    IsBlank = isBlank,
                    IsInTable = isInTable,
                    IsListItem = isListItem,
                    ListLevel = listLevel
                });
            }

            result.DistinctFontSizes = fontSizes.OrderByDescending(s => s).ToList();
            result.DistinctStyles = styles.OrderBy(s => s).ToList();

            sw.Stop();
            Debug.WriteLine($"[Extractor] 深度模式完成: {result.TotalParagraphs} 段落, 耗时 {sw.ElapsedMilliseconds}ms");

            return result;
        }

        private static string Truncate(string text, int maxLen)
        {
            if (text.Length <= maxLen) return text;
            return text.Substring(0, maxLen) + "…";
        }
    }
}
