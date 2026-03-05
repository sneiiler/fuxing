using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace FuXing.UI
{
    /// <summary>
    /// 会话列表面板 — 显示历史对话列表，支持选择、删除。
    /// 在 TaskPaneControl 中与 RichChatPanel 同位切换显示。
    /// </summary>
    public class SessionListPanel : UserControl
    {
        // ═══════════════════════════════════════════════════════════════
        //  事件
        // ═══════════════════════════════════════════════════════════════

        /// <summary>用户点击了某个会话条目</summary>
        public event Action<string> SessionSelected;

        /// <summary>用户点击了某个会话的删除按钮</summary>
        public event Action<string> SessionDeleted;

        /// <summary>用户点击了返回按钮</summary>
        public event Action BackRequested;

        // ═══════════════════════════════════════════════════════════════
        //  控件
        // ═══════════════════════════════════════════════════════════════

        private readonly AntdUI.Panel _headerPanel;
        private readonly Panel _listContainer;
        private string _activeSessionId;

        public SessionListPanel()
        {
            BackColor = Color.FromArgb(248, 249, 250);
            Dock = DockStyle.Fill;

            // ── 顶部标题栏 ──
            _headerPanel = new AntdUI.Panel
            {
                Dock = DockStyle.Top,
                Height = 42,
                Back = Color.White,
                BorderWidth = 0,
                Padding = new Padding(8, 0, 8, 0)
            };

            var backBtn = new AntdUI.Button
            {
                Text = "←",
                Size = new Size(36, 30),
                Location = new Point(6, 6),
                Type = AntdUI.TTypeMini.Default,
                Font = new Font("Microsoft YaHei UI", 11F),
                Radius = 6
            };
            backBtn.Click += (s, e) => BackRequested?.Invoke();

            var titleLabel = new AntdUI.Label
            {
                Text = "对话历史",
                Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(31, 41, 55),
                Location = new Point(48, 8),
                Size = new Size(200, 26),
                TextAlign = ContentAlignment.MiddleLeft
            };

            _headerPanel.Controls.Add(backBtn);
            _headerPanel.Controls.Add(titleLabel);

            // ── 底部分隔线（画在 header 底部）──
            _headerPanel.Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(229, 231, 235)))
                    e.Graphics.DrawLine(pen, 0, _headerPanel.Height - 1, _headerPanel.Width, _headerPanel.Height - 1);
            };

            // ── 可滚动的会话列表区域 ──
            _listContainer = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(248, 249, 250),
                Padding = new Padding(0, 8, 0, 8)
            };

            // 开启双缓冲
            typeof(Panel).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(_listContainer, true, null);

            Controls.Add(_listContainer);  // Fill 先加
            Controls.Add(_headerPanel);    // Top 后加（优先级更高）
        }

        // ═══════════════════════════════════════════════════════════════
        //  公共方法
        // ═══════════════════════════════════════════════════════════════

        /// <summary>刷新会话列表</summary>
        public void RefreshList(List<ChatSession> sessions, string activeSessionId)
        {
            _activeSessionId = activeSessionId;
            _listContainer.SuspendLayout();
            _listContainer.Controls.Clear();

            if (sessions == null || sessions.Count == 0)
            {
                var emptyLabel = new Label
                {
                    Text = "暂无历史对话",
                    Font = new Font("Microsoft YaHei UI", 10F),
                    ForeColor = Color.FromArgb(156, 163, 175),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Dock = DockStyle.Top,
                    Height = 60
                };
                _listContainer.Controls.Add(emptyLabel);
                _listContainer.ResumeLayout(false);
                return;
            }

            // 从底向顶添加（Dock.Top 需要反向添加才能保持顺序）
            for (int i = sessions.Count - 1; i >= 0; i--)
            {
                var card = CreateSessionCard(sessions[i]);
                _listContainer.Controls.Add(card);
            }
            _listContainer.ResumeLayout(false);

            // 滚动到顶部
            _listContainer.AutoScrollPosition = new Point(0, 0);
        }

        // ═══════════════════════════════════════════════════════════════
        //  会话卡片
        // ═══════════════════════════════════════════════════════════════

        private Panel CreateSessionCard(ChatSession session)
        {
            bool isActive = session.Id == _activeSessionId;
            int msgCount = session.Messages?.Count ?? 0;

            var card = new Panel
            {
                Dock = DockStyle.Top,
                Height = 64,
                Margin = new Padding(8, 2, 8, 2),
                Padding = new Padding(12, 8, 8, 8),
                BackColor = isActive ? Color.FromArgb(239, 246, 255) : Color.White,
                Cursor = Cursors.Hand,
                Tag = session.Id
            };

            // 开启双缓冲
            typeof(Panel).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(card, true, null);

            // 标题
            var titleLabel = new Label
            {
                Text = session.Title ?? "新对话",
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(31, 41, 55),
                AutoEllipsis = true,
                AutoSize = false,
                Size = new Size(200, 22),
                Location = new Point(12, 8),
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };

            // 副标题：日期 + 消息数
            string dateStr = session.UpdatedAt.ToString("MM/dd HH:mm");
            string subtitle = $"{dateStr}  ·  {msgCount} 条消息";
            var subtitleLabel = new Label
            {
                Text = subtitle,
                Font = new Font("Microsoft YaHei UI", 8F),
                ForeColor = Color.FromArgb(156, 163, 175),
                AutoSize = false,
                Size = new Size(200, 18),
                Location = new Point(12, 34),
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };

            // 删除按钮
            var deleteBtn = new AntdUI.Button
            {
                Text = "✕",
                Size = new Size(28, 28),
                Type = AntdUI.TTypeMini.Default,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = Color.FromArgb(156, 163, 175),
                Radius = 6,
                Anchor = AnchorStyles.Right | AnchorStyles.Top,
                Visible = false,
                Tag = session.Id
            };
            deleteBtn.Click += (s, e) =>
            {
                string id = (s as Control)?.Tag?.ToString();
                if (!string.IsNullOrEmpty(id))
                    SessionDeleted?.Invoke(id);
            };

            // 响应式：删除按钮定位到右侧
            card.Resize += (s, e) =>
            {
                int cw = card.ClientSize.Width;
                deleteBtn.Location = new Point(cw - 36, 18);
                titleLabel.Width = cw - 60;
                subtitleLabel.Width = cw - 60;
            };

            // Hover 效果
            Action<object, EventArgs> onEnter = (s, e) =>
            {
                if (!isActive) card.BackColor = Color.FromArgb(243, 244, 246);
                deleteBtn.Visible = true;
            };
            Action<object, EventArgs> onLeave = (s, e) =>
            {
                if (!isActive) card.BackColor = Color.White;
                deleteBtn.Visible = false;
            };

            card.MouseEnter += new EventHandler(onEnter);
            card.MouseLeave += new EventHandler(onLeave);
            titleLabel.MouseEnter += new EventHandler(onEnter);
            titleLabel.MouseLeave += new EventHandler(onLeave);
            subtitleLabel.MouseEnter += new EventHandler(onEnter);
            subtitleLabel.MouseLeave += new EventHandler(onLeave);

            // 点击事件
            Action<object, EventArgs> onClick = (s, e) =>
            {
                string id = card.Tag?.ToString();
                if (!string.IsNullOrEmpty(id))
                    SessionSelected?.Invoke(id);
            };
            card.Click += new EventHandler(onClick);
            titleLabel.Click += new EventHandler(onClick);
            subtitleLabel.Click += new EventHandler(onClick);

            // 圆角绘制
            card.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(isActive ? Color.FromArgb(59, 130, 246) : Color.FromArgb(229, 231, 235)))
                {
                    var rect = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
                    using (var path = RoundedRect(rect, 8))
                        g.DrawPath(pen, path);
                }
            };

            card.Controls.Add(deleteBtn);
            card.Controls.Add(subtitleLabel);
            card.Controls.Add(titleLabel);

            return card;
        }

        private static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
