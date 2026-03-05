using System;
using System.Drawing;
using System.Windows.Forms;

namespace FuXing
{
    /// <summary>
    /// 启动安全提示弹窗 — 提醒用户插件可直接操作文档，建议提前备份。
    /// 继承 AntdUI.Window，不依赖宿主 Form，可独立弹出。
    /// </summary>
    public class StartupWarningDialog : AntdUI.Window
    {
        // ── 设计色板 ──
        static readonly Color BG = Color.White;
        static readonly Color TITLE_FG = Color.FromArgb(17, 24, 39);
        static readonly Color BODY_FG = Color.FromArgb(55, 65, 81);
        static readonly Color HINT_FG = Color.FromArgb(107, 114, 128);
        static readonly Color ACCENT = Color.FromArgb(245, 158, 11);   // 琥珀色
        static readonly Color BORDER = Color.FromArgb(229, 231, 235);

        private AntdUI.Checkbox _dontShowCheck;

        public StartupWarningDialog()
        {
            InitializeComponent();
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;   // 确保弹出在最前
        }

        private void InitializeComponent()
        {
            Text = "使用前请注意";
            Size = new Size(460, 380);
            MaximizeBox = false;
            MinimizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            BackColor = BG;
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };

            // ═══════════════════════
            //  警告图标区域
            // ═══════════════════════
            var iconPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 70,
                BackColor = BG
            };
            iconPanel.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                // 绘制圆形琥珀色背景
                int circleSize = 52;
                int cx = (iconPanel.Width - circleSize) / 2;
                int cy = 10;
                using (var brush = new SolidBrush(Color.FromArgb(254, 243, 199)))
                    g.FillEllipse(brush, cx, cy, circleSize, circleSize);
                // 绘制感叹号
                using (var font = new Font("Segoe UI", 26F, FontStyle.Bold))
                using (var brush = new SolidBrush(ACCENT))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString("!", font, brush, new RectangleF(cx, cy, circleSize, circleSize), sf);
                }
            };

            // ═══════════════════════
            //  标题
            // ═══════════════════════
            var titleLabel = new Label
            {
                Text = "使用前请注意",
                Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold),
                ForeColor = TITLE_FG,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = BG,
                Dock = DockStyle.Top,
                Height = 40
            };

            // ═══════════════════════
            //  正文
            // ═══════════════════════
            var bodyLabel = new Label
            {
                Text = "本插件可直接读取和修改当前 Word 文档内容，" +
                       "包括文本、格式、表格、图片等所有元素。\n\n" +
                       "为避免意外修改导致数据丢失，" +
                       "请提前保存或备份您的文档。",
                Font = new Font("Microsoft YaHei UI", 10.5F),
                ForeColor = BODY_FG,
                TextAlign = ContentAlignment.TopCenter,
                BackColor = BG,
                Dock = DockStyle.Top,
                Height = 90,
                Padding = new Padding(32, 8, 32, 0)
            };

            // ═══════════════════════
            //  底部区域：复选框 + 按钮
            // ═══════════════════════
            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 100,
                BackColor = BG
            };

            _dontShowCheck = new AntdUI.Checkbox
            {
                Text = "不再显示此提示",
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = HINT_FG,
                BackColor = Color.Transparent,
                Size = new Size(160, 28),
                Checked = false
            };

            var confirmBtn = new AntdUI.Button
            {
                Text = "我知道了",
                Type = AntdUI.TTypeMini.Primary,
                Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
                Size = new Size(200, 42),
                Radius = 21
            };
            confirmBtn.Click += ConfirmBtn_Click;

            footer.Controls.AddRange(new Control[] { _dontShowCheck, confirmBtn });

            // 底部区域响应式居中
            footer.Resize += (s, e) =>
            {
                int fw = footer.ClientSize.Width;
                _dontShowCheck.Location = new Point((fw - _dontShowCheck.Width) / 2, 8);
                confirmBtn.Location = new Point((fw - confirmBtn.Width) / 2, 46);
            };

            // ═══════════════════════
            //  分割线
            // ═══════════════════════
            var divider = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = BORDER
            };

            // ═══════════════════════
            //  组装（顺序：Top → Bottom → Fill）
            // ═══════════════════════
            Controls.Add(bodyLabel);        // Fill 区域
            Controls.Add(titleLabel);       // Top
            Controls.Add(iconPanel);        // Top
            Controls.Add(divider);          // Bottom
            Controls.Add(footer);           // Bottom
        }

        private void ConfirmBtn_Click(object sender, EventArgs e)
        {
            if (_dontShowCheck.Checked)
            {
                var configLoader = new ConfigLoader();
                var cfg = configLoader.LoadConfig();
                cfg.ShowStartupWarning = false;
                configLoader.SaveConfig(cfg);
            }
            Close();
        }
    }
}
