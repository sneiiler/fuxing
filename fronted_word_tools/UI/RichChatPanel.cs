using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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

        public int MeasureHeight(Graphics g, int width)
        {
            if (string.IsNullOrEmpty(Text)) return 0;
            int totalH = 0;
            var canvas = g.High();
            foreach (var seg in ParseSegments(Text))
            {
                var size = canvas.MeasureText(seg.Text, seg.Font, width);
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
            foreach (var seg in ParseSegments(Text))
            {
                var color = seg.IsSecondary ? Color.FromArgb(107, 114, 128) : fgColor;
                var size = canvas.MeasureText(seg.Text, seg.Font, bounds.Width);
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
            var size = canvas.MeasureText(Text ?? "", ContentFont, width - 20);
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

        private const int HeaderH = 30;
        private const int DetailLineH = 22;
        private const int Pad = 10;

        private static readonly Font NameFont = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
        private static readonly Font StatusFont = new Font("Microsoft YaHei UI", 8.5F);
        private static readonly Font DetailFont = new Font("Microsoft YaHei UI", 8.5F);

        private static readonly Color CardBg = Color.FromArgb(250, 251, 252);
        private static readonly Color CardBorder = Color.FromArgb(216, 222, 228);
        private static readonly Color RunningColor = Color.FromArgb(59, 130, 246);
        private static readonly Color SuccessColor = Color.FromArgb(22, 163, 74);
        private static readonly Color ErrorColor = Color.FromArgb(220, 38, 38);

        public ToolCallCard(string toolName, string statusText, ToolCallStatus status)
        {
            ToolName = toolName;
            StatusText = statusText;
            Status = status;
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
            int h = HeaderH + Pad * 2;
            if (!string.IsNullOrEmpty(StatusText))
            {
                var canvas = g.High();
                var size = canvas.MeasureText(StatusText, StatusFont, width - Pad * 2 - 30);
                h += Math.Max(DetailLineH, size.Height);
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

            // 卡片背景 + 边框
            using (var path = RoundedRect(bounds, 8))
            {
                using (var brush = new SolidBrush(CardBg))
                    g.FillPath(brush, path);
                using (var pen = new Pen(CardBorder))
                    g.DrawPath(pen, path);
            }

            // 左侧强调色条
            var accentRect = new Rectangle(bounds.X + 1, bounds.Y + 8, 3, bounds.Height - 16);
            using (var brush = new SolidBrush(accentColor))
                g.FillRectangle(brush, accentRect);

            var canvas = g.High();
            {
                // 状态 emoji 图标 + 工具名称
                string icon = Status == ToolCallStatus.Success ? "✅"
                            : Status == ToolCallStatus.Error ? "❌" : "⏳";

                int textX = bounds.X + Pad + 6;
                int textY = bounds.Y + Pad;
                canvas.DrawText($"{icon}  {ToolName}", NameFont, Color.FromArgb(31, 41, 55),
                    new Rectangle(textX, textY, bounds.Width - Pad * 2 - 6, HeaderH),
                    FormatFlags.VerticalCenter | FormatFlags.Left);

                textY += HeaderH;

                // 分隔线
                using (var pen = new Pen(Color.FromArgb(229, 231, 235)))
                    g.DrawLine(pen, textX, textY - 2, bounds.Right - Pad, textY - 2);

                // 状态文本
                if (!string.IsNullOrEmpty(StatusText))
                {
                    var statusSize = canvas.MeasureText(StatusText, StatusFont, bounds.Width - Pad * 2 - 30);
                    int statusH = Math.Max(DetailLineH, statusSize.Height);
                    canvas.DrawText(StatusText, StatusFont, accentColor,
                        new Rectangle(textX + 20, textY, bounds.Width - Pad * 2 - 26, statusH),
                        FormatFlags.Top | FormatFlags.Left);
                    textY += statusH;
                }

                // 详细信息列表
                foreach (var detail in Details)
                {
                    canvas.DrawText($"  ▪  {detail}", DetailFont, Color.FromArgb(75, 85, 99),
                        new Rectangle(textX + 20, textY, bounds.Width - Pad * 2 - 26, DetailLineH),
                        FormatFlags.VerticalCenter | FormatFlags.Left);
                    textY += DetailLineH;
                }
            }
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

        // 颜色
        private static readonly Color AIBg = Color.White;
        private static readonly Color UserBg = Color.FromArgb(59, 130, 246);
        private static readonly Color AIBorder = Color.FromArgb(229, 231, 235);
        private static readonly Color NameColor = Color.FromArgb(107, 114, 128);
        private static readonly Color ShadowColor = Color.FromArgb(15, 0, 0, 0);

        public MessageGroup(string name, Image avatar, ChatRole role, int maxWidth)
        {
            Role = role;
            UserName = name;
            Avatar = avatar;
            MaxBubbleWidth = maxWidth;

            SetStyle(ControlStyles.UserPaint
                   | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.DoubleBuffer
                   | ControlStyles.ResizeRedraw
                   | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
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
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>设置文本内容，自动解析 &lt;think&gt; 标签为折叠块</summary>
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
                // 移除 <think>...</think> 及其前后空白
                mainContent = Regex.Replace(text, @"\s*<think>.*?(?:</think>|$)\s*", "", RegexOptions.Singleline).Trim();
            }

            // 更新或创建 ThinkBlock
            var existingThink = _blocks.OfType<ThinkBlock>().FirstOrDefault();
            if (thinkContent != null)
            {
                if (existingThink != null)
                    existingThink.Text = thinkContent;
                else
                {
                    var thinkBlock = new ThinkBlock(thinkContent);
                    _blocks.Insert(0, thinkBlock);
                    thinkBlock.ToggleRequested += () => RequestLayout();
                }
            }

            // 更新或创建 TextBlock
            var existingText = _blocks.OfType<TextBlock>().FirstOrDefault();
            if (!string.IsNullOrEmpty(mainContent))
            {
                if (existingText != null)
                    existingText.Text = mainContent;
                else
                {
                    // 在 ThinkBlock 之后插入
                    int insertIdx = thinkContent != null ? 1 : 0;
                    _blocks.Insert(insertIdx, new TextBlock(mainContent, Role));
                }
            }
            else if (existingText != null)
            {
                existingText.Text = "";
            }

            RequestLayout();
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
                    int totalH = 0;
                    foreach (var block in _blocks)
                    {
                        totalH += block.MeasureHeight(g, contentW) + BlockGap;
                    }
                    if (totalH > 0) totalH -= BlockGap;

                    int bubbleH = totalH + BubblePadV * 2;
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
            int totalContentH = 0;
            using (var measG = CreateGraphics())
            {
                foreach (var block in _blocks)
                    totalContentH += block.MeasureHeight(measG, contentW) + BlockGap;
            }
            if (totalContentH > 0) totalContentH -= BlockGap;

            int bubbleW = Math.Max(Radius * 2 + 2, contentW + BubblePadH * 2);
            int bubbleH = Math.Max(Radius * 2 + 2, totalContentH + BubblePadV * 2);
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
            foreach (var block in _blocks)
            {
                int h;
                using (var measG = CreateGraphics())
                    h = block.MeasureHeight(measG, contentW);
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
            bool isUser = Role == ChatRole.User;
            int bubbleW = contentW + BubblePadH * 2;
            int bubbleX = isUser
                ? Width - AvatarSize - 6 - AvatarGap - bubbleW
                : 6 + AvatarSize + AvatarGap;

            int blockX = bubbleX + BubblePadH;
            int blockY = NameHeight + BubblePadV;

            using (var measG = CreateGraphics())
            {
                foreach (var block in _blocks)
                {
                    int h = block.MeasureHeight(measG, contentW);
                    var blockRect = new Rectangle(blockX, blockY, contentW, h);
                    if (blockRect.Contains(e.Location))
                    {
                        block.OnClick(e.Location, blockRect);
                        break;
                    }
                    blockY += h + BlockGap;
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            // 思考块头部显示手型光标
            int contentW = BubbleContentWidth();
            bool isUser = Role == ChatRole.User;
            int bubbleW = contentW + BubblePadH * 2;
            int bubbleX = isUser
                ? Width - AvatarSize - 6 - AvatarGap - bubbleW
                : 6 + AvatarSize + AvatarGap;

            int blockX = bubbleX + BubblePadH;
            int blockY = NameHeight + BubblePadV;
            bool hand = false;

            using (var measG = CreateGraphics())
            {
                foreach (var block in _blocks)
                {
                    int h = block.MeasureHeight(measG, contentW);
                    var blockRect = new Rectangle(blockX, blockY, contentW, h);
                    if (blockRect.Contains(e.Location) && block.HitTest(e.Location, blockRect))
                    {
                        hand = true;
                        break;
                    }
                    blockY += h + BlockGap;
                }
            }
            Cursor = hand ? Cursors.Hand : Cursors.Default;
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
        private readonly System.Windows.Forms.Panel _container;
        private readonly List<MessageGroup> _messages = new List<MessageGroup>();

        public RichChatPanel()
        {
            SetStyle(ControlStyles.UserPaint
                   | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer, true);
            AutoScroll = true;
            BackColor = Color.FromArgb(245, 247, 250);

            _container = new System.Windows.Forms.Panel
            {
                Location = Point.Empty,
                BackColor = Color.Transparent,
                AutoSize = false
            };
            Controls.Add(_container);

            Resize += (s, e) => PerformRelayout();
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
                Application.DoEvents();
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
                msg.Width = w;  // 先设宽度，再计算高度
                msg.RecalcLayout();
                msg.Location = new Point(4, y);
                y += msg.Height + 8;
            }
            _container.Size = new Size(w + 8, y + 12);
            _container.ResumeLayout(false);
            _container.Invalidate(true);
        }
    }
}
