using AntdUI;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;

namespace FuXing
{
    public partial class AboutDialog : AntdUI.Window
    {
        public AboutDialog()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            // ── 布局常量 ──
            const int L = 32;           // 左边距
            const int R = 32;           // 右边距
            const int W = 480;          // 窗体宽度
            int contentW = W - L - R;   // 内容区宽度

            // ── 窗体基础设置 ──
            Text = "关于 AI福星";
            Size = new Size(W, 600);
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            BackColor = Color.White;
            KeyPreview = true;

            // ── 主容器（不设 Padding，子控件用绝对坐标自行偏移）──
            var mainContainer = new System.Windows.Forms.Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                AutoScroll = false
            };

            int y = 24; // 从顶部 24px 开始

            // ════════════════════════════════════════
            //  头部：Logo + 名称 + 版本
            // ════════════════════════════════════════

            // Logo
            var logoPicture = new PictureBox
            {
                Size = new Size(64, 64),
                Location = new Point(L, y),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };
            try
            {
                var logoImage = ResourceManager.GetIcon("logo.png");
                if (logoImage != null) logoPicture.Image = logoImage;
            }
            catch { /* 加载失败时留空 */ }

            int textLeft = L + 76; // logo(64) + 12px 间距
            var titleLabel = new AntdUI.Label
            {
                Text = "AI福星",
                Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold),
                Location = new Point(textLeft, y - 4),
                Size = new Size(contentW - 76, 48),
                ForeColor = Color.FromArgb(30, 30, 30),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };

            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            string versionText = $"v{ver.Major}.{ver.Minor}.{ver.Build}";
            var versionLabel = new AntdUI.Label
            {
                Text = versionText,
                Font = new Font("Microsoft YaHei UI", 11F),
                Location = new Point(textLeft + 2, y + 46),
                Size = new Size(200, 20),
                ForeColor = Color.FromArgb(130, 130, 130)
            };

            var sloganLabel = new AntdUI.Label
            {
                Text = "Word 智能文档助手",
                Font = new Font("Microsoft YaHei UI", 10F),
                Location = new Point(textLeft + 2, y + 68),
                Size = new Size(300, 18),
                ForeColor = Color.FromArgb(110, 110, 110)
            };

            mainContainer.Controls.AddRange(new Control[] { logoPicture, titleLabel, versionLabel, sloganLabel });
            y += 102;

            // ── 分割线 ──
            var divider1 = new AntdUI.Divider { Location = new Point(L, y), Size = new Size(contentW, 1) };
            mainContainer.Controls.Add(divider1);
            y += 14;

            // ════════════════════════════════════════
            //  核心功能
            // ════════════════════════════════════════

            var featuresTitle = new AntdUI.Label
            {
                Text = "核心功能",
                Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold),
                Location = new Point(L, y),
                Size = new Size(contentW, 24),
                ForeColor = Color.FromArgb(38, 38, 38)
            };
            mainContainer.Controls.Add(featuresTitle);
            y += 30;

            string[] features = {
                "🤖  AI 智能对话，理解文档上下文并精准操作",
                "📝  一键全文纠错、润色与格式规范化",
                "📋  引用标准智能校验与交叉引用修复",
                "📊  表格识别与批量格式化处理",
                "🔧  可扩展工具系统，支持自定义 Skill 扩展"
            };

            foreach (var feat in features)
            {
                var lbl = new AntdUI.Label
                {
                    Text = feat,
                    Font = new Font("Microsoft YaHei UI", 10F),
                    Location = new Point(L + 10, y),
                    Size = new Size(contentW - 10, 22),
                    ForeColor = Color.FromArgb(80, 80, 80)
                };
                mainContainer.Controls.Add(lbl);
                y += 24;
            }

            y += 8;

            // ── 分割线 ──
            var divider2 = new AntdUI.Divider { Location = new Point(L, y), Size = new Size(contentW, 1) };
            mainContainer.Controls.Add(divider2);
            y += 14;

            // ════════════════════════════════════════
            //  更新日志（可滚动）
            // ════════════════════════════════════════

            var changelogTitle = new AntdUI.Label
            {
                Text = "更新日志",
                Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold),
                Location = new Point(L, y),
                Size = new Size(contentW, 24),
                ForeColor = Color.FromArgb(38, 38, 38)
            };
            mainContainer.Controls.Add(changelogTitle);
            y += 30;

            string changelog =
                "v2.0.0  (2026-02)\r\n" +
                "  · AI 智能对话，支持上下文感知的文档操作\r\n" +
                "  · 20+ 文档操作工具（编辑/格式化/页面设置等）\r\n" +
                "  · 子智能体：文档结构分析与关键信息提取\r\n" +
                "  · 自定义 Skill 扩展机制\r\n" +
                "  · 开发者调试日志（DeveloperMode）\r\n" +
                "\r\n" +
                "v1.0.0  (2025-03)\r\n" +
                "  · 首个版本发布\r\n" +
                "  · 内置文本纠错、标准校验、表格格式化工具\r\n" +
                "  · 基础 Ribbon 菜单与功能面板";

            var changelogBox = new TextBox
            {
                Text = changelog,
                Font = new Font("Microsoft YaHei UI", 9.5F),
                Location = new Point(L, y),
                Size = new Size(contentW, 120),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(249, 250, 252),
                ForeColor = Color.FromArgb(80, 80, 80),
                BorderStyle = BorderStyle.FixedSingle
            };
            mainContainer.Controls.Add(changelogBox);
            y += 132;

            // ════════════════════════════════════════
            //  底部：联系方式 + 关闭按钮
            // ════════════════════════════════════════

            var contactLabel = new AntdUI.Label
            {
                Text = "联系邮箱：ykf@live.cn",
                Font = new Font("Microsoft YaHei UI", 9.5F),
                Location = new Point(L, y + 8),
                Size = new Size(260, 20),
                ForeColor = Color.FromArgb(140, 140, 140)
            };

            var closeButton = new AntdUI.Button
            {
                Text = "确定",
                Type = AntdUI.TTypeMini.Primary,
                Location = new Point(L + contentW - 84, y + 4),
                Size = new Size(84, 32),
                Radius = 6
            };
            closeButton.Click += (s, e) => Close();

            mainContainer.Controls.AddRange(new Control[] { contactLabel, closeButton });

            // ── 键盘 ESC 关闭 ──
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) Close();
            };

            Controls.Add(mainContainer);
        }
    }
}