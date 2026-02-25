using AntdUI;
using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace WordTools
{
    public partial class AboutDialog : AntdUI.Window
    {
        public AboutDialog()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            // 基础窗体设置
            Text = "关于 WordTools";
            Size = new Size(500, 500);
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            BackColor = Color.White;
            KeyPreview = true;  // 启用键盘事件预览
            
            // 创建主容器面板，使用 FlowLayoutPanel 实现垂直布局
            var mainContainer = new AntdUI.Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(30, 30, 30, 30),  // 统一边距30
                BackColor = Color.White
            };

            // 头部区域 - 图标和标题
            var headerPanel = new AntdUI.Panel
            {
                Size = new Size(430, 80),
                Location = new Point(0, 0),
                BackColor = Color.White
            };

            // 产品图标 (使用 AntdUI Avatar 组件)
            var productIcon = new AntdUI.Avatar
            {
                Size = new Size(48, 48),
                Location = new Point(0, 16),
                Text = "WT",
                BackColor = Color.FromArgb(24, 144, 255),
                ForeColor = Color.White,
                // Shape = AntdUI.TShape.Circle  // 注释掉不支持的属性
            };

            // 产品标题
            var titleLabel = new AntdUI.Label
            {
                Text = "WordTools",
                Font = new Font("Microsoft YaHei UI", 20F, FontStyle.Bold),
                Location = new Point(64, 8),
                Size = new Size(200, 32),
                ForeColor = Color.FromArgb(38, 38, 38)
            };

            // 副标题
            var subtitleLabel = new AntdUI.Label
            {
                Text = "Microsoft Word 智能插件",
                Font = new Font("Microsoft YaHei UI", 14F),
                Location = new Point(64, 44),
                Size = new Size(300, 24),
                ForeColor = Color.FromArgb(140, 140, 140)
            };

            headerPanel.Controls.AddRange(new Control[] { productIcon, titleLabel, subtitleLabel });

            // 版本信息卡片
            var versionCard = new AntdUI.Panel
            {
                Location = new Point(0, 100),
                Size = new Size(430, 50),
                BorderWidth = 1,
                BorderColor = Color.FromArgb(240, 240, 240),
                Radius = 6,
                Back = Color.FromArgb(250, 250, 250),
                Padding = new Padding(16, 12, 16, 12)
            };

            var versionLabel = new AntdUI.Label
            {
                Text = $"版本: {Assembly.GetExecutingAssembly().GetName().Version}",
                Font = new Font("Microsoft YaHei UI", 12F),
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(102, 102, 102),
                TextAlign = ContentAlignment.MiddleLeft
            };

            versionCard.Controls.Add(versionLabel);

            // 功能特性区域
            var featuresTitle = new AntdUI.Label
            {
                Text = "主要功能",
                Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
                Location = new Point(0, 170),
                Size = new Size(430, 24),
                ForeColor = Color.FromArgb(38, 38, 38)
            };

            // 功能列表容器
            var featuresPanel = new AntdUI.Panel
            {
                Location = new Point(0, 202),
                Size = new Size(430, 120),
                BackColor = Color.White
            };

            // 创建功能项
            string[] features = {
                "🤖 AI文本纠错与优化",
                "📋 引用标准智能校验", 
                "📊 表格格式批量处理",
                "💬 智能批注系统",
                "🎨 现代化用户界面"
            };

            for (int i = 0; i < features.Length; i++)
            {
                var featureLabel = new AntdUI.Label
                {
                    Text = features[i],
                    Font = new Font("Microsoft YaHei UI", 11F),
                    Location = new Point(16, i * 24),
                    Size = new Size(420, 20),
                    ForeColor = Color.FromArgb(102, 102, 102)
                };
                featuresPanel.Controls.Add(featureLabel);
            }

            // 技术栈信息
            var techTitle = new AntdUI.Label
            {
                Text = "技术栈",
                Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
                Location = new Point(0, 342),
                Size = new Size(430, 24),
                ForeColor = Color.FromArgb(38, 38, 38)
            };

            var techLabel = new AntdUI.Label
            {
                Text = ".NET Framework 4.7  |  NetOffice  |  AntdUI",
                Font = new Font("Microsoft YaHei UI", 10F),
                Location = new Point(0, 374),
                Size = new Size(430, 20),
                ForeColor = Color.FromArgb(140, 140, 140),
                TextAlign = ContentAlignment.MiddleCenter
            };

            // 底部按钮区域
            var buttonPanel = new AntdUI.Panel
            {
                Location = new Point(0, 410),
                Size = new Size(430, 50),
                BackColor = Color.White
            };

            var closeButton = new AntdUI.Button
            {
                Text = "确定",
                Type = AntdUI.TTypeMini.Primary,
                Location = new Point(350, 10),
                Size = new Size(80, 32),
                Radius = 6
            };

            closeButton.Click += (s, e) => Close();
            buttonPanel.Controls.Add(closeButton);

            // 添加键盘事件处理
            KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Escape)
                {
                    Close();
                }
            };

            // 添加所有控件到主容器
            mainContainer.Controls.AddRange(new Control[] {
                headerPanel, versionCard, featuresTitle, featuresPanel, 
                techTitle, techLabel, buttonPanel
            });

            Controls.Add(mainContainer);
        }
    }
}