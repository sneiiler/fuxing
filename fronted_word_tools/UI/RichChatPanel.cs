using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using AntdUI;

namespace FuXing.UI
{
    // ════════════════════════════════════════════════════════════════
    //  枚举
    // ════════════════════════════════════════════════════════════════

    public enum ChatRole { User, AI }

    public enum ToolCallStatus
    {
        Running,    // ⏳ 进行中
        Success,    // ✅ 完成
        Error       // ❌ 失败
    }

    // ════════════════════════════════════════════════════════════════
    //  内容块接口
    // ════════════════════════════════════════════════════════════════

    internal interface IContentBlock
    {
        int MeasureHeight(Graphics g, int width);
        void Paint(Graphics g, Rectangle bounds, bool isUser);
        bool HitTest(Point pt, Rectangle bounds);
        void OnClick(Point pt, Rectangle bounds);
    }

    // ════════════════════════════════════════════════════════════════
    //  文本块 — 支持基本 Markdown 渲染
    // ════════════════════════════════════════════════════════════════

    internal class TextBlock : IContentBlock
    {
        public string Text { get; set; }
        private readonly ChatRole _role;

        private List<TextSegment> _cachedSegments;
        private string _cachedSegmentsText;

        private static readonly Font NormalFont = new Font("Microsoft YaHei UI", 9.5F);
        private static readonly Font BoldFont = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
        private static readonly Font H1Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
        private static readonly Font H2Font = new Font("Microsoft YaHei UI", 11.5F, FontStyle.Bold);
        private static readonly Font H3Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);

        public TextBlock(string text, ChatRole role)
        {
            Text = text;
            _role = role;
        }

        private List<TextSegment> GetSegments()
        {
            if (_cachedSegments == null || _cachedSegmentsText != Text)
            {
                _cachedSegments = ParseSegments(Text);
                _cachedSegmentsText = Text;
            }
            return _cachedSegments;
        }

        public int MeasureHeight(Graphics g, int width)
        {
            if (string.IsNullOrEmpty(Text)) return 0;
            int totalH = 0;
            var canvas = g.High();
            foreach (var seg in GetSegments())
            {
                var size = canvas.MeasureText(seg.Text, seg.Font, width, FormatFlags.Top | FormatFlags.Left);
                totalH += size.Height + seg.ExtraSpacing;
            }
            return Math.Max(18, totalH);
        }

        public void Paint(Graphics g, Rectangle bounds, bool isUser)
        {
            if (string.IsNullOrEmpty(Text)) return;
            var fgColor = isUser ? Color.White : Color.FromArgb(31, 41, 55);
            int y = bounds.Y;
            var canvas = g.High();
            foreach (var seg in GetSegments())
            {
                var color = seg.IsSecondary ? Color.FromArgb(107, 114, 128) : fgColor;
                var size = canvas.MeasureText(seg.Text, seg.Font, bounds.Width, FormatFlags.Top | FormatFlags.Left);
                canvas.DrawText(seg.Text, seg.Font, color,
                    new Rectangle(bounds.X, y, bounds.Width, size.Height),
                    FormatFlags.Top | FormatFlags.Left);
                y += size.Height + seg.ExtraSpacing;
            }
        }

        public bool HitTest(Point pt, Rectangle bounds) => false;
        public void OnClick(Point pt, Rectangle bounds) { }

        // ── Markdown 解析 ──

        private struct TextSegment
        {
            public string Text;
            public Font Font;
            public int ExtraSpacing;
            public bool IsSecondary;
        }

        private List<TextSegment> ParseSegments(string md)
        {
            var segments = new List<TextSegment>();
            if (string.IsNullOrEmpty(md)) return segments;

            foreach (var rawLine in md.Split('\n'))
            {
                string line = rawLine.TrimEnd('\r');
                Font font = NormalFont;
                int extraSpacing = 0;
                bool secondary = false;

                // 标题
                if (line.StartsWith("### "))
                {
                    line = line.Substring(4); font = H3Font; extraSpacing = 2;
                }
                else if (line.StartsWith("## "))
                {
                    line = line.Substring(3); font = H2Font; extraSpacing = 4;
                }
                else if (line.StartsWith("# "))
                {
                    line = line.Substring(2); font = H1Font; extraSpacing = 6;
                }
                // 整行加粗
                else if (line.StartsWith("**") && line.EndsWith("**") && line.Length > 4)
                {
                    line = line.Substring(2, line.Length - 4); font = BoldFont;
                }

                // 列表符号
                if (Regex.IsMatch(line, @"^[-*+]\s+"))
                    line = "  •  " + line.Substring(Regex.Match(line, @"^[-*+]\s+").Length);
                // 有序列表
                var olMatch = Regex.Match(line, @"^(\d+)\.\s+");
                if (olMatch.Success)
                    line = "  " + olMatch.Groups[1].Value + ".  " + line.Substring(olMatch.Length);

                // 清理行内 markdown
                line = Regex.Replace(line, @"\*\*(.+?)\*\*", "$1");
                line = Regex.Replace(line, @"\*(.+?)\*", "$1");
                line = Regex.Replace(line, @"`(.+?)`", "$1");
                line = Regex.Replace(line, @"\[([^\]]+)\]\([^\)]+\)", "$1");

                // 空行添加间距
                if (string.IsNullOrWhiteSpace(line))
                {
                    segments.Add(new TextSegment { Text = " ", Font = NormalFont, ExtraSpacing = 2 });
                    continue;
                }

                segments.Add(new TextSegment { Text = line, Font = font, ExtraSpacing = extraSpacing, IsSecondary = secondary });
            }
            return segments;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  引用块 — 渲染 Markdown > 引用
    // ════════════════════════════════════════════════════════════════

    internal class BlockquoteBlock : IContentBlock
    {
        public string Text { get; set; }
        private readonly ChatRole _role;

        private static readonly Font QuoteFont = new Font("Microsoft YaHei UI", 9.5F);
        private static readonly Color QuoteBorderColor = Color.FromArgb(59, 130, 246);   // 蓝色左竖线
        private static readonly Color QuoteBgColor = Color.FromArgb(239, 246, 255);       // 淡蓝背景
        private static readonly Color QuoteFgColor = Color.FromArgb(55, 65, 81);          // 深灰文字
        private const int LeftBarWidth = 3;
        private const int PadLeft = 14;  // 左竖线 + 内间距
        private const int PadVert = 8;
        private const int Radius = 6;

        public BlockquoteBlock(string text, ChatRole role)
        {
            Text = text;
            _role = role;
        }

        public int MeasureHeight(Graphics g, int width)
        {
            if (string.IsNullOrEmpty(Text)) return 0;
            var canvas = g.High();
            var size = canvas.MeasureText(Text, QuoteFont, width - PadLeft - 10, FormatFlags.Top | FormatFlags.Left);
            return size.Height + PadVert * 2;
        }

        public void Paint(Graphics g, Rectangle bounds, bool isUser)
        {
            if (string.IsNullOrEmpty(Text)) return;

            // 圆角背景
            using (var path = RoundedRect(bounds, Radius))
            using (var brush = new SolidBrush(QuoteBgColor))
                g.FillPath(brush, path);

            // 左侧蓝色竖线
            using (var pen = new Pen(QuoteBorderColor, LeftBarWidth))
                g.DrawLine(pen, bounds.X + LeftBarWidth / 2 + 1, bounds.Y + 4,
                               bounds.X + LeftBarWidth / 2 + 1, bounds.Bottom - 4);

            // 文字
            var canvas = g.High();
            var textRect = new Rectangle(bounds.X + PadLeft, bounds.Y + PadVert,
                                         bounds.Width - PadLeft - 10, bounds.Height - PadVert * 2);
            canvas.DrawText(Text, QuoteFont, QuoteFgColor, textRect,
                FormatFlags.Top | FormatFlags.Left);
        }

        public bool HitTest(Point pt, Rectangle bounds) => false;
        public void OnClick(Point pt, Rectangle bounds) { }

        private GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            if (r.Width < 1 || r.Height < 1) { p.AddRectangle(r); return p; }
            radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
            if (radius < 1) { p.AddRectangle(r); return p; }
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  围栏代码块 — 渲染 Markdown ``` 代码块
    // ════════════════════════════════════════════════════════════════

    internal class FencedCodeBlock : IContentBlock
    {
        public string Code { get; set; }
        public string Language { get; set; }

        private static readonly Font CodeFont = new Font("Consolas", 9F);
        private static readonly Font LangFont = new Font("Microsoft YaHei UI", 7.5F);
        private static readonly Color CodeBg = Color.FromArgb(245, 243, 235);
        private static readonly Color CodeBorder = Color.FromArgb(230, 218, 185);
        private static readonly Color CodeFg = Color.FromArgb(55, 65, 81);
        private static readonly Color LangFg = Color.FromArgb(120, 80, 20);
        private const int PadX = 12;
        private const int PadY = 10;
        private const int LangH = 20;
        private const int Radius = 6;

        public FencedCodeBlock(string code, string language)
        {
            Code = code;
            Language = language;
        }

        public int MeasureHeight(Graphics g, int width)
        {
            if (string.IsNullOrEmpty(Code)) return 0;
            var canvas = g.High();
            int langExtra = string.IsNullOrEmpty(Language) ? 0 : LangH;
            var size = canvas.MeasureText(Code, CodeFont, width - PadX * 2, FormatFlags.Top | FormatFlags.Left);
            return size.Height + PadY * 2 + langExtra;
        }

        public void Paint(Graphics g, Rectangle bounds, bool isUser)
        {
            if (string.IsNullOrEmpty(Code)) return;
            var canvas = g.High();
            int langExtra = string.IsNullOrEmpty(Language) ? 0 : LangH;

            // 圆角背景
            using (var path = RoundedRect(bounds, Radius))
            {
                using (var brush = new SolidBrush(CodeBg))
                    g.FillPath(brush, path);
                using (var pen = new Pen(CodeBorder))
                    g.DrawPath(pen, path);
            }

            // 语言标签
            if (!string.IsNullOrEmpty(Language))
            {
                canvas.DrawText(Language, LangFont, LangFg,
                    new Rectangle(bounds.X + PadX, bounds.Y + 4, bounds.Width - PadX * 2, 16),
                    FormatFlags.Top | FormatFlags.Left);
            }

            // 代码文本
            canvas.DrawText(Code, CodeFont, CodeFg,
                new Rectangle(bounds.X + PadX, bounds.Y + PadY + langExtra,
                              bounds.Width - PadX * 2, bounds.Height - PadY * 2 - langExtra),
                FormatFlags.Top | FormatFlags.Left);
        }

        public bool HitTest(Point pt, Rectangle bounds) => false;
        public void OnClick(Point pt, Rectangle bounds) { }

        private GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            if (r.Width < 1 || r.Height < 1) { p.AddRectangle(r); return p; }
            radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
            if (radius < 1) { p.AddRectangle(r); return p; }
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  表格块 — 渲染 Markdown 表格
    // ════════════════════════════════════════════════════════════════

    internal class TableBlock : IContentBlock
    {
        private readonly string[][] _rows; // 第一行为表头
        private readonly string _rawText;  // 原始文本，用于复制
        private readonly ChatRole _role;

        private static readonly Font HeaderFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        private static readonly Font CellFont = new Font("Microsoft YaHei UI", 9F);
        private const int CellPadH = 8;
        private const int CellPadV = 5;
        private static readonly Color HeaderBg = Color.FromArgb(243, 244, 246);
        private static readonly Color GridColor = Color.FromArgb(220, 223, 228);
        private static readonly Color AltRowBg = Color.FromArgb(249, 250, 251);

        public TableBlock(List<string> tableLines, ChatRole role)
        {
            _role = role;
            _rawText = string.Join("\n", tableLines);
            var rows = new List<string[]>();
            foreach (var line in tableLines)
            {
                var trimmed = line.Trim();
                // 跳过分隔行 |---|---|
                if (Regex.IsMatch(trimmed, @"^\|[\s\-:\|]+\|$"))
                    continue;
                var cells = ParseRow(trimmed);
                if (cells.Length > 0)
                    rows.Add(cells);
            }
            _rows = rows.ToArray();
        }

        public string GetPlainText() => _rawText;

        private static string[] ParseRow(string line)
        {
            line = line.Trim();
            if (line.StartsWith("|")) line = line.Substring(1);
            if (line.EndsWith("|")) line = line.Substring(0, line.Length - 1);
            return line.Split('|').Select(c => c.Trim()).ToArray();
        }

        private int ColumnCount => _rows.Length > 0 ? _rows.Max(r => r.Length) : 0;

        private int[] CalcColumnWidths(Graphics g, int availableWidth)
        {
            int cols = ColumnCount;
            if (cols == 0) return new int[0];
            var canvas = g.High();
            var idealWidths = new int[cols];
            for (int r = 0; r < _rows.Length; r++)
            {
                var font = (r == 0) ? HeaderFont : CellFont;
                for (int c = 0; c < cols && c < _rows[r].Length; c++)
                {
                    var size = canvas.MeasureText(_rows[r][c], font, 9999);
                    idealWidths[c] = Math.Max(idealWidths[c], size.Width + CellPadH * 2);
                }
            }
            for (int c = 0; c < cols; c++)
                idealWidths[c] = Math.Max(idealWidths[c], 40);

            int totalIdeal = idealWidths.Sum();
            if (totalIdeal <= availableWidth)
                return idealWidths;

            // 超宽时按比例缩放
            var widths = new int[cols];
            for (int c = 0; c < cols; c++)
                widths[c] = Math.Max(30, (int)(idealWidths[c] * (double)availableWidth / totalIdeal));
            return widths;
        }

        private int RowHeight(Graphics g, int[] colWidths, int rowIdx)
        {
            if (rowIdx >= _rows.Length) return 0;
            var canvas = g.High();
            var row = _rows[rowIdx];
            int maxH = 0;
            for (int c = 0; c < colWidths.Length && c < row.Length; c++)
            {
                var font = (rowIdx == 0) ? HeaderFont : CellFont;
                int textW = Math.Max(10, colWidths[c] - CellPadH * 2);
                var size = canvas.MeasureText(row[c], font, textW, FormatFlags.Top | FormatFlags.Left);
                maxH = Math.Max(maxH, size.Height);
            }
            return maxH + CellPadV * 2;
        }

        public int MeasureHeight(Graphics g, int width)
        {
            if (_rows.Length == 0) return 0;
            var colWidths = CalcColumnWidths(g, width);
            int totalH = 0;
            for (int r = 0; r < _rows.Length; r++)
                totalH += RowHeight(g, colWidths, r);
            return totalH + 1;
        }

        public void Paint(Graphics g, Rectangle bounds, bool isUser)
        {
            if (_rows.Length == 0) return;
            var colWidths = CalcColumnWidths(g, bounds.Width);
            var canvas = g.High();
            var fgColor = isUser ? Color.White : Color.FromArgb(31, 41, 55);
            int tableWidth = colWidths.Sum();

            using (var gridPen = new Pen(isUser ? Color.FromArgb(80, 255, 255, 255) : GridColor))
            {
                var headerBg = isUser ? Color.FromArgb(30, 255, 255, 255) : HeaderBg;
                var altBg = isUser ? Color.FromArgb(15, 255, 255, 255) : AltRowBg;

                int y = bounds.Y;
                for (int r = 0; r < _rows.Length; r++)
                {
                    int rowH = RowHeight(g, colWidths, r);
                    int x = bounds.X;

                    // 行背景
                    if (r == 0)
                        using (var brush = new SolidBrush(headerBg))
                            g.FillRectangle(brush, x, y, tableWidth, rowH);
                    else if (r % 2 == 0)
                        using (var brush = new SolidBrush(altBg))
                            g.FillRectangle(brush, x, y, tableWidth, rowH);

                    // 绘制单元格文本
                    var row = _rows[r];
                    for (int c = 0; c < colWidths.Length; c++)
                    {
                        int textW = Math.Max(10, colWidths[c] - CellPadH * 2);
                        var cellRect = new Rectangle(x + CellPadH, y + CellPadV, textW, rowH - CellPadV * 2);
                        var font = (r == 0) ? HeaderFont : CellFont;
                        string text = (c < row.Length) ? row[c] : "";
                        canvas.DrawText(text, font, fgColor, cellRect,
                            FormatFlags.VerticalCenter | FormatFlags.Left);
                        // 右侧竖线
                        g.DrawLine(gridPen, x + colWidths[c], y, x + colWidths[c], y + rowH);
                        x += colWidths[c];
                    }

                    // 左侧竖线
                    g.DrawLine(gridPen, bounds.X, y, bounds.X, y + rowH);
                    // 行底部横线
                    g.DrawLine(gridPen, bounds.X, y + rowH, bounds.X + tableWidth, y + rowH);
                    // 首行顶部横线
                    if (r == 0)
                        g.DrawLine(gridPen, bounds.X, y, bounds.X + tableWidth, y);

                    y += rowH;
                }
            }
        }

        public bool HitTest(Point pt, Rectangle bounds) => false;
        public void OnClick(Point pt, Rectangle bounds) { }
    }

    // ════════════════════════════════════════════════════════════════
    //  思考块 — 可折叠展示 AI 推理过程
    // ════════════════════════════════════════════════════════════════

    public class ThinkBlock : IContentBlock
    {
        public string Text { get; set; }
        public bool IsExpanded { get; set; }
        internal event Action ToggleRequested;

        private const int HeaderHeight = 28;
        private static readonly Font HeaderFont = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
        private static readonly Font ContentFont = new Font("Microsoft YaHei UI", 8.5F);
        private static readonly Color HeaderBg = Color.FromArgb(243, 244, 246);
        private static readonly Color HeaderFg = Color.FromArgb(107, 114, 128);
        private static readonly Color ContentFg = Color.FromArgb(75, 85, 99);
        private static readonly Color BorderColor = Color.FromArgb(229, 231, 235);

        public ThinkBlock(string text)
        {
            Text = text;
            IsExpanded = false;
        }

        public int MeasureHeight(Graphics g, int width)
        {
            if (!IsExpanded) return HeaderHeight;
            var canvas = g.High();
            var size = canvas.MeasureText(Text ?? "", ContentFont, width - 20, FormatFlags.Top | FormatFlags.Left);
            return HeaderHeight + size.Height + 16;
        }

        public void Paint(Graphics g, Rectangle bounds, bool isUser)
        {
            // 圆角背景
            using (var path = RoundedRect(bounds, 8))
            {
                using (var brush = new SolidBrush(HeaderBg))
                    g.FillPath(brush, path);
                using (var pen = new Pen(BorderColor))
                    g.DrawPath(pen, path);
            }

            // 左侧装饰线
            using (var pen = new Pen(Color.FromArgb(156, 163, 175), 2))
                g.DrawLine(pen, bounds.X + 4, bounds.Y + 8, bounds.X + 4, bounds.Bottom - 8);

            var canvas = g.High();
            {
                // 头部: 💭 emoji + 文字 + 展开/折叠指示
                string chevron = IsExpanded ? "▾" : "▸";
                string headerText = $"{chevron}  💭 思考过程";
                canvas.DrawText(headerText, HeaderFont, HeaderFg,
                    new Rectangle(bounds.X + 12, bounds.Y, bounds.Width - 24, HeaderHeight),
                    FormatFlags.VerticalCenter | FormatFlags.Left);

                // 折叠时显示省略提示
                if (!IsExpanded)
                {
                    using (var hintFont = new Font("Microsoft YaHei UI", 7.5F))
                        canvas.DrawText("点击展开", hintFont, Color.FromArgb(156, 163, 175),
                            new Rectangle(bounds.X + 12, bounds.Y, bounds.Width - 24, HeaderHeight),
                            FormatFlags.VerticalCenter | FormatFlags.Right);
                }

                // 展开时显示内容
                if (IsExpanded && !string.IsNullOrEmpty(Text))
                {
                    // 分隔线
                    int sepY = bounds.Y + HeaderHeight;
                    using (var pen = new Pen(BorderColor))
                        g.DrawLine(pen, bounds.X + 12, sepY, bounds.Right - 12, sepY);

                    var contentRect = new Rectangle(bounds.X + 12, sepY + 4, bounds.Width - 24, bounds.Bottom - sepY - 8);
                    canvas.DrawText(Text, ContentFont, ContentFg, contentRect,
                        FormatFlags.Top | FormatFlags.Left);
                }
            }
        }

        public bool HitTest(Point pt, Rectangle bounds)
        {
            return pt.Y <= bounds.Y + HeaderHeight;
        }

        public void OnClick(Point pt, Rectangle bounds)
        {
            if (pt.Y <= bounds.Y + HeaderHeight)
            {
                IsExpanded = !IsExpanded;
                ToggleRequested?.Invoke();
            }
        }

        private GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            if (r.Width < 1 || r.Height < 1) { p.AddRectangle(r); return p; }
            radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
            if (radius < 1) { p.AddRectangle(r); return p; }
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  工具调用卡片 — 展示操作进度与状态
    // ════════════════════════════════════════════════════════════════

    public class ToolCallCard : IContentBlock
    {
        public string ToolName { get; set; }
        public string StatusText { get; set; }
        public ToolCallStatus Status { get; set; }
        public List<string> Details { get; set; } = new List<string>();
        /// <summary>展开后显示的代码片段（如 execute_word_script 传入的 C# 代码）</summary>
        public string CodeSnippet { get; set; }
        public bool IsExpanded { get; set; }
        internal event Action ToggleRequested;

        // 与 ThinkBlock 保持一致的布局常量
        private const int HeaderH = 28;
        private const int DetailLineH = 22;
        private const int Pad = 10;

        // 字体与 ThinkBlock 一致
        private static readonly Font NameFont = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
        private static readonly Font StatusFont = new Font("Microsoft YaHei UI", 8.5F);
        private static readonly Font DetailFont = new Font("Microsoft YaHei UI", 8.5F);
        private static readonly Font CodeFont = new Font("Cascadia Code", 8.5F);

        // 颜色与 ThinkBlock 一致
        private static readonly Color CardBg = Color.FromArgb(243, 244, 246);
        private static readonly Color CardBorder = Color.FromArgb(229, 231, 235);
        private static readonly Color HeaderFg = Color.FromArgb(107, 114, 128);
        private static readonly Color ContentFg = Color.FromArgb(75, 85, 99);
        private static readonly Color RunningColor = Color.FromArgb(59, 130, 246);
        private static readonly Color SuccessColor = Color.FromArgb(22, 163, 74);
        private static readonly Color ErrorColor = Color.FromArgb(220, 38, 38);

        // 代码块颜色
        private static readonly Color CodeBg = Color.FromArgb(229, 231, 235);
        private static readonly Color CodeFg = Color.FromArgb(55, 65, 81);

        // 工具英文函数名 → 中文显示名：从 ToolRegistry 动态获取
        /// <summary>将函数名翻译为中文显示名</summary>
        private static string TranslateToolName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            var registry = Connect.CurrentInstance?.ToolRegistry;
            if (registry != null) return registry.GetDisplayName(name);
            return name;
        }

        public ToolCallCard(string toolName, string statusText, ToolCallStatus status)
        {
            ToolName = TranslateToolName(toolName);
            StatusText = statusText;
            Status = status;
            IsExpanded = false;
        }

        /// <summary>更新卡片状态</summary>
        public void Update(ToolCallStatus status, string statusText, List<string> details = null)
        {
            Status = status;
            StatusText = statusText;
            if (details != null) Details = details;
        }

        public int MeasureHeight(Graphics g, int width)
        {
            if (!IsExpanded) return HeaderH;
            int h = HeaderH;
            int contentW = width - 24;
            var canvas = g.High();

            // 代码块
            if (!string.IsNullOrEmpty(CodeSnippet))
            {
                h += 4; // 间距
                var codeSize = canvas.MeasureText(CodeSnippet, CodeFont, contentW - 16, FormatFlags.Top | FormatFlags.Left);
                h += codeSize.Height + 16; // 上下各 8px 内边距
            }

            // 状态文本
            if (!string.IsNullOrEmpty(StatusText))
            {
                var size = canvas.MeasureText(StatusText, StatusFont, contentW, FormatFlags.Top | FormatFlags.Left);
                h += Math.Max(DetailLineH, size.Height) + 4;
            }
            if (Details.Count > 0)
                h += Details.Count * DetailLineH + 4;
            return h;
        }

        public void Paint(Graphics g, Rectangle bounds, bool isUser)
        {
            var accentColor = Status == ToolCallStatus.Success ? SuccessColor
                            : Status == ToolCallStatus.Error ? ErrorColor
                            : RunningColor;

            // 圆角背景（与 ThinkBlock 一致）
            using (var path = RoundedRect(bounds, 8))
            {
                using (var brush = new SolidBrush(CardBg))
                    g.FillPath(brush, path);
                using (var pen = new Pen(CardBorder))
                    g.DrawPath(pen, path);
            }

            // 左侧装饰线（与 ThinkBlock 一致）
            using (var pen = new Pen(Color.FromArgb(156, 163, 175), 2))
                g.DrawLine(pen, bounds.X + 4, bounds.Y + 8, bounds.X + 4, bounds.Bottom - 8);

            var canvas = g.High();
            {
                // 折叠/展开指示符 + 状态 emoji + 工具中文名
                string chevron = IsExpanded ? "▾" : "▸";
                string icon = Status == ToolCallStatus.Success ? "✅"
                            : Status == ToolCallStatus.Error ? "❌" : "⏳";

                int textX = bounds.X + 12;
                int textY = bounds.Y;
                canvas.DrawText($"{chevron}  {icon} {ToolName}", NameFont, HeaderFg,
                    new Rectangle(textX, textY, bounds.Width - 24, HeaderH),
                    FormatFlags.VerticalCenter | FormatFlags.Left);

                // 折叠/展开提示
                using (var hintFont = new Font("Microsoft YaHei UI", 7.5F))
                    canvas.DrawText(IsExpanded ? "点击收起" : "点击展开", hintFont, Color.FromArgb(156, 163, 175),
                        new Rectangle(textX, textY, bounds.Width - 24, HeaderH),
                        FormatFlags.VerticalCenter | FormatFlags.Right);

                if (!IsExpanded) return;

                textY += HeaderH;

                // 分隔线
                int sepY = textY;
                using (var pen = new Pen(CardBorder))
                    g.DrawLine(pen, textX, sepY, bounds.Right - 12, sepY);

                // 代码块
                if (!string.IsNullOrEmpty(CodeSnippet))
                {
                    textY += 4;
                    int contentW = bounds.Width - 24;

                    // 代码背景区域
                    var codeSize = canvas.MeasureText(CodeSnippet, CodeFont, contentW - 16, FormatFlags.Top | FormatFlags.Left);
                    int codeBlockH = codeSize.Height + 16;
                    var codeRect = new Rectangle(textX, textY, contentW, codeBlockH);
                    using (var codePath = RoundedRect(codeRect, 6))
                    using (var codeBrush = new SolidBrush(CodeBg))
                        g.FillPath(codeBrush, codePath);

                    // 代码文本
                    canvas.DrawText(CodeSnippet, CodeFont, CodeFg,
                        new Rectangle(textX + 8, textY + 8, contentW - 16, codeSize.Height),
                        FormatFlags.Top | FormatFlags.Left);
                    textY += codeBlockH;
                }

                // 状态文本
                if (!string.IsNullOrEmpty(StatusText))
                {
                    var statusSize = canvas.MeasureText(StatusText, StatusFont, bounds.Width - 24, FormatFlags.Top | FormatFlags.Left);
                    int statusH = Math.Max(DetailLineH, statusSize.Height);
                    canvas.DrawText(StatusText, StatusFont, accentColor,
                        new Rectangle(textX, textY + 4, bounds.Width - 24, statusH),
                        FormatFlags.Top | FormatFlags.Left);
                    textY += statusH + 4;
                }

                // 详细信息列表
                foreach (var detail in Details)
                {
                    canvas.DrawText($"  ▪  {detail}", DetailFont, ContentFg,
                        new Rectangle(textX, textY, bounds.Width - 24, DetailLineH),
                        FormatFlags.VerticalCenter | FormatFlags.Left);
                    textY += DetailLineH;
                }
            }
        }

        public bool HitTest(Point pt, Rectangle bounds) => pt.Y <= bounds.Y + HeaderH;

        public void OnClick(Point pt, Rectangle bounds)
        {
            if (pt.Y <= bounds.Y + HeaderH)
            {
                IsExpanded = !IsExpanded;
                ToggleRequested?.Invoke();
            }
        }

        private GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            if (r.Width < 1 || r.Height < 1) { p.AddRectangle(r); return p; }
            radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
            if (radius < 1) { p.AddRectangle(r); return p; }
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  子智能体块 — 展示子智能体的完整执行过程
    // ════════════════════════════════════════════════════════════════

    /// <summary>子智能体执行步骤类型</summary>
    internal enum SubAgentStepType { Thinking, ToolCall, Output }

    /// <summary>子智能体内部执行步骤记录</summary>
    internal class SubAgentStep
    {
        public SubAgentStepType Type;
        public string Text;
        public ToolCallStatus Status; // 用于 ToolCall 类型
    }

    /// <summary>
    /// 子智能体专用内容块。展示子智能体的轮次、思考、工具调用和最终输出，
    /// 比普通 ToolCallCard 提供更丰富的执行过程可视化。
    /// </summary>
    public class SubAgentBlock : IContentBlock
    {
        public string TaskName { get; set; }
        public ToolCallStatus Status { get; set; }
        public bool IsExpanded { get; set; }
        internal event Action ToggleRequested;

        private readonly List<SubAgentStep> _steps = new List<SubAgentStep>();

        // 布局常量（与 ToolCallCard / ThinkBlock 一致）
        private const int HeaderH = 28;
        private const int StepLineH = 20;
        private const int Pad = 10;
        private const int MaxThinkPreviewLen = 100;
        private const int MaxOutputPreviewLen = 120;

        // 字体
        private static readonly Font HeaderFont = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
        private static readonly Font StepFont = new Font("Microsoft YaHei UI", 8F);


        // 颜色（与 ToolCallCard / ThinkBlock 一致）
        private static readonly Color CardBg = Color.FromArgb(243, 244, 246);
        private static readonly Color CardBorder = Color.FromArgb(229, 231, 235);
        private static readonly Color HeaderFg = Color.FromArgb(107, 114, 128);
        private static readonly Color ContentFg = Color.FromArgb(75, 85, 99);
        private static readonly Color ThinkFg = Color.FromArgb(130, 140, 155);

        private static readonly Color RunningColor = Color.FromArgb(59, 130, 246);
        private static readonly Color SuccessColor = Color.FromArgb(22, 163, 74);
        private static readonly Color ErrorColor = Color.FromArgb(220, 38, 38);
        private static readonly Color AccentLine = Color.FromArgb(99, 102, 241); // 靛蓝装饰线，区别于普通工具

        /// <summary>将函数名翻译为中文显示名</summary>
        private static string TranslateToolName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            var registry = Connect.CurrentInstance?.ToolRegistry;
            if (registry != null) return registry.GetDisplayName(name);
            return name;
        }

        public SubAgentBlock(string taskName)
        {
            TaskName = taskName;
            Status = ToolCallStatus.Running;
            IsExpanded = true; // 默认展开，让用户看到执行过程
        }

        // ── 步骤操作 API（由 ISubAgentProgress 实现调用）──

        /// <summary>添加"正在思考"占位步骤（LLM 请求期间显示，结束后自动移除）</summary>
        public void AddThinkingPlaceholder()
        {
            // 避免重复添加
            if (_steps.Count > 0 && _steps[_steps.Count - 1].Type == SubAgentStepType.Thinking
                && _steps[_steps.Count - 1].Text == "⏳")
                return;
            _steps.Add(new SubAgentStep
            {
                Type = SubAgentStepType.Thinking,
                Text = "⏳" // 特殊标记，渲染时显示为动态文字
            });
            ToggleRequested?.Invoke();
        }

        /// <summary>移除"正在思考"占位步骤</summary>
        private void RemoveThinkingPlaceholder()
        {
            _steps.RemoveAll(s => s.Type == SubAgentStepType.Thinking && s.Text == "⏳");
        }

        /// <summary>添加思考内容步骤</summary>
        public void AddThinking(string content)
        {
            RemoveThinkingPlaceholder(); // LLM 已返回，移除占位
            string preview = Abbreviate(content, MaxThinkPreviewLen);
            _steps.Add(new SubAgentStep
            {
                Type = SubAgentStepType.Thinking,
                Text = preview
            });
            ToggleRequested?.Invoke();
        }

        /// <summary>添加工具调用步骤（初始为运行中状态）</summary>
        public void AddToolCallStep(string toolName)
        {
            RemoveThinkingPlaceholder(); // 开始工具调用，移除占位
            _steps.Add(new SubAgentStep
            {
                Type = SubAgentStepType.ToolCall,
                Text = TranslateToolName(toolName),
                Status = ToolCallStatus.Running
            });
            ToggleRequested?.Invoke();
        }

        /// <summary>更新最后一个工具调用步骤的状态</summary>
        public void UpdateLastToolCallStep(bool success)
        {
            for (int i = _steps.Count - 1; i >= 0; i--)
            {
                if (_steps[i].Type == SubAgentStepType.ToolCall)
                {
                    _steps[i].Status = success ? ToolCallStatus.Success : ToolCallStatus.Error;
                    break;
                }
            }
            ToggleRequested?.Invoke();
        }

        /// <summary>添加最终输出步骤</summary>
        public void AddOutput(string text, bool success)
        {
            if (!string.IsNullOrEmpty(text))
            {
                string preview = Abbreviate(text, MaxOutputPreviewLen);
                _steps.Add(new SubAgentStep
                {
                    Type = SubAgentStepType.Output,
                    Text = preview,
                    Status = success ? ToolCallStatus.Success : ToolCallStatus.Error
                });
            }
            ToggleRequested?.Invoke();
        }

        /// <summary>标记整体完成状态</summary>
        public void SetComplete(ToolCallStatus status)
        {
            Status = status;
            ToggleRequested?.Invoke();
        }

        // ── IContentBlock 实现 ──

        public int MeasureHeight(Graphics g, int width)
        {
            if (!IsExpanded) return HeaderH;
            int h = HeaderH + 4; // 4px padding after header separator
            foreach (var step in _steps)
                h += StepLineH;
            return Math.Max(HeaderH, h);
        }

        public void Paint(Graphics g, Rectangle bounds, bool isUser)
        {
            // 圆角背景
            using (var path = RoundedRect(bounds, 8))
            {
                using (var brush = new SolidBrush(CardBg))
                    g.FillPath(brush, path);
                using (var pen = new Pen(CardBorder))
                    g.DrawPath(pen, path);
            }

            // 左侧装饰线（靛蓝色，区别于普通工具的灰色）
            using (var pen = new Pen(AccentLine, 2))
                g.DrawLine(pen, bounds.X + 4, bounds.Y + 8, bounds.X + 4, bounds.Bottom - 8);

            var canvas = g.High();

            // ── 头部 ──
            string chevron = IsExpanded ? "▾" : "▸";
            string statusIcon = Status == ToolCallStatus.Success ? "✅"
                              : Status == ToolCallStatus.Error ? "❌" : "⏳";
            string statusLabel = Status == ToolCallStatus.Success ? "完成"
                               : Status == ToolCallStatus.Error ? "失败" : "执行中";

            canvas.DrawText($"{chevron}  🤖 子智能体：{TaskName}", HeaderFont, HeaderFg,
                new Rectangle(bounds.X + 12, bounds.Y, bounds.Width - 24, HeaderH),
                FormatFlags.VerticalCenter | FormatFlags.Left);

            canvas.DrawText($"{statusIcon} {statusLabel}", HeaderFont,
                Status == ToolCallStatus.Success ? SuccessColor
                : Status == ToolCallStatus.Error ? ErrorColor : RunningColor,
                new Rectangle(bounds.X + 12, bounds.Y, bounds.Width - 24, HeaderH),
                FormatFlags.VerticalCenter | FormatFlags.Right);

            if (!IsExpanded) return;

            // ── 分隔线 ──
            int sepY = bounds.Y + HeaderH;
            using (var pen = new Pen(CardBorder))
                g.DrawLine(pen, bounds.X + 12, sepY, bounds.Right - 12, sepY);

            // ── 步骤列表 ──
            int y = sepY + 4;
            foreach (var step in _steps)
            {
                var rect = new Rectangle(bounds.X + 16, y, bounds.Width - 32, StepLineH);

                switch (step.Type)
                {
                    case SubAgentStepType.Thinking:
                        if (step.Text == "⏳")
                            canvas.DrawText("💭 正在思考...", StepFont, RunningColor, rect,
                                FormatFlags.VerticalCenter | FormatFlags.Left);
                        else
                            canvas.DrawText($"💭 {step.Text}", StepFont, ThinkFg, rect,
                                FormatFlags.VerticalCenter | FormatFlags.Left);
                        break;

                    case SubAgentStepType.ToolCall:
                        string toolIcon = step.Status == ToolCallStatus.Success ? "✅"
                                        : step.Status == ToolCallStatus.Error ? "❌" : "⏳";
                        var toolColor = step.Status == ToolCallStatus.Success ? SuccessColor
                                      : step.Status == ToolCallStatus.Error ? ErrorColor : RunningColor;
                        canvas.DrawText($"🔧 {step.Text}", StepFont, ContentFg, rect,
                            FormatFlags.VerticalCenter | FormatFlags.Left);
                        canvas.DrawText(toolIcon, StepFont, toolColor, rect,
                            FormatFlags.VerticalCenter | FormatFlags.Right);
                        break;

                    case SubAgentStepType.Output:
                        var outputColor = step.Status == ToolCallStatus.Success ? SuccessColor : ErrorColor;
                        canvas.DrawText($"📋 {step.Text}", StepFont, outputColor, rect,
                            FormatFlags.VerticalCenter | FormatFlags.Left);
                        break;
                }

                y += StepLineH;
            }
        }

        public bool HitTest(Point pt, Rectangle bounds) => pt.Y <= bounds.Y + HeaderH;

        public void OnClick(Point pt, Rectangle bounds)
        {
            if (pt.Y <= bounds.Y + HeaderH)
            {
                IsExpanded = !IsExpanded;
                ToggleRequested?.Invoke();
            }
        }

        // ── 工具方法 ──

        private static string Abbreviate(string text, int maxLen)
        {
            if (string.IsNullOrEmpty(text)) return "";
            // 取第一个有效行（跳过空行）
            string firstLine = null;
            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("#"))
                {
                    firstLine = trimmed;
                    break;
                }
            }
            if (firstLine == null) firstLine = text.Replace('\n', ' ').Trim();
            if (firstLine.Length > maxLen)
                firstLine = firstLine.Substring(0, maxLen) + "…";
            return firstLine;
        }

        private GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            if (r.Width < 1 || r.Height < 1) { p.AddRectangle(r); return p; }
            radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
            if (radius < 1) { p.AddRectangle(r); return p; }
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  审批卡片 — 操作确认嵌入式卡片（替代弹窗 MessageBox）
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// 操作审批确认卡片 — 嵌入聊天面板，用户通过点击按钮完成审批。
    /// 使用 <see cref="ResultTask"/> 异步等待用户决策。
    /// </summary>
    internal class ApprovalCard : IContentBlock
    {
        public string ToolDisplayName { get; }
        public string FunctionName { get; }
        public string Summary { get; }

        private bool? _result;
        private readonly TaskCompletionSource<bool> _tcs = new TaskCompletionSource<bool>();

        /// <summary>等待用户审批结果（true=允许，false=拒绝）</summary>
        public Task<bool> ResultTask => _tcs.Task;

        internal event Action ToggleRequested;

        // 布局常量
        private const int HeaderH = 30;
        private const int TopPad = 14;
        private const int SidePad = 14;
        private const int SepGap = 10;     // 标题与分隔线间距
        private const int ContentGap = 12; // 分隔线与正文间距
        private const int ButtonH = 32;
        private const int ButtonW = 88;
        private const int ButtonGap = 12;
        private const int ButtonTopGap = 12;
        private const int BottomPad = 14;
        private const int ResultH = 26;
        private const int ToolBadgeH = 26; // 工具名称徽章行高
        private const int LabelLineH = 22; // "参数:" 标签行高
        private const int CodeBlockPadX = 10; // 代码块水平内边距
        private const int CodeBlockPadY = 8;  // 代码块垂直内边距

        // 缓存按钮 Y 偏移（相对于块顶部），由 MeasureHeight 计算
        private int _cachedBtnYOffset;

        // 字体
        private static readonly Font HeaderFont = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
        private static readonly Font DescFont = new Font("Microsoft YaHei UI", 8.5F);
        private static readonly Font LabelFont = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
        private static readonly Font CodeFont = new Font("Consolas", 8.5F);
        private static readonly Font ButtonFont = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
        private static readonly Font ResultFont = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);

        // 颜色 — 低饱和度柔和风格
        private static readonly Color PendingBg = Color.FromArgb(254, 252, 243);       // 极浅暖黄
        private static readonly Color PendingBorder = Color.FromArgb(234, 213, 160);   // 柔和琥珀边框
        private static readonly Color HeaderFg = Color.FromArgb(120, 80, 20);          // 深棕标题
        private static readonly Color DescFg = Color.FromArgb(140, 110, 60);           // 柔和描述文本
        private static readonly Color SepColor = Color.FromArgb(230, 218, 185);        // 分隔线

        // 工具名称徽章
        private static readonly Color ToolBadgeBg = Color.FromArgb(254, 243, 199);     // 暖黄徽章背景
        private static readonly Color ToolBadgeBorder = Color.FromArgb(234, 213, 160); // 徽章边框
        private static readonly Color ToolBadgeFg = Color.FromArgb(120, 80, 20);       // 徽章文字

        // 代码块
        private static readonly Color CodeBlockBg = Color.FromArgb(245, 243, 235);     // 暖色代码块背景
        private static readonly Color CodeBlockBorder = Color.FromArgb(230, 218, 185); // 代码块边框
        private static readonly Color CodeFg = Color.FromArgb(55, 65, 81);             // 代码文字

        // 允许按钮 — 红色警告风格
        private static readonly Color ApproveBtnBg = Color.FromArgb(254, 242, 242);     // 极浅红
        private static readonly Color ApproveBtnBorder = Color.FromArgb(252, 165, 165); // 柔和红边框
        private static readonly Color ApproveBtnText = Color.FromArgb(153, 27, 27);     // 深红文字
        private static readonly Color RejectBtnBg = Color.FromArgb(243, 244, 246);      // 极浅灰
        private static readonly Color RejectBtnBorder = Color.FromArgb(209, 213, 219);  // 灰边框
        private static readonly Color RejectBtnText = Color.FromArgb(55, 65, 81);       // 深灰文字

        // 审批结果状态背景
        private static readonly Color ApprovedBg = Color.FromArgb(236, 253, 245);
        private static readonly Color ApprovedBorder = Color.FromArgb(134, 239, 172);
        private static readonly Color ApprovedFg = Color.FromArgb(22, 101, 52);
        private static readonly Color RejectedBg = Color.FromArgb(254, 242, 242);
        private static readonly Color RejectedBorder = Color.FromArgb(252, 165, 165);
        private static readonly Color RejectedFg = Color.FromArgb(153, 27, 27);

        public ApprovalCard(string toolDisplayName, string functionName, string summary)
        {
            ToolDisplayName = toolDisplayName;
            FunctionName = functionName;
            Summary = summary;
        }

        public int MeasureHeight(Graphics g, int width)
        {
            int textW = width - SidePad * 2;
            var canvas = g.High();

            int h = TopPad;
            h += HeaderH;          // 标题行
            h += SepGap;           // 标题→分隔线间距
            h += 1;                // 分隔线
            h += ContentGap;       // 分隔线→正文间距

            // 描述提示语
            var descSize = canvas.MeasureText("AI 助手请求执行以下操作，请确认是否允许：", DescFont, textW, FormatFlags.Top | FormatFlags.Left);
            h += descSize.Height + 12;

            // 工具名称行（徽章）
            h += ToolBadgeH + 10;

            // 参数代码块
            if (!string.IsNullOrEmpty(Summary))
            {
                h += LabelLineH; // "参数:" 标签
                int codeW = textW - CodeBlockPadX * 2;
                var codeSize = canvas.MeasureText(Summary, CodeFont, codeW, FormatFlags.Top | FormatFlags.Left);
                h += codeSize.Height + CodeBlockPadY * 2 + ButtonTopGap;
            }
            else
            {
                h += 4;
            }

            _cachedBtnYOffset = h; // 缓存按钮起始位置

            if (_result == null)
                h += ButtonH + BottomPad;
            else
                h += ResultH + BottomPad;

            return h;
        }

        public void Paint(Graphics g, Rectangle bounds, bool isUser)
        {
            var bgColor = _result == null ? PendingBg
                        : _result.Value ? ApprovedBg : RejectedBg;
            var borderColor = _result == null ? PendingBorder
                            : _result.Value ? ApprovedBorder : RejectedBorder;
            var accentColor = _result == null ? PendingBorder
                            : _result.Value ? ApprovedFg : RejectedFg;

            // 圆角背景
            using (var path = RoundedRect(bounds, 8))
            {
                using (var brush = new SolidBrush(bgColor))
                    g.FillPath(brush, path);
                using (var pen = new Pen(borderColor))
                    g.DrawPath(pen, path);
            }

            var canvas = g.High();
            int textX = bounds.X + SidePad;
            int textW = bounds.Width - SidePad * 2;
            int y = bounds.Y + TopPad;

            // 标题
            canvas.DrawText("操作确认", HeaderFont, HeaderFg,
                new Rectangle(textX, y, textW, HeaderH),
                FormatFlags.VerticalCenter | FormatFlags.Left);
            y += HeaderH + SepGap;

            // 分隔线
            using (var pen = new Pen(_result == null ? SepColor : borderColor))
                g.DrawLine(pen, textX, y, bounds.Right - SidePad, y);
            y += 1 + ContentGap;

            // 描述提示
            string desc = "AI 助手请求执行以下操作，请确认是否允许：";
            var descSize = canvas.MeasureText(desc, DescFont, textW, FormatFlags.Top | FormatFlags.Left);
            canvas.DrawText(desc, DescFont, DescFg,
                new Rectangle(textX, y, textW, descSize.Height),
                FormatFlags.Top | FormatFlags.Left);
            y += descSize.Height + 12;

            // 工具名称徽章
            string toolLabel = "工具: ";
            var toolLabelSize = canvas.MeasureText(toolLabel, LabelFont, textW, FormatFlags.VerticalCenter | FormatFlags.Left);
            canvas.DrawText(toolLabel, LabelFont, HeaderFg,
                new Rectangle(textX, y, toolLabelSize.Width + 2, ToolBadgeH),
                FormatFlags.VerticalCenter | FormatFlags.Left);

            string toolName = $"{ToolDisplayName} ({FunctionName})";
            int badgeX = textX + toolLabelSize.Width + 6;
            var toolNameSize = canvas.MeasureText(toolName, DescFont, textW, FormatFlags.Top | FormatFlags.Left);
            int badgeW = Math.Min(toolNameSize.Width + 16, bounds.Right - SidePad - badgeX);
            var badgeRect = new Rectangle(badgeX, y + (ToolBadgeH - 20) / 2, badgeW, 20);
            using (var path = RoundedRect(badgeRect, 4))
            {
                using (var brush = new SolidBrush(ToolBadgeBg))
                    g.FillPath(brush, path);
                using (var pen = new Pen(ToolBadgeBorder))
                    g.DrawPath(pen, path);
            }
            canvas.DrawText(toolName, DescFont, ToolBadgeFg, badgeRect, FormatFlags.Center);
            y += ToolBadgeH + 10;

            // 参数代码块
            if (!string.IsNullOrEmpty(Summary))
            {
                // "参数:" 标签
                canvas.DrawText("参数:", LabelFont, HeaderFg,
                    new Rectangle(textX, y, textW, LabelLineH),
                    FormatFlags.VerticalCenter | FormatFlags.Left);
                y += LabelLineH;

                // 代码块背景
                int codeW = textW - CodeBlockPadX * 2;
                var codeSize = canvas.MeasureText(Summary, CodeFont, codeW, FormatFlags.Top | FormatFlags.Left);
                int codeBlockH = codeSize.Height + CodeBlockPadY * 2;
                var codeRect = new Rectangle(textX, y, textW, codeBlockH);
                using (var path = RoundedRect(codeRect, 6))
                {
                    using (var brush = new SolidBrush(CodeBlockBg))
                        g.FillPath(brush, path);
                    using (var pen = new Pen(CodeBlockBorder))
                        g.DrawPath(pen, path);
                }
                canvas.DrawText(Summary, CodeFont, CodeFg,
                    new Rectangle(textX + CodeBlockPadX, y + CodeBlockPadY, codeW, codeSize.Height),
                    FormatFlags.Top | FormatFlags.Left);
                y += codeBlockH + ButtonTopGap;
            }
            else
            {
                y += 4;
            }

            if (_result == null)
            {
                // 绘制审批按钮（柔和描边风格）
                int btnX = textX;

                // 允许按钮
                var approveRect = new Rectangle(btnX, y, ButtonW, ButtonH);
                using (var path = RoundedRect(approveRect, 6))
                {
                    using (var brush = new SolidBrush(ApproveBtnBg))
                        g.FillPath(brush, path);
                    using (var pen = new Pen(ApproveBtnBorder))
                        g.DrawPath(pen, path);
                }
                canvas.DrawText("允许执行", ButtonFont, ApproveBtnText, approveRect,
                    FormatFlags.Center);

                // 拒绝按钮
                var rejectRect = new Rectangle(btnX + ButtonW + ButtonGap, y, ButtonW, ButtonH);
                using (var path = RoundedRect(rejectRect, 6))
                {
                    using (var brush = new SolidBrush(RejectBtnBg))
                        g.FillPath(brush, path);
                    using (var pen = new Pen(RejectBtnBorder))
                        g.DrawPath(pen, path);
                }
                canvas.DrawText("拒绝", ButtonFont, RejectBtnText, rejectRect,
                    FormatFlags.Center);
            }
            else
            {
                // 显示审批结果
                string resultText = _result.Value ? "已允许执行" : "已拒绝执行";
                var resultColor = _result.Value ? ApprovedFg : RejectedFg;
                canvas.DrawText(resultText, ResultFont, resultColor,
                    new Rectangle(textX, y, textW, ResultH),
                    FormatFlags.VerticalCenter | FormatFlags.Left);
            }
        }

        public bool HitTest(Point pt, Rectangle bounds)
        {
            if (_result != null) return false;
            var (approveRect, rejectRect) = ComputeButtonRects(bounds);
            return approveRect.Contains(pt) || rejectRect.Contains(pt);
        }

        public void OnClick(Point pt, Rectangle bounds)
        {
            if (_result != null) return;
            var (approveRect, rejectRect) = ComputeButtonRects(bounds);

            if (approveRect.Contains(pt))
            {
                _result = true;
                _tcs.TrySetResult(true);
                ToggleRequested?.Invoke();
            }
            else if (rejectRect.Contains(pt))
            {
                _result = false;
                _tcs.TrySetResult(false);
                ToggleRequested?.Invoke();
            }
        }

        private (Rectangle approve, Rectangle reject) ComputeButtonRects(Rectangle bounds)
        {
            int y = bounds.Y + _cachedBtnYOffset;
            int btnX = bounds.X + SidePad;
            return (
                new Rectangle(btnX, y, ButtonW, ButtonH),
                new Rectangle(btnX + ButtonW + ButtonGap, y, ButtonW, ButtonH)
            );
        }

        private GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            if (r.Width < 1 || r.Height < 1) { p.AddRectangle(r); return p; }
            radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
            if (radius < 1) { p.AddRectangle(r); return p; }
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  用户问答卡片 — LLM 向用户提问，支持选项和自由输入
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// LLM 向用户提问的卡片。支持选项点击和自由文本输入。
    /// 使用 <see cref="ResultTask"/> 异步等待用户回答。
    /// </summary>
    internal class AskUserCard : IContentBlock
    {
        public string Question { get; }
        public List<AskUserOption> Options { get; }
        public bool AllowFreeInput { get; }

        private string _answer;
        private readonly TaskCompletionSource<string> _tcs = new TaskCompletionSource<string>();

        /// <summary>等待用户回答</summary>
        public Task<string> ResultTask => _tcs.Task;

        internal event Action ToggleRequested;

        /// <summary>嵌入的输入控件（由 MessageGroup.AddAskUserCard 创建并注入）</summary>
        internal AntdUI.Input EmbeddedInput { get; set; }
        internal AntdUI.Button EmbeddedSubmitBtn { get; set; }

        // 布局常量
        private const int TopPad = 14;
        private const int SidePad = 14;
        private const int HeaderH = 28;
        private const int SepGap = 8;
        private const int ContentGap = 10;
        private const int PillH = 36;
        private const int PillPadH = 16;
        private const int PillGap = 8;
        private const int PillRowGap = 8;
        private const int InputAreaH = 36;
        private const int InputGap = 10;
        private const int SubmitBtnW = 64;
        private const int BottomPad = 14;
        private const int AnsweredH = 26;

        // 字体
        private static readonly Font HeaderFont = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
        private static readonly Font QuestionFont = new Font("Microsoft YaHei UI", 9F);
        private static readonly Font PillFont = new Font("Microsoft YaHei UI", 8.5F);
        private static readonly Font PillDescFont = new Font("Microsoft YaHei UI", 7.5F);
        private static readonly Font AnsweredFont = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);

        // 颜色 — 冷灰蓝调，与审批卡片的暖黄区分
        private static readonly Color CardBg = Color.FromArgb(248, 250, 254);
        private static readonly Color CardBorder = Color.FromArgb(196, 206, 224);
        private static readonly Color AccentColor = Color.FromArgb(99, 120, 180);
        private static readonly Color HeaderFg = Color.FromArgb(40, 52, 80);
        private static readonly Color QuestionFg = Color.FromArgb(55, 65, 81);
        private static readonly Color SepColor = Color.FromArgb(218, 225, 238);

        // 选项 pill — 柔和描边
        private static readonly Color PillBg = Color.FromArgb(238, 243, 252);
        private static readonly Color PillBorder = Color.FromArgb(180, 198, 230);
        private static readonly Color PillText = Color.FromArgb(45, 65, 130);
        private static readonly Color PillDescColor = Color.FromArgb(100, 116, 150);
        private static readonly Color PillHoverBg = Color.FromArgb(222, 232, 250);

        // 已回答状态
        private static readonly Color AnsweredBg = Color.FromArgb(240, 249, 244);
        private static readonly Color AnsweredBorder = Color.FromArgb(167, 227, 190);
        private static readonly Color AnsweredAccent = Color.FromArgb(22, 101, 52);
        private static readonly Color AnsweredFg = Color.FromArgb(22, 101, 52);

        // 缓存 pill 矩形（MeasureHeight 中计算）
        private List<Rectangle> _pillRects = new List<Rectangle>();
        private int _cachedPillsEndY;
        private int _hoveredPillIndex = -1;
        private int _cachedInputYOffset;

        public AskUserCard(string question, List<AskUserOption> options, bool allowFreeInput)
        {
            Question = question;
            Options = options ?? new List<AskUserOption>();
            AllowFreeInput = allowFreeInput;
        }

        public int MeasureHeight(Graphics g, int width)
        {
            int textW = width - SidePad * 2;
            var canvas = g.High();

            int h = TopPad;
            h += HeaderH;
            h += SepGap + 1 + ContentGap; // 分隔线区

            // 问题文本
            var qSize = canvas.MeasureText(Question, QuestionFont, textW, FormatFlags.Top | FormatFlags.Left);
            h += qSize.Height + ContentGap;

            // 计算 pill 布局（全宽垂直堆叠，保持视觉整齐）
            _pillRects.Clear();
            if (Options.Count > 0 && _answer == null)
            {
                int py = h;
                foreach (var opt in Options)
                {
                    int pillHeight = opt.Description != null ? PillH + 14 : PillH;
                    _pillRects.Add(new Rectangle(0, py, textW, pillHeight));
                    py += pillHeight + PillRowGap;
                }
                h = py;
                _cachedPillsEndY = h;
            }
            else if (_answer != null)
            {
                h += AnsweredH;
                _cachedPillsEndY = h;
            }

            // 自由输入区
            if (AllowFreeInput && _answer == null)
            {
                _cachedInputYOffset = h;
                h += InputAreaH + InputGap;
            }

            h += BottomPad;
            return h;
        }

        public void Paint(Graphics g, Rectangle bounds, bool isUser)
        {
            bool answered = _answer != null;
            var bgColor = answered ? AnsweredBg : CardBg;
            var borderColor = answered ? AnsweredBorder : CardBorder;
            var accentClr = answered ? AnsweredAccent : AccentColor;

            // 圆角背景
            using (var path = RoundedRect(bounds, 8))
            {
                using (var brush = new SolidBrush(bgColor))
                    g.FillPath(brush, path);
                using (var pen = new Pen(borderColor))
                    g.DrawPath(pen, path);
            }

            // 左侧装饰线
            using (var pen = new Pen(accentClr, 2.5f))
                g.DrawLine(pen, bounds.X + 4, bounds.Y + 10, bounds.X + 4, bounds.Bottom - 10);

            var canvas = g.High();
            int textX = bounds.X + SidePad;
            int textW = bounds.Width - SidePad * 2;
            int y = bounds.Y + TopPad;

            // 标题
            canvas.DrawText("💬 需要您的输入", HeaderFont, answered ? AnsweredFg : HeaderFg,
                new Rectangle(textX, y, textW, HeaderH),
                FormatFlags.VerticalCenter | FormatFlags.Left);
            y += HeaderH + SepGap;

            // 分隔线
            using (var pen = new Pen(answered ? AnsweredBorder : SepColor))
                g.DrawLine(pen, textX, y, bounds.Right - SidePad, y);
            y += 1 + ContentGap;

            // 问题文本
            var qSize = canvas.MeasureText(Question, QuestionFont, textW, FormatFlags.Top | FormatFlags.Left);
            canvas.DrawText(Question, QuestionFont, QuestionFg,
                new Rectangle(textX, y, textW, qSize.Height),
                FormatFlags.Top | FormatFlags.Left);
            y += qSize.Height + ContentGap;

            if (!answered)
            {
                // 绘制选项 pills
                for (int i = 0; i < _pillRects.Count && i < Options.Count; i++)
                {
                    var pr = _pillRects[i];
                    var pillBounds = new Rectangle(textX + pr.X, bounds.Y + pr.Y, pr.Width, pr.Height);
                    bool hovered = (i == _hoveredPillIndex);

                    using (var path = RoundedRect(pillBounds, 6))
                    {
                        using (var brush = new SolidBrush(hovered ? PillHoverBg : PillBg))
                            g.FillPath(brush, path);
                        using (var pen = new Pen(PillBorder))
                            g.DrawPath(pen, path);
                    }

                    if (Options[i].Description != null)
                    {
                        int labelH = PillH - 4;
                        canvas.DrawText(Options[i].Label, PillFont, PillText,
                            new Rectangle(pillBounds.X + PillPadH, pillBounds.Y + 2, pillBounds.Width - PillPadH * 2, labelH),
                            FormatFlags.VerticalCenter | FormatFlags.Left);
                        canvas.DrawText(Options[i].Description, PillDescFont, PillDescColor,
                            new Rectangle(pillBounds.X + PillPadH, pillBounds.Y + labelH - 2, pillBounds.Width - PillPadH * 2, 14),
                            FormatFlags.Top | FormatFlags.Left);
                    }
                    else
                    {
                        canvas.DrawText(Options[i].Label, PillFont, PillText, pillBounds,
                            FormatFlags.Center);
                    }
                }

                // 定位嵌入的 AntdUI 输入控件
                if (AllowFreeInput && EmbeddedInput != null && EmbeddedSubmitBtn != null)
                {
                    int inputY = bounds.Y + _cachedInputYOffset;
                    int inputW = textW - SubmitBtnW - PillGap;
                    EmbeddedInput.SetBounds(textX, inputY, inputW, InputAreaH);
                    EmbeddedSubmitBtn.SetBounds(textX + inputW + PillGap, inputY, SubmitBtnW, InputAreaH);
                    EmbeddedInput.Visible = true;
                    EmbeddedSubmitBtn.Visible = true;
                }
            }
            else
            {
                // 已回答：显示答案
                canvas.DrawText($"已回答：{_answer}", AnsweredFont, AnsweredFg,
                    new Rectangle(textX, y, textW, AnsweredH),
                    FormatFlags.VerticalCenter | FormatFlags.Left);

                // 隐藏嵌入控件
                if (EmbeddedInput != null) EmbeddedInput.Visible = false;
                if (EmbeddedSubmitBtn != null) EmbeddedSubmitBtn.Visible = false;
            }
        }

        public bool HitTest(Point pt, Rectangle bounds)
        {
            if (_answer != null) return false;
            int textX = bounds.X + SidePad;
            for (int i = 0; i < _pillRects.Count; i++)
            {
                var pr = _pillRects[i];
                var pillBounds = new Rectangle(textX + pr.X, bounds.Y + pr.Y, pr.Width, pr.Height);
                if (pillBounds.Contains(pt)) return true;
            }
            return false;
        }

        /// <summary>鼠标移动时更新 hover 状态（由 MessageGroup 调用）</summary>
        internal bool UpdateHover(Point pt, Rectangle bounds)
        {
            if (_answer != null) { _hoveredPillIndex = -1; return false; }
            int textX = bounds.X + SidePad;
            int oldHover = _hoveredPillIndex;
            _hoveredPillIndex = -1;
            for (int i = 0; i < _pillRects.Count; i++)
            {
                var pr = _pillRects[i];
                var pillBounds = new Rectangle(textX + pr.X, bounds.Y + pr.Y, pr.Width, pr.Height);
                if (pillBounds.Contains(pt)) { _hoveredPillIndex = i; break; }
            }
            return _hoveredPillIndex != oldHover;
        }

        public void OnClick(Point pt, Rectangle bounds)
        {
            if (_answer != null) return;
            int textX = bounds.X + SidePad;
            for (int i = 0; i < _pillRects.Count && i < Options.Count; i++)
            {
                var pr = _pillRects[i];
                var pillBounds = new Rectangle(textX + pr.X, bounds.Y + pr.Y, pr.Width, pr.Height);
                if (pillBounds.Contains(pt))
                {
                    Submit(Options[i].Label);
                    return;
                }
            }
        }

        /// <summary>提交自由文本答案（由嵌入的提交按钮调用）</summary>
        internal void SubmitFreeText(string text)
        {
            if (_answer != null || string.IsNullOrWhiteSpace(text)) return;
            Submit(text.Trim());
        }

        private void Submit(string answer)
        {
            _answer = answer;
            _tcs.TrySetResult(answer);
            ToggleRequested?.Invoke();
        }

        private GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            if (r.Width < 1 || r.Height < 1) { p.AddRectangle(r); return p; }
            radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
            if (radius < 1) { p.AddRectangle(r); return p; }
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    /// <summary>ask_user 工具的选项</summary>
    public class AskUserOption
    {
        public string Label { get; set; }
        public string Description { get; set; }
    }

    // ════════════════════════════════════════════════════════════════
    //  MessageGroup — 一条完整消息（头像 + 气泡 + 内容块）
    // ════════════════════════════════════════════════════════════════

    public class MessageGroup : System.Windows.Forms.Panel
    {
        public ChatRole Role { get; }
        public string UserName { get; }
        public Image Avatar { get; }
        internal int MaxBubbleWidth { get; set; }

        // 事件：内容变化时通知父容器重新布局
        internal event Action LayoutChanged;

        // 布局常量
        private const int AvatarSize = 40;
        private const int AvatarGap = 8;
        private const int BubblePadV = 12;
        private const int BubblePadH = 14;
        private const int Radius = 12;
        private const int BlockGap = 8;
        private const int NameHeight = 0; // 不显示名称，只显示头像

        // 内容块
        private readonly List<IContentBlock> _blocks = new List<IContentBlock>();

        // 测量缓存：避免在 OnPaint/OnMouseMove 中重复 Measure
        private readonly List<int> _cachedBlockHeights = new List<int>();
        private int _cachedMeasuredWidth = -1;
        private int _cachedTotalContentHeight;
        private bool _measureDirty = true;
        private bool _containsInteractiveBlocks;
        private bool _lastHandCursor;

        // 颜色
        private static readonly Color AIBg = Color.White;
        private static readonly Color UserBg = Color.FromArgb(59, 130, 246);
        private static readonly Color AIBorder = Color.FromArgb(229, 231, 235);
        private static readonly Color NameColor = Color.FromArgb(107, 114, 128);
        private static readonly Color ShadowColor = Color.FromArgb(15, 0, 0, 0);
        private static readonly Color PanelBg = Color.FromArgb(245, 247, 250);

        public MessageGroup(string name, Image avatar, ChatRole role, int maxWidth)
        {
            Role = role;
            UserName = name;
            Avatar = avatar;
            MaxBubbleWidth = maxWidth;

            SetStyle(ControlStyles.UserPaint
                   | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = PanelBg;
            Cursor = Cursors.Default;

            // 右键菜单 — 支持复制消息文本
            var ctxMenu = new System.Windows.Forms.ContextMenuStrip();
            var copyItem = new ToolStripMenuItem("复制文本");
            copyItem.Click += (s, ev) =>
            {
                var allText = GetPlainText();
                if (!string.IsNullOrEmpty(allText))
                    Clipboard.SetText(allText);
            };
            ctxMenu.Items.Add(copyItem);
            ContextMenuStrip = ctxMenu;
        }

        // ── 公共 API ──

        /// <summary>获取所有文本块的纯文本（用于复制）</summary>
        public string GetPlainText()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var block in _blocks)
            {
                if (block is TextBlock tb && !string.IsNullOrEmpty(tb.Text))
                    sb.AppendLine(tb.Text);
                else if (block is ThinkBlock thk && !string.IsNullOrEmpty(thk.Text))
                    sb.AppendLine(thk.Text);
                else if (block is TableBlock tbl)
                    sb.AppendLine(tbl.GetPlainText());
                else if (block is SubAgentBlock sab)
                    sb.AppendLine($"[子智能体: {sab.TaskName}]");
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>设置文本内容，自动解析 &lt;think&gt; 标签为折叠块，自动识别 Markdown 表格。</summary>
        public void SetText(string text)
        {
            text = text ?? "";

            // 解析 <think>...</think> 标签
            string thinkContent = null;
            string mainContent = text;

            var thinkMatch = Regex.Match(text, @"<think>(.*?)(?:</think>|$)", RegexOptions.Singleline);
            if (thinkMatch.Success)
            {
                thinkContent = thinkMatch.Groups[1].Value.Trim();
                mainContent = Regex.Replace(text, @"\s*<think>.*?(?:</think>|$)\s*", "", RegexOptions.Singleline).Trim();
            }

            // 确定当前流式段落的起点（最后一个 ToolCallCard 之后）
            int sectionStart = 0;
            for (int i = _blocks.Count - 1; i >= 0; i--)
            {
                if (_blocks[i] is ToolCallCard)
                {
                    sectionStart = i + 1;
                    break;
                }
            }

            // 保留 ThinkBlock 的折叠状态
            ThinkBlock existingThink = null;
            for (int i = sectionStart; i < _blocks.Count; i++)
            {
                if (_blocks[i] is ThinkBlock tb) { existingThink = tb; break; }
            }

            // 移除当前段落中所有内容块（TextBlock / TableBlock），保留 ThinkBlock
            for (int i = _blocks.Count - 1; i >= sectionStart; i--)
            {
                if (!(_blocks[i] is ThinkBlock))
                    _blocks.RemoveAt(i);
            }

            // 处理 ThinkBlock
            if (thinkContent != null)
            {
                if (existingThink != null)
                    existingThink.Text = thinkContent;
                else
                {
                    var thinkBlock = new ThinkBlock(thinkContent);
                    _blocks.Insert(sectionStart, thinkBlock);
                    thinkBlock.ToggleRequested += () => RequestLayout();
                }
            }

            // 解析主要内容为内容块序列（TextBlock + TableBlock）
            if (!string.IsNullOrEmpty(mainContent))
            {
                var contentBlocks = SplitIntoBlocks(mainContent, Role);
                _blocks.AddRange(contentBlocks);
            }

            RequestLayout();
        }

        // ── Markdown 表格检测辅助方法 ──

        private static bool IsTableLine(string line)
        {
            var trimmed = line.Trim();
            return trimmed.StartsWith("|") && trimmed.EndsWith("|") && trimmed.Length >= 3;
        }

        private static bool IsValidTable(List<string> lines)
        {
            // 至少 header + separator + 1 data row = 3 行
            if (lines.Count < 3) return false;
            return Regex.IsMatch(lines[1].Trim(), @"^\|[\s\-:\|]+\|$");
        }

        private static List<IContentBlock> SplitIntoBlocks(string text, ChatRole role)
        {
            var blocks = new List<IContentBlock>();
            if (string.IsNullOrEmpty(text)) return blocks;

            var lines = text.Split('\n');
            var textLines = new List<string>();
            var tableLines = new List<string>();
            var quoteLines = new List<string>();

            // 围栏代码块状态
            bool inCodeBlock = false;
            string codeLanguage = "";
            var codeLines = new List<string>();

            // 辅助：刷出当前积累的文本行
            void FlushText()
            {
                if (textLines.Count > 0)
                {
                    blocks.Add(new TextBlock(string.Join("\n", textLines), role));
                    textLines.Clear();
                }
            }

            // 辅助：刷出当前积累的表格行
            void FlushTable()
            {
                if (tableLines.Count > 0)
                {
                    if (IsValidTable(tableLines))
                    {
                        FlushText();
                        blocks.Add(new TableBlock(tableLines, role));
                    }
                    else
                    {
                        textLines.AddRange(tableLines);
                    }
                    tableLines.Clear();
                }
            }

            // 辅助：刷出当前积累的引用行
            void FlushQuote()
            {
                if (quoteLines.Count > 0)
                {
                    FlushText();
                    blocks.Add(new BlockquoteBlock(string.Join("\n", quoteLines), role));
                    quoteLines.Clear();
                }
            }

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');

                // ── 围栏代码块检测（``` 开头） ──
                if (line.StartsWith("```"))
                {
                    if (!inCodeBlock)
                    {
                        // 代码块开始：刷出之前所有积累的内容
                        FlushTable();
                        FlushQuote();
                        FlushText();
                        codeLanguage = line.Length > 3 ? line.Substring(3).Trim() : "";
                        inCodeBlock = true;
                        codeLines.Clear();
                    }
                    else
                    {
                        // 代码块结束
                        blocks.Add(new FencedCodeBlock(string.Join("\n", codeLines), codeLanguage));
                        inCodeBlock = false;
                        codeLines.Clear();
                    }
                    continue;
                }

                // 在代码块内部，直接收集行
                if (inCodeBlock)
                {
                    codeLines.Add(line);
                    continue;
                }

                // ── 引用块检测: > 开头的行 ──
                bool isQuote = line.StartsWith("> ") || line == ">";
                if (isQuote)
                {
                    FlushTable();
                    string stripped = line.Length > 2 ? line.Substring(2) : "";
                    quoteLines.Add(stripped);
                    continue;
                }

                // 非引用行 → 刷出之前积累的引用块
                FlushQuote();

                if (IsTableLine(line))
                {
                    tableLines.Add(line);
                }
                else
                {
                    FlushTable();
                    textLines.Add(line);
                }
            }

            // 处理末尾剩余
            // 未闭合的代码块也输出
            if (inCodeBlock && codeLines.Count > 0)
            {
                FlushText();
                blocks.Add(new FencedCodeBlock(string.Join("\n", codeLines), codeLanguage));
            }

            FlushTable();
            FlushQuote();

            if (textLines.Count > 0)
                blocks.Add(new TextBlock(string.Join("\n", textLines), role));

            return blocks;
        }

        /// <summary>添加可折叠思考块</summary>
        public ThinkBlock AddThink(string text)
        {
            var block = new ThinkBlock(text);
            // 在文本块之后插入
            int insertIdx = _blocks.FindIndex(b => !(b is TextBlock)) ;
            if (insertIdx < 0) insertIdx = _blocks.Count;
            _blocks.Insert(insertIdx, block);
            block.ToggleRequested += () => RequestLayout();
            RequestLayout();
            return block;
        }

        /// <summary>添加工具调用卡片</summary>
        public ToolCallCard AddToolCall(string toolName, string statusText)
        {
            var card = new ToolCallCard(toolName, statusText, ToolCallStatus.Running);
            card.ToggleRequested += () => RequestLayout();
            _blocks.Add(card);
            RequestLayout();
            return card;
        }

        /// <summary>添加子智能体执行块（展示子智能体的完整执行过程）</summary>
        public SubAgentBlock AddSubAgent(string taskName)
        {
            var block = new SubAgentBlock(taskName);
            block.ToggleRequested += () => RequestLayout();
            _blocks.Add(block);
            RequestLayout();
            return block;
        }

        /// <summary>添加操作审批确认卡片，返回卡片实例（通过 ResultTask 等待用户决策）</summary>
        internal ApprovalCard AddApprovalCard(string toolDisplayName, string functionName, string summary)
        {
            var card = new ApprovalCard(toolDisplayName, functionName, summary);
            card.ToggleRequested += () => RequestLayout();
            _blocks.Add(card);
            RequestLayout();
            return card;
        }

        /// <summary>添加用户问答卡片，返回卡片实例（通过 ResultTask 等待用户回答）</summary>
        internal AskUserCard AddAskUserCard(string question, List<AskUserOption> options, bool allowFreeInput)
        {
            var card = new AskUserCard(question, options, allowFreeInput);
            card.ToggleRequested += () => RequestLayout();

            // 如果允许自由输入，嵌入 TextBox 和提交按钮
            if (allowFreeInput)
            {
                var inputBox = new AntdUI.Input
                {
                    Font = new Font("Microsoft YaHei UI", 9F),
                    PlaceholderText = "输入自定义回答...",
                    BorderWidth = 1,
                    BorderColor = Color.FromArgb(180, 198, 230),
                    BackColor = Color.FromArgb(249, 250, 251),
                    ForeColor = Color.FromArgb(55, 65, 81),
                    Radius = 6,
                    Visible = false
                };

                var submitBtn = new AntdUI.Button
                {
                    Text = "提交",
                    Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold),
                    Type = AntdUI.TTypeMini.Primary,
                    Radius = 6,
                    Visible = false
                };

                submitBtn.Click += (s, ev) =>
                {
                    card.SubmitFreeText(inputBox.Text);
                };

                inputBox.KeyDown += (s, ev) =>
                {
                    if (ev.KeyCode == Keys.Enter)
                    {
                        ev.SuppressKeyPress = true;
                        card.SubmitFreeText(inputBox.Text);
                    }
                };

                card.EmbeddedInput = inputBox;
                card.EmbeddedSubmitBtn = submitBtn;
                this.Controls.Add(inputBox);
                this.Controls.Add(submitBtn);
            }

            _blocks.Add(card);
            RequestLayout();
            return card;
        }

        /// <summary>获取第一个工具调用卡片</summary>
        public ToolCallCard GetToolCall()
        {
            return _blocks.OfType<ToolCallCard>().FirstOrDefault();
        }

        /// <summary>通知内容已更新，重新布局和绘制</summary>
        public void NotifyContentChanged()
        {
            RequestLayout();
        }

        /// <summary>开始新的文本段落（用于工具调用后继续流式输出，不创建新消息）</summary>
        public void PrepareNewStreamSection(string placeholder = "🤔 正在思考中...")
        {
            _blocks.Add(new TextBlock(placeholder, Role));
            RequestLayout();
        }

        // ── 布局计算 ──

        private int BubbleContentWidth()
        {
            int maxBubble = (int)(MaxBubbleWidth * 0.80);
            return Math.Max(120, maxBubble - AvatarSize - AvatarGap - BubblePadH * 2);
        }

        internal void RecalcLayout()
        {
            if (!IsHandleCreated) return;
            try
            {
                using (var g = CreateGraphics())
                {
                    int contentW = BubbleContentWidth();
                    EnsureMeasureCache(g, contentW);

                    int bubbleH = _cachedTotalContentHeight + BubblePadV * 2;
                    int newH = NameHeight + bubbleH + 6;
                    if (Height != newH)
                        Height = newH;
                }
            }
            catch { /* 控件尚未就绪 */ }
        }

        private void RequestLayout()
        {
            if (!IsHandleCreated) { Invalidate(); return; }

            try
            {
                MarkMeasureDirty();
                RecalcLayout();
                LayoutChanged?.Invoke();
                Invalidate();
            }
            catch { Invalidate(); }
        }

        // ── 绘制 ──

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int contentW = BubbleContentWidth();
            EnsureMeasureCache(g, contentW);

            int bubbleW = Math.Max(Radius * 2 + 2, contentW + BubblePadH * 2);
            int bubbleH = Math.Max(Radius * 2 + 2, _cachedTotalContentHeight + BubblePadV * 2);
            bool isUser = Role == ChatRole.User;

            // 计算位置
            int avatarX, bubbleX;
            if (isUser)
            {
                avatarX = Width - AvatarSize - 6;
                bubbleX = avatarX - AvatarGap - bubbleW;
            }
            else
            {
                avatarX = 6;
                bubbleX = avatarX + AvatarSize + AvatarGap;
            }

            int bubbleY = NameHeight;

            // ── 绘制头像（圆形裁剪）──
            if (Avatar != null)
            {
                var avatarRect = new Rectangle(avatarX, bubbleY + 4, AvatarSize, AvatarSize);
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(avatarRect);
                    var oldClip = g.Clip;
                    g.SetClip(path);
                    g.DrawImage(Avatar, avatarRect);
                    g.Clip = oldClip;
                }
            }

            // ── 绘制气泡阴影 ──
            var shadowRect = new Rectangle(bubbleX + 1, bubbleY + 2, bubbleW, bubbleH);
            using (var shadowPath = CreateRoundedRect(shadowRect, Radius))
            using (var shadowBrush = new SolidBrush(ShadowColor))
                g.FillPath(shadowBrush, shadowPath);

            // ── 绘制气泡背景 ──
            var bubbleRect = new Rectangle(bubbleX, bubbleY, bubbleW, bubbleH);
            using (var path = CreateRoundedRect(bubbleRect, Radius))
            {
                using (var brush = new SolidBrush(isUser ? UserBg : AIBg))
                    g.FillPath(brush, path);
                if (!isUser)
                    using (var pen = new Pen(AIBorder))
                        g.DrawPath(pen, path);
            }

            // ── 绘制内容块 ──
            int blockX = bubbleX + BubblePadH;
            int blockY = bubbleY + BubblePadV;
            for (int i = 0; i < _blocks.Count; i++)
            {
                var block = _blocks[i];
                int h = i < _cachedBlockHeights.Count ? _cachedBlockHeights[i] : 0;
                var blockRect = new Rectangle(blockX, blockY, contentW, h);
                block.Paint(g, blockRect, isUser);
                blockY += h + BlockGap;
            }
        }

        // ── 鼠标交互 ──

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int contentW = BubbleContentWidth();
            using (var g = CreateGraphics())
            {
                EnsureMeasureCache(g, contentW);
            }

            if (TryFindBlock(e.Location, contentW, out var block, out var blockRect))
            {
                block.OnClick(e.Location, blockRect);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (e.Button != MouseButtons.None)
            {
                if (_lastHandCursor)
                {
                    Cursor = Cursors.Default;
                    _lastHandCursor = false;
                }
                return;
            }

            int contentW = BubbleContentWidth();

            if (_measureDirty || _cachedMeasuredWidth != contentW)
            {
                using (var g = CreateGraphics())
                {
                    EnsureMeasureCache(g, contentW);
                }
            }

            if (!_containsInteractiveBlocks)
            {
                if (_lastHandCursor)
                {
                    Cursor = Cursors.Default;
                    _lastHandCursor = false;
                }
                return;
            }

            // 思考块头部显示手型光标
            bool hand = false;

            if (TryFindBlock(e.Location, contentW, out var block, out var blockRect))
            {
                hand = block.HitTest(e.Location, blockRect);

                // AskUserCard 需要更新 hover 状态以高亮选项
                if (block is AskUserCard askCard)
                {
                    if (askCard.UpdateHover(e.Location, blockRect))
                        Invalidate();
                }
            }

            if (hand != _lastHandCursor)
            {
                Cursor = hand ? Cursors.Hand : Cursors.Default;
                _lastHandCursor = hand;
            }
        }

        private void MarkMeasureDirty()
        {
            _measureDirty = true;
            _cachedMeasuredWidth = -1;
            _cachedTotalContentHeight = 0;
            _cachedBlockHeights.Clear();
            _containsInteractiveBlocks = false;
        }

        private void EnsureMeasureCache(Graphics g, int contentW)
        {
            if (!_measureDirty && _cachedMeasuredWidth == contentW)
                return;

            _cachedBlockHeights.Clear();
            _cachedTotalContentHeight = 0;
            _containsInteractiveBlocks = false;

            foreach (var block in _blocks)
            {
                int height = block.MeasureHeight(g, contentW);
                _cachedBlockHeights.Add(height);
                _cachedTotalContentHeight += height + BlockGap;
                if (!_containsInteractiveBlocks && (block is ThinkBlock || block is ToolCallCard || block is SubAgentBlock || block is ApprovalCard || block is AskUserCard))
                    _containsInteractiveBlocks = true;
            }

            if (_cachedTotalContentHeight > 0)
                _cachedTotalContentHeight -= BlockGap;

            _cachedMeasuredWidth = contentW;
            _measureDirty = false;
        }

        private bool TryFindBlock(Point location, int contentW, out IContentBlock hitBlock, out Rectangle hitRect)
        {
            hitBlock = null;
            hitRect = Rectangle.Empty;

            bool isUser = Role == ChatRole.User;
            int bubbleW = contentW + BubblePadH * 2;
            int bubbleX = isUser
                ? Width - AvatarSize - 6 - AvatarGap - bubbleW
                : 6 + AvatarSize + AvatarGap;

            int blockX = bubbleX + BubblePadH;
            int blockY = NameHeight + BubblePadV;

            for (int i = 0; i < _blocks.Count; i++)
            {
                int height = i < _cachedBlockHeights.Count ? _cachedBlockHeights[i] : 0;
                var rect = new Rectangle(blockX, blockY, contentW, height);
                if (rect.Contains(location))
                {
                    hitBlock = _blocks[i];
                    hitRect = rect;
                    return true;
                }
                blockY += height + BlockGap;
            }

            return false;
        }

        // ── 工具方法 ──

        private GraphicsPath CreateRoundedRect(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            // 安全检查：矩形太小时缩小圆角半径，避免 GDI+ ArgumentException
            if (r.Width < 1 || r.Height < 1)
            {
                p.AddRectangle(r);
                return p;
            }
            radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
            if (radius < 1)
            {
                p.AddRectangle(r);
                return p;
            }
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  RichChatPanel — 富消息聊天面板主容器
    // ════════════════════════════════════════════════════════════════

    public class RichChatPanel : System.Windows.Forms.Panel
    {
        private const int WM_MOUSEWHEEL = 0x020A;

        private readonly System.Windows.Forms.Panel _container;
        private readonly List<MessageGroup> _messages = new List<MessageGroup>();

        public RichChatPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer, true);
            AutoScroll = true;
            BackColor = Color.FromArgb(245, 247, 250);

            _container = new System.Windows.Forms.Panel
            {
                Location = Point.Empty,
                BackColor = Color.FromArgb(245, 247, 250),
                AutoSize = false
            };
            Controls.Add(_container);

            Resize += (s, e) => PerformRelayout();
        }

        protected override void WndProc(ref System.Windows.Forms.Message m)
        {
            // 捕获鼠标滚轮消息并处理滚动
            if (m.Msg == WM_MOUSEWHEEL)
            {
                int delta = (short)((long)m.WParam >> 16);
                int currentY = -AutoScrollPosition.Y;
                int maxY = Math.Max(0, _container.Height - ClientSize.Height);
                int lines = SystemInformation.MouseWheelScrollLines > 0 ? SystemInformation.MouseWheelScrollLines : 3;
                int scrollAmount = (delta / 120) * lines * 14;
                if (scrollAmount == 0 && delta != 0)
                    scrollAmount = delta > 0 ? lines * 14 : -lines * 14;
                int newY = Math.Max(0, Math.Min(maxY, currentY - scrollAmount));
                AutoScrollPosition = new Point(0, newY);
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        // ═══ 公共 API ═══

        /// <summary>添加一条消息，返回 MessageGroup 供后续操作</summary>
        public MessageGroup AddMessage(string name, Image avatar, ChatRole role, string text = null)
        {
            var msg = new MessageGroup(name, avatar, role, ContentWidth);
            if (!string.IsNullOrEmpty(text))
                msg.SetText(text);
            _messages.Add(msg);
            msg.LayoutChanged += PerformRelayout;
            _container.Controls.Add(msg);
            PerformRelayout();
            ScrollToEnd();
            return msg;
        }

        /// <summary>获取最后一条 AI 消息</summary>
        public MessageGroup LastAIMessage
        {
            get
            {
                for (int i = _messages.Count - 1; i >= 0; i--)
                    if (_messages[i].Role == ChatRole.AI) return _messages[i];
                return null;
            }
        }

        /// <summary>获取最后一条消息（不分角色）</summary>
        public MessageGroup LastMessage
        {
            get { return _messages.Count > 0 ? _messages[_messages.Count - 1] : null; }
        }

        /// <summary>消息数量</summary>
        public int MessageCount => _messages.Count;

        /// <summary>清空所有消息</summary>
        public void ClearAll()
        {
            _messages.Clear();
            _container.Controls.Clear();
            _container.Height = 0;
        }

        /// <summary>滚动到底部</summary>
        public void ScrollToEnd()
        {
            try
            {
                if (_container.Height > ClientSize.Height)
                    AutoScrollPosition = new Point(0, _container.Height);
            }
            catch { /* ignore */ }
        }

        // ═══ 内部布局 ═══

        private int ContentWidth => Math.Max(200, ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8);

        private void PerformRelayout()
        {
            if (_messages.Count == 0) return;

            _container.SuspendLayout();
            int w = ContentWidth;
            int y = 12;
            foreach (var msg in _messages)
            {
                msg.MaxBubbleWidth = w;
                msg.Width = w;
                msg.RecalcLayout();
                msg.Location = new Point(4, y);
                y += msg.Height + 8;
            }
            _container.Size = new Size(w + 8, y + 12);
            _container.ResumeLayout(false);
            _container.Invalidate();
            Invalidate();
        }
    }
}
