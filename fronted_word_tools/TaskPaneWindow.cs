using AntdUI;
using System;
using System.Windows.Forms;

namespace WordTools
{
    public partial class TaskPaneWindow : AntdUI.Window
    {
        public TaskPaneWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            // 基础窗体设置
            Text = "WordTools 工具面板";
            Size = new System.Drawing.Size(320, 600);
            StartPosition = FormStartPosition.Manual;
            MaximizeBox = false;
            MinimizeBox = false;
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            ShowInTaskbar = false;
            TopMost = true;
            
            // 设置窗口位置到屏幕右侧
            var screen = Screen.PrimaryScreen;
            Location = new System.Drawing.Point(screen.WorkingArea.Right - Width - 10, 100);
            
            // 主面板
            var mainPanel = new AntdUI.Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(15)
            };

            // 标题
            var titleLabel = new AntdUI.Label
            {
                Text = "WordTools 工具箱",
                Font = new System.Drawing.Font("Microsoft YaHei UI", 14F, System.Drawing.FontStyle.Bold),
                Location = new System.Drawing.Point(15, 15),
                Size = new System.Drawing.Size(290, 35),
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                ForeColor = System.Drawing.Color.FromArgb(24, 144, 255)
            };

            // AI工具组
            var aiGroup = new AntdUI.Divider
            {
                Text = "AI智能工具",
                Location = new System.Drawing.Point(15, 60),
                Size = new System.Drawing.Size(290, 30)
            };

            var textCorrectionBtn = new AntdUI.Button
            {
                Text = "选中文本纠错",
                Location = new System.Drawing.Point(15, 100),
                Size = new System.Drawing.Size(290, 40),
                Type = AntdUI.TTypeMini.Primary
            };

            var allTextCorrectionBtn = new AntdUI.Button
            {
                Text = "全文文本纠错", 
                Location = new System.Drawing.Point(15, 150),
                Size = new System.Drawing.Size(290, 40)
            };

            // 标准校验组
            var standardGroup = new AntdUI.Divider
            {
                Text = "标准校验",
                Location = new System.Drawing.Point(15, 210),
                Size = new System.Drawing.Size(290, 30)
            };

            var standardCheckBtn = new AntdUI.Button
            {
                Text = "校验引用标准",
                Location = new System.Drawing.Point(15, 250),
                Size = new System.Drawing.Size(290, 40)
            };

            // 表格工具组
            var tableGroup = new AntdUI.Divider
            {
                Text = "表格格式化",
                Location = new System.Drawing.Point(15, 310),
                Size = new System.Drawing.Size(290, 30)
            };

            var formatTableBtn = new AntdUI.Button
            {
                Text = "格式化选中表格",
                Location = new System.Drawing.Point(15, 350),
                Size = new System.Drawing.Size(290, 40)
            };

            var formatAllTablesBtn = new AntdUI.Button
            {
                Text = "格式化全部表格",
                Location = new System.Drawing.Point(15, 400),
                Size = new System.Drawing.Size(290, 40)
            };

            // 设置组
            var settingsGroup = new AntdUI.Divider
            {
                Text = "设置选项",
                Location = new System.Drawing.Point(15, 460),
                Size = new System.Drawing.Size(290, 30)
            };

            var configBtn = new AntdUI.Button
            {
                Text = "配置设置",
                Location = new System.Drawing.Point(15, 500),
                Size = new System.Drawing.Size(140, 35),
                Type = AntdUI.TTypeMini.Default
            };

            var aboutBtn = new AntdUI.Button
            {
                Text = "关于",
                Location = new System.Drawing.Point(165, 500),
                Size = new System.Drawing.Size(140, 35),
                Type = AntdUI.TTypeMini.Default
            };

            // 事件绑定
            textCorrectionBtn.Click += (s, e) => TriggerWordFunction("ai_text_correction_btn");
            allTextCorrectionBtn.Click += (s, e) => TriggerWordFunction("ai_text_correction_all_btn");
            standardCheckBtn.Click += (s, e) => TriggerWordFunction("CheckStandardValidityButton");
            formatTableBtn.Click += (s, e) => TriggerWordFunction("FormatTableStyleButton");
            formatAllTablesBtn.Click += (s, e) => TriggerWordFunction("table_all_style_format_btn");
            configBtn.Click += (s, e) => TriggerWordFunction("setting_btn");
            aboutBtn.Click += (s, e) => TriggerWordFunction("about_btn");

            // 添加控件
            mainPanel.Controls.AddRange(new Control[] {
                titleLabel, aiGroup, textCorrectionBtn, allTextCorrectionBtn,
                standardGroup, standardCheckBtn, tableGroup, formatTableBtn, formatAllTablesBtn,
                settingsGroup, configBtn, aboutBtn
            });

            Controls.Add(mainPanel);
        }

        private void TriggerWordFunction(string functionName)
        {
            try
            {
                // 获取当前的Connect实例
                var connectInstance = Connect.CurrentInstance;
                if (connectInstance == null)
                {
                    AntdUI.Notification.error(this, "错误", "无法获取WordTools插件实例");
                    return;
                }

                // 根据功能名称调用相应的方法
                switch (functionName)
                {
                    case "ai_text_correction_btn":
                        connectInstance.ai_text_correction_btn_Click(null);
                        break;
                    case "ai_text_correction_all_btn":
                        connectInstance.ai_text_correction_all_btn_Click(null);
                        break;
                    case "CheckStandardValidityButton":
                        connectInstance.CheckStandardValidityButton_Click(null);
                        break;
                    case "FormatTableStyleButton":
                        connectInstance.FormatTableStyleButton_Click(null);
                        break;
                    case "table_all_style_format_btn":
                        connectInstance.table_all_style_format_btn_Click(null);
                        break;
                    case "setting_btn":
                        connectInstance.setting_btn_Click(null);
                        break;
                    case "about_btn":
                        connectInstance.about_btn_Click(null);
                        break;
                }
            }
            catch (Exception ex)
            {
                AntdUI.Notification.error(this, "操作失败", "执行操作时出错: " + ex.Message);
            }
        }
    }
}