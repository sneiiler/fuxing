using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using AntdUI;
using FuXingAgent.Core;

namespace FuXingAgent.UI
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

    internal static class BlockDpi
    {
        public static int S(Graphics g, int value)
        {
            if (value <= 0) return 0;
            float scale = 1f;
            if (g != null)
            {
                scale = g.DpiX / 96f;
                if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0f)
                    scale = 1f;
            }
            scale = Math.Max(1f, Math.Min(3f, scale));
            return Math.Max(1, (int)Math.Round(value * scale, MidpointRounding.AwayFromZero));
        }
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
            int padLeft = BlockDpi.S(g, PadLeft);
            int padVert = BlockDpi.S(g, PadVert);
            int rightPad = BlockDpi.S(g, 10);
            var canvas = g.High();
            var size = canvas.MeasureText(Text, QuoteFont, width - padLeft - rightPad, FormatFlags.Top | FormatFlags.Left);
            return size.Height + padVert * 2;
        }

        public void Paint(Graphics g, Rectangle bounds, bool isUser)
        {
            if (string.IsNullOrEmpty(Text)) return;
            int radius = BlockDpi.S(g, Radius);
            int leftBarWidth = BlockDpi.S(g, LeftBarWidth);
            int padLeft = BlockDpi.S(g, PadLeft);
            int padVert = BlockDpi.S(g, PadVert);
            int rightPad = BlockDpi.S(g, 10);
            int topGap = BlockDpi.S(g, 4);
            int lineOffset = BlockDpi.S(g, 1);

            // 圆角背景
            using (var path = RoundedRect(bounds, radius))
            using (var brush = new SolidBrush(QuoteBgColor))
                g.FillPath(brush, path);

            // 左侧蓝色竖线
            using (var pen = new Pen(QuoteBorderColor, leftBarWidth))
                g.DrawLine(pen, bounds.X + leftBarWidth / 2 + lineOffset, bounds.Y + topGap,
                               bounds.X + leftBarWidth / 2 + lineOffset, bounds.Bottom - topGap);

            // 文字
            var canvas = g.High();
            var textRect = new Rectangle(bounds.X + padLeft, bounds.Y + padVert,
                                         bounds.Width - padLeft - rightPad, bounds.Height - padVert * 2);
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
            int padX = BlockDpi.S(g, PadX);
            int padY = BlockDpi.S(g, PadY);
            int langH = BlockDpi.S(g, LangH);
            var canvas = g.High();
            int langExtra = string.IsNullOrEmpty(Language) ? 0 : langH;
            var size = canvas.MeasureText(Code, CodeFont, width - padX * 2, FormatFlags.Top | FormatFlags.Left);
            return size.Height + padY * 2 + langExtra;
        }

        public void Paint(Graphics g, Rectangle bounds, bool isUser)
        {
            if (string.IsNullOrEmpty(Code)) return;
            int padX = BlockDpi.S(g, PadX);
            int padY = BlockDpi.S(g, PadY);
            int langH = BlockDpi.S(g, LangH);
            int radius = BlockDpi.S(g, Radius);
            int topGap = BlockDpi.S(g, 4);
            int langTextH = BlockDpi.S(g, 16);
            var canvas = g.High();
            int langExtra = string.IsNullOrEmpty(Language) ? 0 : langH;

            // 圆角背景
            using (var path = RoundedRect(bounds, radius))
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
                    new Rectangle(bounds.X + padX, bounds.Y + topGap, bounds.Width - padX * 2, langTextH),
                    FormatFlags.Top | FormatFlags.Left);
            }

            // 代码文本
            canvas.DrawText(Code, CodeFont, CodeFg,
                new Rectangle(bounds.X + padX, bounds.Y + padY + langExtra,
                              bounds.Width - padX * 2, bounds.Height - padY * 2 - langExtra),
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
            int cellPadH = BlockDpi.S(g, CellPadH);
            int minColW = BlockDpi.S(g, 40);
            int minScaledColW = BlockDpi.S(g, 30);
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
                    idealWidths[c] = Math.Max(idealWidths[c], size.Width + cellPadH * 2);
                }
            }
            for (int c = 0; c < cols; c++)
                idealWidths[c] = Math.Max(idealWidths[c], minColW);

            int totalIdeal = idealWidths.Sum();
            if (totalIdeal <= availableWidth)
                return idealWidths;

            // 超宽时按比例缩放
            var widths = new int[cols];
            for (int c = 0; c < cols; c++)
                widths[c] = Math.Max(minScaledColW, (int)(idealWidths[c] * (double)availableWidth / totalIdeal));
            return widths;
        }

        private int RowHeight(Graphics g, int[] colWidths, int rowIdx)
        {
            if (rowIdx >= _rows.Length) return 0;
            int cellPadH = BlockDpi.S(g, CellPadH);
            int cellPadV = BlockDpi.S(g, CellPadV);
            var canvas = g.High();
            var row = _rows[rowIdx];
            int maxH = 0;
            for (int c = 0; c < colWidths.Length && c < row.Length; c++)
            {
                var font = (rowIdx == 0) ? HeaderFont : CellFont;
                int textW = Math.Max(BlockDpi.S(g, 10), colWidths[c] - cellPadH * 2);
                var size = canvas.MeasureText(row[c], font, textW, FormatFlags.Top | FormatFlags.Left);
                maxH = Math.Max(maxH, size.Height);
            }
            return maxH + cellPadV * 2;
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
            int cellPadH = BlockDpi.S(g, CellPadH);
            int cellPadV = BlockDpi.S(g, CellPadV);
            int minTextW = BlockDpi.S(g, 10);
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
                        int textW = Math.Max(minTextW, colWidths[c] - cellPadH * 2);
                        var cellRect = new Rectangle(x + cellPadH, y + cellPadV, textW, rowH - cellPadV * 2);
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
        private int _cachedHeaderHeight = HeaderHeight;

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
            int headerHeight = BlockDpi.S(g, HeaderHeight);
            _cachedHeaderHeight = headerHeight;
            if (!IsExpanded) return headerHeight;
            var canvas = g.High();
            var size = canvas.MeasureText(Text ?? "", ContentFont, width - BlockDpi.S(g, 20), FormatFlags.Top | FormatFlags.Left);
            return headerHeight + size.Height + BlockDpi.S(g, 16);
        }

        public void Paint(Graphics g, Rectangle bounds, bool isUser)
        {
            int cornerRadius = BlockDpi.S(g, 8);
            int lineWidth = BlockDpi.S(g, 2);
            int padX = BlockDpi.S(g, 12);
            int sideTextPad = BlockDpi.S(g, 24);
            int topGap = BlockDpi.S(g, 8);
            int contentTopGap = BlockDpi.S(g, 4);
            int contentBottomGap = BlockDpi.S(g, 8);
            int headerHeight = BlockDpi.S(g, HeaderHeight);
            _cachedHeaderHeight = headerHeight;
            // 圆角背景
            using (var path = RoundedRect(bounds, cornerRadius))
            {
                using (var brush = new SolidBrush(HeaderBg))
                    g.FillPath(brush, path);
                using (var pen = new Pen(BorderColor))
                    g.DrawPath(pen, path);
            }

            // 左侧装饰线
            using (var pen = new Pen(Color.FromArgb(156, 163, 175), lineWidth))
                g.DrawLine(pen, bounds.X + BlockDpi.S(g, 4), bounds.Y + topGap, bounds.X + BlockDpi.S(g, 4), bounds.Bottom - topGap);

            var canvas = g.High();
            {
                // 头部: 💭 emoji + 文字 + 展开/折叠指示
                string chevron = IsExpanded ? "▾" : "▸";
                string headerText = $"{chevron}  💭 思考过程";
                canvas.DrawText(headerText, HeaderFont, HeaderFg,
                    new Rectangle(bounds.X + padX, bounds.Y, bounds.Width - sideTextPad, headerHeight),
                    FormatFlags.VerticalCenter | FormatFlags.Left);

                // 折叠时显示省略提示
                if (!IsExpanded)
                {
                    using (var hintFont = new Font("Microsoft YaHei UI", 7.5F))
                        canvas.DrawText("点击展开", hintFont, Color.FromArgb(156, 163, 175),
                            new Rectangle(bounds.X + padX, bounds.Y, bounds.Width - sideTextPad, headerHeight),
                            FormatFlags.VerticalCenter | FormatFlags.Right);
                }

                // 展开时显示内容
                if (IsExpanded && !string.IsNullOrEmpty(Text))
                {
                    // 分隔线
                    int sepY = bounds.Y + headerHeight;
                    using (var pen = new Pen(BorderColor))
                        g.DrawLine(pen, bounds.X + padX, sepY, bounds.Right - padX, sepY);

                    var contentRect = new Rectangle(bounds.X + padX, sepY + contentTopGap, bounds.Width - sideTextPad, bounds.Bottom - sepY - contentBottomGap);
                    canvas.DrawText(Text, ContentFont, ContentFg, contentRect,
                        FormatFlags.Top | FormatFlags.Left);
                }
            }
        }

        public bool HitTest(Point pt, Rectangle bounds)
        {
            return pt.Y <= bounds.Y + _cachedHeaderHeight;
        }

        public void OnClick(Point pt, Rectangle bounds)
        {
            if (pt.Y <= bounds.Y + _cachedHeaderHeight)
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
    //  加载指示器 — thinking.gif + 随机提示文字
    // ════════════════════════════════════════════════════════════════

    internal class ThinkingIndicatorBlock : IContentBlock, IDisposable
    {
        private static readonly string[] Tips =
        {
            "正在思考中...",
            "让我想想...",
            "AI 正在组织语言...",
            "稍等，灵感就来...",
            "正在分析你的问题...",
            "正在查阅相关资料...",
            "思路整理中...",
            "马上就好...",
            "正在为你撰写回复...",
            "AI 大脑高速运转中...",
        };

        private static readonly Font TipFont = new Font("Microsoft YaHei UI", 10.5F);
        private static readonly Color TipColor = Color.FromArgb(75, 85, 99);
        private const int GifSize = 40;
        private const int Gap = 10;
        private const int MinRowHeight = 44;

        private readonly string _tipText;
        private Image _gif;
        private bool _animating;
        private Control _owner;

        public ThinkingIndicatorBlock()
        {
            _tipText = Tips[new Random().Next(Tips.Length)];
            LoadGif();
        }

        private void LoadGif()
        {
            try
            {
                var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var gifPath = Path.Combine(dir, "Resources", "thinking.gif");
                if (File.Exists(gifPath))
                    _gif = Image.FromFile(gifPath);
            }
            catch { /* 静默：缺少 GIF 时仅显示文字 */ }
        }

        public void StartAnimation(Control owner)
        {
            _owner = owner;
            if (_gif != null && ImageAnimator.CanAnimate(_gif) && !_animating)
            {
                ImageAnimator.Animate(_gif, OnFrameChanged);
                _animating = true;
            }
        }

        public void StopAnimation()
        {
            if (_gif != null && _animating)
            {
                ImageAnimator.StopAnimate(_gif, OnFrameChanged);
                _animating = false;
            }
            _owner = null;
        }

        private void OnFrameChanged(object sender, EventArgs e)
        {
            try { _owner?.Invalidate(); }
            catch { /* 控件可能已释放 */ }
        }

        public int MeasureHeight(Graphics g, int width)
        {
            var canvas = g.High();
            int tipW = Math.Max(24, width - GifSize - Gap);
            var tipSize = canvas.MeasureText(_tipText, TipFont, tipW, FormatFlags.Left | FormatFlags.VerticalCenter);
            return Math.Max(MinRowHeight, Math.Max(GifSize, tipSize.Height));
        }

        public void Paint(Graphics g, Rectangle bounds, bool isUser)
        {
            int rowHeight = Math.Max(MinRowHeight, bounds.Height);
            int y = bounds.Y + (rowHeight - GifSize) / 2;

            if (_gif != null)
            {
                ImageAnimator.UpdateFrames(_gif);
                g.DrawImage(_gif, bounds.X, y, GifSize, GifSize);
            }

            var canvas = g.High();
            int textX = bounds.X + (_gif != null ? GifSize + Gap : 0);
            int textW = Math.Max(0, bounds.Right - textX);
            if (textW <= 0) return;
            canvas.DrawText(_tipText, TipFont, TipColor,
                new Rectangle(textX, bounds.Y, textW, rowHeight),
                FormatFlags.VerticalCenter | FormatFlags.Left);
        }

        public bool HitTest(Point pt, Rectangle bounds) => false;
        public void OnClick(Point pt, Rectangle bounds) { }

        public void Dispose()
        {
            StopAnimation();
            _gif?.Dispose();
            _gif = null;
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
        private int _cachedHeaderHeight = HeaderH;

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
            return name ?? "";
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
            int headerH = BlockDpi.S(g, HeaderH);
            _cachedHeaderHeight = headerH;
            int detailLineH = BlockDpi.S(g, DetailLineH);
            int sidePad = BlockDpi.S(g, 24);
            if (!IsExpanded) return headerH;
            int h = headerH;
            int contentW = width - sidePad;
            var canvas = g.High();

            // 代码块
            if (!string.IsNullOrEmpty(CodeSnippet))
            {
                h += BlockDpi.S(g, 4); // 间距
                var codeSize = canvas.MeasureText(CodeSnippet, CodeFont, contentW - BlockDpi.S(g, 16), FormatFlags.Top | FormatFlags.Left);
                h += codeSize.Height + BlockDpi.S(g, 16); // 上下各 8px 内边距
            }

            // 状态文本
            if (!string.IsNullOrEmpty(StatusText))
            {
                var size = canvas.MeasureText(StatusText, StatusFont, contentW, FormatFlags.Top | FormatFlags.Left);
                h += Math.Max(detailLineH, size.Height) + BlockDpi.S(g, 4);
            }
            if (Details.Count > 0)
                h += Details.Count * detailLineH + BlockDpi.S(g, 4);
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

        public bool HitTest(Point pt, Rectangle bounds) => pt.Y <= bounds.Y + _cachedHeaderHeight;

        public void OnClick(Point pt, Rectangle bounds)
        {
            if (pt.Y <= bounds.Y + _cachedHeaderHeight)
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
        private int _cachedHeaderHeight = HeaderH;

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

        /// <summary>将函数名翻译为中文显示名</summary>
        private static string TranslateToolName(string name)
        {
            return name ?? "";
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
            int headerH = BlockDpi.S(g, HeaderH);
            int stepLineH = BlockDpi.S(g, StepLineH);
            _cachedHeaderHeight = headerH;
            if (!IsExpanded) return headerH;
            int h = headerH + BlockDpi.S(g, 4); // 4px padding after header separator
            foreach (var step in _steps)
                h += stepLineH;
            return Math.Max(headerH, h);
        }

        public void Paint(Graphics g, Rectangle bounds, bool isUser)
        {
            int headerH = BlockDpi.S(g, HeaderH);
            int stepLineH = BlockDpi.S(g, StepLineH);
            int cornerRadius = BlockDpi.S(g, 8);
            int lineWidth = BlockDpi.S(g, 2);
            int topGap = BlockDpi.S(g, 8);
            int sidePad = BlockDpi.S(g, 12);
            int sidePadWide = BlockDpi.S(g, 24);
            int stepSidePad = BlockDpi.S(g, 16);
            int stepSidePadWide = BlockDpi.S(g, 32);
            _cachedHeaderHeight = headerH;
            // 圆角背景
            using (var path = RoundedRect(bounds, cornerRadius))
            {
                using (var brush = new SolidBrush(CardBg))
                    g.FillPath(brush, path);
                using (var pen = new Pen(CardBorder))
                    g.DrawPath(pen, path);
            }



            var canvas = g.High();

            // ── 头部 ──
            string chevron = IsExpanded ? "▾" : "▸";
            string statusIcon = Status == ToolCallStatus.Success ? "✅"
                              : Status == ToolCallStatus.Error ? "❌" : "⏳";
            string statusLabel = Status == ToolCallStatus.Success ? "完成"
                               : Status == ToolCallStatus.Error ? "失败" : "执行中";

            canvas.DrawText($"{chevron}  🤖 子智能体：{TaskName}", HeaderFont, HeaderFg,
                new Rectangle(bounds.X + sidePad, bounds.Y, bounds.Width - sidePadWide, headerH),
                FormatFlags.VerticalCenter | FormatFlags.Left);

            canvas.DrawText($"{statusIcon} {statusLabel}", HeaderFont,
                Status == ToolCallStatus.Success ? SuccessColor
                : Status == ToolCallStatus.Error ? ErrorColor : RunningColor,
                new Rectangle(bounds.X + sidePad, bounds.Y, bounds.Width - sidePadWide, headerH),
                FormatFlags.VerticalCenter | FormatFlags.Right);

            if (!IsExpanded) return;

            // ── 分隔线 ──
            int sepY = bounds.Y + headerH;
            using (var pen = new Pen(CardBorder))
                g.DrawLine(pen, bounds.X + sidePad, sepY, bounds.Right - sidePad, sepY);

            // ── 步骤列表 ──
            int y = sepY + BlockDpi.S(g, 4);
            foreach (var step in _steps)
            {
                var rect = new Rectangle(bounds.X + stepSidePad, y, bounds.Width - stepSidePadWide, stepLineH);

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

                y += stepLineH;
            }
        }

        public bool HitTest(Point pt, Rectangle bounds) => pt.Y <= bounds.Y + _cachedHeaderHeight;

        public void OnClick(Point pt, Rectangle bounds)
        {
            if (pt.Y <= bounds.Y + _cachedHeaderHeight)
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
    //  工作流卡片 — 嵌入聊天消息的工作流执行进度
    // ════════════════════════════════════════════════════════════════

    /// <summary>工作流步骤记录</summary>
    internal class WorkflowStep
    {
        public int StepIndex;
        public string StepName;
        public string Description;
        public bool IsCompleted;
        public bool Success;
    }

    /// <summary>
    /// 工作流执行进度卡片 — 嵌入 AI 消息内，展示工作流名称、各步骤状态及最终结果。
    /// </summary>
    public class WorkflowBlock : IContentBlock
    {
        public string WorkflowName { get; set; }
        public string WorkflowDisplayName { get; set; }
        public int TotalSteps { get; set; }
        public bool? Finished { get; private set; }
        public bool? Success { get; private set; }
        public bool IsExpanded { get; set; }
        internal event Action ToggleRequested;
        private int _cachedHeaderHeight = HeaderH;

        private readonly Dictionary<int, WorkflowStep> _steps = new Dictionary<int, WorkflowStep>();

        // 布局常量
        private const int HeaderH = 28;
        private const int StepLineH = 22;
        private const int DescLineH = 18;

        // 字体
        private static readonly Font HeaderFont = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
        private static readonly Font StepFont = new Font("Microsoft YaHei UI", 8.5F);
        private static readonly Font DescFont = new Font("Microsoft YaHei UI", 8F);

        // 颜色
        private static readonly Color CardBg = Color.FromArgb(243, 244, 246);
        private static readonly Color CardBorder = Color.FromArgb(229, 231, 235);
        private static readonly Color HeaderFg = Color.FromArgb(107, 114, 128);
        private static readonly Color ContentFg = Color.FromArgb(75, 85, 99);
        private static readonly Color DescFg = Color.FromArgb(156, 163, 175);
        private static readonly Color RunningColor = Color.FromArgb(59, 130, 246);
        private static readonly Color SuccessColor = Color.FromArgb(22, 163, 74);
        private static readonly Color ErrorColor = Color.FromArgb(220, 38, 38);
        private static readonly Color DotBg = Color.White;

        public WorkflowBlock(string workflowName, string workflowDisplayName, int totalSteps)
        {
            WorkflowName = workflowName;
            WorkflowDisplayName = string.IsNullOrWhiteSpace(workflowDisplayName) ? workflowName : workflowDisplayName;
            TotalSteps = totalSteps;
            IsExpanded = true;
        }

        /// <summary>更新步骤状态</summary>
        public void UpdateStep(int stepIndex, string stepName, string description, bool isCompleted, bool success)
        {
            if (!_steps.TryGetValue(stepIndex, out var step))
            {
                step = new WorkflowStep { StepIndex = stepIndex };
                _steps[stepIndex] = step;
            }
            step.StepName = stepName ?? step.StepName ?? $"Step {stepIndex}";
            step.Description = description ?? step.Description;
            step.IsCompleted = isCompleted;
            step.Success = success;
            ToggleRequested?.Invoke();
        }

        /// <summary>标记工作流完成</summary>
        public void SetFinished(bool success, string summary)
        {
            Finished = true;
            Success = success;
            if (!string.IsNullOrWhiteSpace(summary))
                WorkflowDisplayName = $"{WorkflowDisplayName}";
            ToggleRequested?.Invoke();
        }

        // ── IContentBlock ──

        public int MeasureHeight(Graphics g, int width)
        {
            int headerH = BlockDpi.S(g, HeaderH);
            _cachedHeaderHeight = headerH;
            if (!IsExpanded) return headerH;

            int stepLineH = BlockDpi.S(g, StepLineH);
            int descLineH = BlockDpi.S(g, DescLineH);
            int h = headerH + BlockDpi.S(g, 4);

            // 按 stepIndex 排序的步骤
            var sorted = new List<WorkflowStep>(_steps.Values);
            sorted.Sort((a, b) => a.StepIndex.CompareTo(b.StepIndex));
            foreach (var step in sorted)
            {
                h += stepLineH;
                if (!string.IsNullOrEmpty(step.Description))
                    h += descLineH;
            }
            return Math.Max(headerH, h + BlockDpi.S(g, 4));
        }

        public void Paint(Graphics g, Rectangle bounds, bool isUser)
        {
            int headerH = BlockDpi.S(g, HeaderH);
            int stepLineH = BlockDpi.S(g, StepLineH);
            int descLineH = BlockDpi.S(g, DescLineH);
            int cornerRadius = BlockDpi.S(g, 8);
            int lineWidth = BlockDpi.S(g, 2);
            int topGap = BlockDpi.S(g, 8);
            int sidePad = BlockDpi.S(g, 12);
            int sidePadWide = BlockDpi.S(g, 24);
            int dotSize = BlockDpi.S(g, 8);
            int dotX = bounds.X + BlockDpi.S(g, 20);
            _cachedHeaderHeight = headerH;

            // 圆角背景
            using (var path = RoundedRect(bounds, cornerRadius))
            {
                using (var brush = new SolidBrush(CardBg))
                    g.FillPath(brush, path);
                using (var pen = new Pen(CardBorder))
                    g.DrawPath(pen, path);
            }



            var canvas = g.High();

            // ── 头部 ──
            string chevron = IsExpanded ? "▾" : "▸";
            bool done = Finished == true;
            bool ok = Success == true;
            string statusLabel = done ? (ok ? "Completed" : "Failed") : "Running";
            var statusColor = done ? (ok ? SuccessColor : ErrorColor) : RunningColor;

            canvas.DrawText($"{chevron}  {WorkflowDisplayName}", HeaderFont, HeaderFg,
                new Rectangle(bounds.X + sidePad, bounds.Y, bounds.Width - sidePadWide, headerH),
                FormatFlags.VerticalCenter | FormatFlags.Left);

            canvas.DrawText(statusLabel, HeaderFont, statusColor,
                new Rectangle(bounds.X + sidePad, bounds.Y, bounds.Width - sidePadWide, headerH),
                FormatFlags.VerticalCenter | FormatFlags.Right);

            if (!IsExpanded) return;

            // ── 分隔线 ──
            int sepY = bounds.Y + headerH;
            using (var pen = new Pen(CardBorder))
                g.DrawLine(pen, bounds.X + sidePad, sepY, bounds.Right - sidePad, sepY);

            // ── 步骤列表（Timeline 风格）──
            var sorted = new List<WorkflowStep>(_steps.Values);
            sorted.Sort((a, b) => a.StepIndex.CompareTo(b.StepIndex));

            int y = sepY + BlockDpi.S(g, 4);
            int textX = dotX + dotSize + BlockDpi.S(g, 8);
            int textW = bounds.Right - sidePad - textX;

            for (int i = 0; i < sorted.Count; i++)
            {
                var step = sorted[i];
                var dotColor = step.IsCompleted ? (step.Success ? SuccessColor : ErrorColor) : RunningColor;

                // 竖线（连接相邻的点）
                if (i < sorted.Count - 1)
                {
                    int lineStartY = y + stepLineH / 2 + dotSize / 2;
                    int nextH = stepLineH + (!string.IsNullOrEmpty(step.Description) ? descLineH : 0);
                    using (var pen = new Pen(Color.FromArgb(209, 213, 219), 1))
                        g.DrawLine(pen, dotX + dotSize / 2, lineStartY, dotX + dotSize / 2, y + nextH + stepLineH / 2 - dotSize / 2);
                }

                // 圆点
                int dotY = y + (stepLineH - dotSize) / 2;
                using (var brush = new SolidBrush(DotBg))
                    g.FillEllipse(brush, dotX, dotY, dotSize, dotSize);
                using (var pen = new Pen(dotColor, lineWidth))
                    g.DrawEllipse(pen, dotX, dotY, dotSize, dotSize);
                if (step.IsCompleted && step.Success)
                {
                    using (var brush = new SolidBrush(dotColor))
                        g.FillEllipse(brush, dotX + 2, dotY + 2, dotSize - 4, dotSize - 4);
                }

                // 步骤名称
                canvas.DrawText(step.StepName, StepFont, ContentFg,
                    new Rectangle(textX, y, textW, stepLineH),
                    FormatFlags.VerticalCenter | FormatFlags.Left);

                y += stepLineH;

                // 描述
                if (!string.IsNullOrEmpty(step.Description))
                {
                    canvas.DrawText(step.Description, DescFont, DescFg,
                        new Rectangle(textX, y, textW, descLineH),
                        FormatFlags.VerticalCenter | FormatFlags.Left);
                    y += descLineH;
                }
            }
        }

        public bool HitTest(Point pt, Rectangle bounds) => pt.Y <= bounds.Y + _cachedHeaderHeight;

        public void OnClick(Point pt, Rectangle bounds)
        {
            if (pt.Y <= bounds.Y + _cachedHeaderHeight)
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
        private int _cachedSidePad = SidePad;
        private int _cachedButtonW = ButtonW;
        private int _cachedButtonH = ButtonH;
        private int _cachedButtonGap = ButtonGap;

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
            int sidePad = BlockDpi.S(g, SidePad);
            int headerH = BlockDpi.S(g, HeaderH);
            int topPad = BlockDpi.S(g, TopPad);
            int sepGap = BlockDpi.S(g, SepGap);
            int contentGap = BlockDpi.S(g, ContentGap);
            int buttonH = BlockDpi.S(g, ButtonH);
            int buttonGap = BlockDpi.S(g, ButtonGap);
            int buttonTopGap = BlockDpi.S(g, ButtonTopGap);
            int bottomPad = BlockDpi.S(g, BottomPad);
            int resultH = BlockDpi.S(g, ResultH);
            int toolBadgeH = BlockDpi.S(g, ToolBadgeH);
            int labelLineH = BlockDpi.S(g, LabelLineH);
            int codeBlockPadX = BlockDpi.S(g, CodeBlockPadX);
            int codeBlockPadY = BlockDpi.S(g, CodeBlockPadY);

            _cachedSidePad = sidePad;
            _cachedButtonW = BlockDpi.S(g, ButtonW);
            _cachedButtonH = buttonH;
            _cachedButtonGap = buttonGap;

            int textW = width - sidePad * 2;
            var canvas = g.High();

            int h = topPad;
            h += headerH;          // 标题行
            h += sepGap;           // 标题→分隔线间距
            h += 1;                // 分隔线
            h += contentGap;       // 分隔线→正文间距

            // 描述提示语
            var descSize = canvas.MeasureText("AI 助手请求执行以下操作，请确认是否允许：", DescFont, textW, FormatFlags.Top | FormatFlags.Left);
            h += descSize.Height + BlockDpi.S(g, 12);

            // 工具名称行（徽章）
            h += toolBadgeH + BlockDpi.S(g, 10);

            // 参数代码块
            if (!string.IsNullOrEmpty(Summary))
            {
                h += labelLineH; // "参数:" 标签
                int codeW = textW - codeBlockPadX * 2;
                var codeSize = canvas.MeasureText(Summary, CodeFont, codeW, FormatFlags.Top | FormatFlags.Left);
                h += codeSize.Height + codeBlockPadY * 2 + buttonTopGap;
            }
            else
            {
                h += BlockDpi.S(g, 4);
            }

            _cachedBtnYOffset = h; // 缓存按钮起始位置

            if (_result == null)
                h += buttonH + bottomPad;
            else
                h += resultH + bottomPad;

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
            int cornerRadius = BlockDpi.S(g, 8);
            int sidePad = BlockDpi.S(g, SidePad);
            int topPad = BlockDpi.S(g, TopPad);
            int headerH = BlockDpi.S(g, HeaderH);
            int sepGap = BlockDpi.S(g, SepGap);
            int contentGap = BlockDpi.S(g, ContentGap);
            int toolBadgeH = BlockDpi.S(g, ToolBadgeH);
            int labelLineH = BlockDpi.S(g, LabelLineH);
            int codeBlockPadX = BlockDpi.S(g, CodeBlockPadX);
            int codeBlockPadY = BlockDpi.S(g, CodeBlockPadY);
            int buttonW = BlockDpi.S(g, ButtonW);
            int buttonH = BlockDpi.S(g, ButtonH);
            int buttonGap = BlockDpi.S(g, ButtonGap);
            int buttonTopGap = BlockDpi.S(g, ButtonTopGap);
            int resultH = BlockDpi.S(g, ResultH);

            // 圆角背景
            using (var path = RoundedRect(bounds, cornerRadius))
            {
                using (var brush = new SolidBrush(bgColor))
                    g.FillPath(brush, path);
                using (var pen = new Pen(borderColor))
                    g.DrawPath(pen, path);
            }

            var canvas = g.High();
            int textX = bounds.X + sidePad;
            int textW = bounds.Width - sidePad * 2;
            int y = bounds.Y + topPad;

            // 标题
            canvas.DrawText("操作确认", HeaderFont, HeaderFg,
                new Rectangle(textX, y, textW, headerH),
                FormatFlags.VerticalCenter | FormatFlags.Left);
            y += headerH + sepGap;

            // 分隔线
            using (var pen = new Pen(_result == null ? SepColor : borderColor))
                g.DrawLine(pen, textX, y, bounds.Right - sidePad, y);
            y += 1 + contentGap;

            // 描述提示
            string desc = "AI 助手请求执行以下操作，请确认是否允许：";
            var descSize = canvas.MeasureText(desc, DescFont, textW, FormatFlags.Top | FormatFlags.Left);
            canvas.DrawText(desc, DescFont, DescFg,
                new Rectangle(textX, y, textW, descSize.Height),
                FormatFlags.Top | FormatFlags.Left);
            y += descSize.Height + BlockDpi.S(g, 12);

            // 工具名称徽章
            string toolLabel = "工具: ";
            var toolLabelSize = canvas.MeasureText(toolLabel, LabelFont, textW, FormatFlags.VerticalCenter | FormatFlags.Left);
            canvas.DrawText(toolLabel, LabelFont, HeaderFg,
                new Rectangle(textX, y, toolLabelSize.Width + BlockDpi.S(g, 2), toolBadgeH),
                FormatFlags.VerticalCenter | FormatFlags.Left);

            string toolName = $"{ToolDisplayName} ({FunctionName})";
            int badgeX = textX + toolLabelSize.Width + BlockDpi.S(g, 6);
            var toolNameSize = canvas.MeasureText(toolName, DescFont, textW, FormatFlags.Top | FormatFlags.Left);
            int badgeW = Math.Min(toolNameSize.Width + BlockDpi.S(g, 16), bounds.Right - sidePad - badgeX);
            int badgeH = BlockDpi.S(g, 20);
            var badgeRect = new Rectangle(badgeX, y + (toolBadgeH - badgeH) / 2, badgeW, badgeH);
            using (var path = RoundedRect(badgeRect, BlockDpi.S(g, 4)))
            {
                using (var brush = new SolidBrush(ToolBadgeBg))
                    g.FillPath(brush, path);
                using (var pen = new Pen(ToolBadgeBorder))
                    g.DrawPath(pen, path);
            }
            canvas.DrawText(toolName, DescFont, ToolBadgeFg, badgeRect, FormatFlags.Center);
            y += toolBadgeH + BlockDpi.S(g, 10);

            // 参数代码块
            if (!string.IsNullOrEmpty(Summary))
            {
                // "参数:" 标签
                canvas.DrawText("参数:", LabelFont, HeaderFg,
                    new Rectangle(textX, y, textW, labelLineH),
                    FormatFlags.VerticalCenter | FormatFlags.Left);
                y += labelLineH;

                // 代码块背景
                int codeW = textW - codeBlockPadX * 2;
                var codeSize = canvas.MeasureText(Summary, CodeFont, codeW, FormatFlags.Top | FormatFlags.Left);
                int codeBlockH = codeSize.Height + codeBlockPadY * 2;
                var codeRect = new Rectangle(textX, y, textW, codeBlockH);
                using (var path = RoundedRect(codeRect, BlockDpi.S(g, 6)))
                {
                    using (var brush = new SolidBrush(CodeBlockBg))
                        g.FillPath(brush, path);
                    using (var pen = new Pen(CodeBlockBorder))
                        g.DrawPath(pen, path);
                }
                canvas.DrawText(Summary, CodeFont, CodeFg,
                    new Rectangle(textX + codeBlockPadX, y + codeBlockPadY, codeW, codeSize.Height),
                    FormatFlags.Top | FormatFlags.Left);
                y += codeBlockH + buttonTopGap;
            }
            else
            {
                y += BlockDpi.S(g, 4);
            }

            if (_result == null)
            {
                // 绘制审批按钮（柔和描边风格）
                int btnX = textX;

                // 允许按钮
                var approveRect = new Rectangle(btnX, y, buttonW, buttonH);
                using (var path = RoundedRect(approveRect, BlockDpi.S(g, 6)))
                {
                    using (var brush = new SolidBrush(ApproveBtnBg))
                        g.FillPath(brush, path);
                    using (var pen = new Pen(ApproveBtnBorder))
                        g.DrawPath(pen, path);
                }
                canvas.DrawText("允许执行", ButtonFont, ApproveBtnText, approveRect,
                    FormatFlags.Center);

                // 拒绝按钮
                var rejectRect = new Rectangle(btnX + buttonW + buttonGap, y, buttonW, buttonH);
                using (var path = RoundedRect(rejectRect, BlockDpi.S(g, 6)))
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
                    new Rectangle(textX, y, textW, resultH),
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
            int btnX = bounds.X + _cachedSidePad;
            return (
                new Rectangle(btnX, y, _cachedButtonW, _cachedButtonH),
                new Rectangle(btnX + _cachedButtonW + _cachedButtonGap, y, _cachedButtonW, _cachedButtonH)
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
        private int _cachedSidePad = SidePad;
        private int _cachedSubmitBtnW = SubmitBtnW;
        private int _cachedPillGap = PillGap;
        private int _cachedInputAreaH = InputAreaH;

        public AskUserCard(string question, List<AskUserOption> options, bool allowFreeInput)
        {
            Question = question;
            Options = options ?? new List<AskUserOption>();
            AllowFreeInput = allowFreeInput;
        }

        public int MeasureHeight(Graphics g, int width)
        {
            int topPad = BlockDpi.S(g, TopPad);
            int sidePad = BlockDpi.S(g, SidePad);
            int headerH = BlockDpi.S(g, HeaderH);
            int sepGap = BlockDpi.S(g, SepGap);
            int contentGap = BlockDpi.S(g, ContentGap);
            int pillH = BlockDpi.S(g, PillH);
            int pillRowGap = BlockDpi.S(g, PillRowGap);
            int inputAreaH = BlockDpi.S(g, InputAreaH);
            int inputGap = BlockDpi.S(g, InputGap);
            int bottomPad = BlockDpi.S(g, BottomPad);
            int answeredH = BlockDpi.S(g, AnsweredH);

            _cachedSidePad = sidePad;
            _cachedSubmitBtnW = BlockDpi.S(g, SubmitBtnW);
            _cachedPillGap = BlockDpi.S(g, PillGap);
            _cachedInputAreaH = inputAreaH;

            int textW = width - sidePad * 2;
            var canvas = g.High();

            int h = topPad;
            h += headerH;
            h += sepGap + 1 + contentGap; // 分隔线区

            // 问题文本
            var qSize = canvas.MeasureText(Question, QuestionFont, textW, FormatFlags.Top | FormatFlags.Left);
            h += qSize.Height + contentGap;

            // 计算 pill 布局（全宽垂直堆叠，保持视觉整齐）
            _pillRects.Clear();
            if (Options.Count > 0 && _answer == null)
            {
                int py = h;
                foreach (var opt in Options)
                {
                    int pillHeight = opt.Description != null ? pillH + BlockDpi.S(g, 14) : pillH;
                    _pillRects.Add(new Rectangle(0, py, textW, pillHeight));
                    py += pillHeight + pillRowGap;
                }
                h = py;
                _cachedPillsEndY = h;
            }
            else if (_answer != null)
            {
                h += answeredH;
                _cachedPillsEndY = h;
            }

            // 自由输入区
            if (AllowFreeInput && _answer == null)
            {
                _cachedInputYOffset = h;
                h += inputAreaH + inputGap;
            }

            h += bottomPad;
            return h;
        }

        public void Paint(Graphics g, Rectangle bounds, bool isUser)
        {
            bool answered = _answer != null;
            var bgColor = answered ? AnsweredBg : CardBg;
            var borderColor = answered ? AnsweredBorder : CardBorder;
            var accentClr = answered ? AnsweredAccent : AccentColor;
            int cornerRadius = BlockDpi.S(g, 8);
            int lineWidth = BlockDpi.S(g, 3);
            int sidePad = BlockDpi.S(g, SidePad);
            int topPad = BlockDpi.S(g, TopPad);
            int headerH = BlockDpi.S(g, HeaderH);
            int sepGap = BlockDpi.S(g, SepGap);
            int contentGap = BlockDpi.S(g, ContentGap);
            int pillH = BlockDpi.S(g, PillH);
            int pillPadH = BlockDpi.S(g, PillPadH);
            int answeredH = BlockDpi.S(g, AnsweredH);
            int pillDescH = BlockDpi.S(g, 14);
            int textOffsetY = BlockDpi.S(g, 2);
            int lineTop = BlockDpi.S(g, 10);

            // 圆角背景
            using (var path = RoundedRect(bounds, cornerRadius))
            {
                using (var brush = new SolidBrush(bgColor))
                    g.FillPath(brush, path);
                using (var pen = new Pen(borderColor))
                    g.DrawPath(pen, path);
            }

            // 左侧装饰线
            using (var pen = new Pen(accentClr, lineWidth))
                g.DrawLine(pen, bounds.X + BlockDpi.S(g, 4), bounds.Y + lineTop, bounds.X + BlockDpi.S(g, 4), bounds.Bottom - lineTop);

            var canvas = g.High();
            int textX = bounds.X + sidePad;
            int textW = bounds.Width - sidePad * 2;
            int y = bounds.Y + topPad;

            // 标题
            canvas.DrawText("💬 需要您的输入", HeaderFont, answered ? AnsweredFg : HeaderFg,
                new Rectangle(textX, y, textW, headerH),
                FormatFlags.VerticalCenter | FormatFlags.Left);
            y += headerH + sepGap;

            // 分隔线
            using (var pen = new Pen(answered ? AnsweredBorder : SepColor))
                g.DrawLine(pen, textX, y, bounds.Right - sidePad, y);
            y += 1 + contentGap;

            // 问题文本
            var qSize = canvas.MeasureText(Question, QuestionFont, textW, FormatFlags.Top | FormatFlags.Left);
            canvas.DrawText(Question, QuestionFont, QuestionFg,
                new Rectangle(textX, y, textW, qSize.Height),
                FormatFlags.Top | FormatFlags.Left);
            y += qSize.Height + contentGap;

            if (!answered)
            {
                // 绘制选项 pills
                for (int i = 0; i < _pillRects.Count && i < Options.Count; i++)
                {
                    var pr = _pillRects[i];
                    var pillBounds = new Rectangle(textX + pr.X, bounds.Y + pr.Y, pr.Width, pr.Height);
                    bool hovered = (i == _hoveredPillIndex);

                    using (var path = RoundedRect(pillBounds, BlockDpi.S(g, 6)))
                    {
                        using (var brush = new SolidBrush(hovered ? PillHoverBg : PillBg))
                            g.FillPath(brush, path);
                        using (var pen = new Pen(PillBorder))
                            g.DrawPath(pen, path);
                    }

                    if (Options[i].Description != null)
                    {
                        int labelH = pillH - BlockDpi.S(g, 4);
                        canvas.DrawText(Options[i].Label, PillFont, PillText,
                            new Rectangle(pillBounds.X + pillPadH, pillBounds.Y + textOffsetY, pillBounds.Width - pillPadH * 2, labelH),
                            FormatFlags.VerticalCenter | FormatFlags.Left);
                        canvas.DrawText(Options[i].Description, PillDescFont, PillDescColor,
                            new Rectangle(pillBounds.X + pillPadH, pillBounds.Y + labelH - textOffsetY, pillBounds.Width - pillPadH * 2, pillDescH),
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
                    int inputW = textW - _cachedSubmitBtnW - _cachedPillGap;
                    EmbeddedInput.SetBounds(textX, inputY, inputW, _cachedInputAreaH);
                    EmbeddedSubmitBtn.SetBounds(textX + inputW + _cachedPillGap, inputY, _cachedSubmitBtnW, _cachedInputAreaH);
                    EmbeddedInput.Visible = true;
                    EmbeddedSubmitBtn.Visible = true;
                }
            }
            else
            {
                // 已回答：显示答案
                canvas.DrawText($"已回答：{_answer}", AnsweredFont, AnsweredFg,
                    new Rectangle(textX, y, textW, answeredH),
                    FormatFlags.VerticalCenter | FormatFlags.Left);

                // 隐藏嵌入控件
                if (EmbeddedInput != null) EmbeddedInput.Visible = false;
                if (EmbeddedSubmitBtn != null) EmbeddedSubmitBtn.Visible = false;
            }
        }

        public bool HitTest(Point pt, Rectangle bounds)
        {
            if (_answer != null) return false;
            int textX = bounds.X + _cachedSidePad;
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
            int textX = bounds.X + _cachedSidePad;
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
            int textX = bounds.X + _cachedSidePad;
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
        private float _scale = 1f;

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

        /// <summary>显示加载指示器（thinking.gif + 提示文字）</summary>
        public void ShowThinkingIndicator()
        {
            if (_blocks.OfType<ThinkingIndicatorBlock>().Any()) return;
            var indicator = new ThinkingIndicatorBlock();
            _blocks.Add(indicator);
            indicator.StartAnimation(this);
            RequestLayout();
        }

        /// <summary>隐藏加载指示器</summary>
        public void HideThinkingIndicator()
        {
            for (int i = _blocks.Count - 1; i >= 0; i--)
            {
                if (_blocks[i] is ThinkingIndicatorBlock ind)
                {
                    ind.Dispose();
                    _blocks.RemoveAt(i);
                }
            }
            RequestLayout();
        }

        /// <summary>设置文本内容，自动解析 &lt;think&gt; 标签为折叠块，自动识别 Markdown 表格。</summary>
        public void SetText(string text)
        {
            text = text ?? "";

            // 收到实际内容时自动移除加载指示器
            HideThinkingIndicator();

            // 解析 <think>...</think> 标签
            string thinkContent = null;
            string mainContent = text;

            var thinkMatch = Regex.Match(text, @"<think>(.*?)(?:</think>|$)", RegexOptions.Singleline);
            if (thinkMatch.Success)
            {
                thinkContent = thinkMatch.Groups[1].Value.Trim();
                mainContent = Regex.Replace(text, @"\s*<think>.*?(?:</think>|$)\s*", "", RegexOptions.Singleline).Trim();
            }

            // 确定当前流式段落的起点（最后一个持久 UI 块之后）
            int sectionStart = 0;
            for (int i = _blocks.Count - 1; i >= 0; i--)
            {
                if (_blocks[i] is ToolCallCard
                    || _blocks[i] is WorkflowBlock
                    || _blocks[i] is SubAgentBlock)
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

        /// <summary>移除指定的内容块</summary>
        internal void RemoveBlock(IContentBlock block)
        {
            _blocks.Remove(block);
            RequestLayout();
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

        /// <summary>添加工作流进度卡片</summary>
        public WorkflowBlock AddWorkflowCard(string workflowName, string workflowDisplayName, int totalSteps)
        {
            var block = new WorkflowBlock(workflowName, workflowDisplayName, totalSteps);
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
            int avatarSize = S(AvatarSize);
            int avatarGap = S(AvatarGap);
            int bubblePadH = S(BubblePadH);
            int maxBubble = (int)(MaxBubbleWidth * 0.80);
            return Math.Max(S(120), maxBubble - avatarSize - avatarGap - bubblePadH * 2);
        }

        internal void RecalcLayout()
        {
            if (!IsHandleCreated) return;
            try
            {
                using (var g = CreateGraphics())
                {
                    UpdateScale(g);
                    int contentW = BubbleContentWidth();
                    EnsureMeasureCache(g, contentW);

                    int bubbleH = _cachedTotalContentHeight + S(BubblePadV) * 2;
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
            UpdateScale(g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int contentW = BubbleContentWidth();
            EnsureMeasureCache(g, contentW);
            int radius = S(Radius);
            int bubblePadH = S(BubblePadH);
            int bubblePadV = S(BubblePadV);
            int avatarSize = S(AvatarSize);
            int avatarGap = S(AvatarGap);
            int blockGap = S(BlockGap);
            int edge = S(6);
            int avatarTopGap = S(4);

            int bubbleW = Math.Max(radius * 2 + 2, contentW + bubblePadH * 2);
            int bubbleH = Math.Max(radius * 2 + 2, _cachedTotalContentHeight + bubblePadV * 2);
            bool isUser = Role == ChatRole.User;

            // 计算位置
            int avatarX, bubbleX;
            if (isUser)
            {
                avatarX = Width - avatarSize - edge;
                bubbleX = avatarX - avatarGap - bubbleW;
            }
            else
            {
                avatarX = edge;
                bubbleX = avatarX + avatarSize + avatarGap;
            }

            int bubbleY = NameHeight;

            // ── 绘制头像（圆形裁剪）──
            if (Avatar != null)
            {
                var avatarRect = new Rectangle(avatarX, bubbleY + avatarTopGap, avatarSize, avatarSize);
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
            var shadowRect = new Rectangle(bubbleX + S(1), bubbleY + S(2), bubbleW, bubbleH);
            using (var shadowPath = CreateRoundedRect(shadowRect, radius))
            using (var shadowBrush = new SolidBrush(ShadowColor))
                g.FillPath(shadowBrush, shadowPath);

            // ── 绘制气泡背景 ──
            var bubbleRect = new Rectangle(bubbleX, bubbleY, bubbleW, bubbleH);
            using (var path = CreateRoundedRect(bubbleRect, radius))
            {
                using (var brush = new SolidBrush(isUser ? UserBg : AIBg))
                    g.FillPath(brush, path);
                if (!isUser)
                    using (var pen = new Pen(AIBorder))
                        g.DrawPath(pen, path);
            }

            // ── 绘制内容块 ──
            int blockX = bubbleX + bubblePadH;
            int blockY = bubbleY + bubblePadV;
            for (int i = 0; i < _blocks.Count; i++)
            {
                var block = _blocks[i];
                int h = i < _cachedBlockHeights.Count ? _cachedBlockHeights[i] : 0;
                var blockRect = new Rectangle(blockX, blockY, contentW, h);
                block.Paint(g, blockRect, isUser);
                blockY += h + blockGap;
            }
        }

        // ── 鼠标交互 ──

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int contentW = BubbleContentWidth();
            using (var g = CreateGraphics())
            {
                UpdateScale(g);
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
                    UpdateScale(g);
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
                _cachedTotalContentHeight += height + S(BlockGap);
                if (!_containsInteractiveBlocks && (block is ThinkBlock || block is ToolCallCard || block is SubAgentBlock || block is ApprovalCard || block is AskUserCard))
                    _containsInteractiveBlocks = true;
            }

            if (_cachedTotalContentHeight > 0)
                _cachedTotalContentHeight -= S(BlockGap);

            _cachedMeasuredWidth = contentW;
            _measureDirty = false;
        }

        private bool TryFindBlock(Point location, int contentW, out IContentBlock hitBlock, out Rectangle hitRect)
        {
            hitBlock = null;
            hitRect = Rectangle.Empty;

            bool isUser = Role == ChatRole.User;
            int avatarSize = S(AvatarSize);
            int avatarGap = S(AvatarGap);
            int bubblePadH = S(BubblePadH);
            int bubblePadV = S(BubblePadV);
            int edge = S(6);
            int bubbleW = contentW + bubblePadH * 2;
            int bubbleX = isUser
                ? Width - avatarSize - edge - avatarGap - bubbleW
                : edge + avatarSize + avatarGap;

            int blockX = bubbleX + bubblePadH;
            int blockY = NameHeight + bubblePadV;

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
                blockY += height + S(BlockGap);
            }

            return false;
        }

        private int S(int value)
        {
            return UiScale.Scale(value, _scale);
        }

        private void UpdateScale(Graphics g)
        {
            if (g == null) return;
            float s = g.DpiX / 96f;
            if (float.IsNaN(s) || float.IsInfinity(s) || s <= 0f)
                s = 1f;
            _scale = Math.Max(1f, Math.Min(3f, s));
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

        private int ContentWidth => Math.Max(200, ClientSize.Width - 2);

        private void PerformRelayout()
        {
            _container.SuspendLayout();
            int w = ContentWidth;
            int y = 12;
            foreach (var msg in _messages)
            {
                msg.MaxBubbleWidth = w;
                msg.Width = w;
                msg.RecalcLayout();
                msg.Location = new Point(0, y);
                y += msg.Height + 8;
            }
            _container.Size = new Size(ClientSize.Width, Math.Max(ClientSize.Height, y + 12));
            _container.ResumeLayout(false);
            _container.Invalidate();
            Invalidate();
        }
    }
}
