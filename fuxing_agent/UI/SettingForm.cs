using AntdUI;
using Newtonsoft.Json.Linq;
using OpenAI;
using System;
using System.ClientModel;
using System.Drawing;
using System.Windows.Forms;
using FuXingAgent.Core;

namespace FuXingAgent
{
    public partial class SettingForm : AntdUI.Window
    {
        private AntdUI.Input baseUrlInput;
        private AntdUI.Input apiKeyInput;
        private AntdUI.Input modelNameInput;
        private AntdUI.Button fetchModelsBtn;
        private AntdUI.Switch devModeSwitch;
        private AntdUI.Switch approvalSwitch;
        private AntdUI.Switch startupWarningSwitch;
        private AntdUI.Select contextWindowSelect;
        private AntdUI.Select maxToolRoundsSelect;
        private AntdUI.Select maxScriptIterationsSelect;
        private System.Windows.Forms.ContextMenuStrip modelMenu;

        public event Action OnConfigUpdated;

        static readonly Color BG = Color.White;
        static readonly Color BORDER = Color.FromArgb(228, 231, 236);
        static readonly Color LABEL_FG = Color.FromArgb(107, 114, 128);
        static readonly Color TITLE_FG = Color.FromArgb(17, 24, 39);
        static readonly Color SUBTITLE_FG = Color.FromArgb(55, 65, 81);
        static readonly Color INPUT_BG = Color.FromArgb(249, 250, 251);

        public SettingForm()
        {
            InitializeComponent();
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            LoadConfig();
        }

        private void InitializeComponent()
        {
            Text = "插件设置";
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Size = new Size(560, 780);
            MinimumSize = new Size(460, 680);
            MaximizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            BackColor = BG;

            var header = new System.Windows.Forms.Panel { Dock = DockStyle.Top, Height = 56, BackColor = Color.White };
            header.Paint += (s, e) =>
            {
                using (var pen = new Pen(BORDER))
                    e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
            };

            var logoBox = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(28, 28),
                Location = new Point(24, 14),
                BackColor = Color.Transparent
            };
            try
            {
                var logo = ResourceManager.GetIcon("logo.png");
                if (logo != null) logoBox.Image = new Bitmap(logo, 28, 28);
            }
            catch { }

            var titleLabel = new System.Windows.Forms.Label
            {
                Text = "插件设置",
                Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
                ForeColor = TITLE_FG,
                AutoSize = false,
                Size = new Size(260, 56),
                Location = new Point(58, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent
            };
            header.Controls.Add(logoBox);
            header.Controls.Add(titleLabel);

            var footer = new System.Windows.Forms.Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Color.White };
            var saveBtn = MakeButton("保存设置", AntdUI.TTypeMini.Primary, 96, true);
            var cancelBtn = MakeButton("取消", AntdUI.TTypeMini.Default, 72, false);
            var resetBtn = MakeButton("重置默认", AntdUI.TTypeMini.Default, 88, false);
            saveBtn.Click += setting_confirm_btn_Click;
            cancelBtn.Click += (s, e) => Close();
            resetBtn.Click += ResetButton_Click;
            footer.Resize += (s, e) =>
            {
                int right = footer.ClientSize.Width - 24;
                saveBtn.Location = new Point(right - saveBtn.Width, 10);
                cancelBtn.Location = new Point(saveBtn.Left - 10 - cancelBtn.Width, 10);
                resetBtn.Location = new Point(cancelBtn.Left - 10 - resetBtn.Width, 10);
            };
            footer.Controls.AddRange(new Control[] { saveBtn, cancelBtn, resetBtn });

            var scrollPanel = new System.Windows.Forms.Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White, Padding = new Padding(0) };
            var content = new System.Windows.Forms.Panel { BackColor = Color.White, Location = new Point(0, 0), Size = new Size(520, 840), Margin = new Padding(0) };

            var sectionTitle = new System.Windows.Forms.Label
            {
                Text = "大模型服务",
                Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
                ForeColor = SUBTITLE_FG,
                AutoSize = false,
                Size = new Size(240, 24),
                Location = new Point(28, 20),
                BackColor = Color.Transparent
            };

            var lblUrl = MakeLabel("Base URL", 28, 60);
            baseUrlInput = MakeInput("http://127.0.0.1:8000", 28, 84);

            var lblKey = MakeLabel("API Key (SK)", 28, 140);
            apiKeyInput = MakeInput("sk-...", 28, 164);
            apiKeyInput.UseSystemPasswordChar = true;

            var lblModel = MakeLabel("模型名称", 28, 220);
            modelNameInput = MakeInput("点击右侧获取可用模型", 28, 244);

            fetchModelsBtn = new AntdUI.Button
            {
                Text = "获取",
                Type = AntdUI.TTypeMini.Default,
                Size = new Size(56, 34),
                Location = new Point(432, 244),
                Radius = 8,
                Font = new Font("Microsoft YaHei UI", 9F)
            };
            fetchModelsBtn.Click += FetchModelsBtn_Click;

            modelMenu = new System.Windows.Forms.ContextMenuStrip
            {
                Font = new Font("Microsoft YaHei UI", 9.5F),
                ShowImageMargin = false,
                BackColor = Color.White,
                ForeColor = TITLE_FG,
                Renderer = new ToolStripProfessionalRenderer(new ModernMenuColors())
            };

            var divider = new System.Windows.Forms.Panel { Location = new Point(28, 302), Size = new Size(460, 1), BackColor = BORDER };

            var lblContext = new System.Windows.Forms.Label { Text = "上下文窗口", Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold), ForeColor = SUBTITLE_FG, AutoSize = false, Size = new Size(120, 22), Location = new Point(28, 322), BackColor = Color.Transparent };
            var lblContextDesc = new System.Windows.Forms.Label { Text = "越大越智能但更慢更贵，越小越快越省", Font = new Font("Microsoft YaHei UI", 8.5F), ForeColor = LABEL_FG, AutoSize = false, Size = new Size(280, 18), Location = new Point(28, 346), BackColor = Color.Transparent };
            contextWindowSelect = new AntdUI.Select { Size = new Size(120, 32), Location = new Point(420, 322), Font = new Font("Microsoft YaHei UI", 9F), Name = "ContextWindowSelect" };
            contextWindowSelect.Items.AddRange(new object[] { "32K", "64K", "128K" });
            contextWindowSelect.SelectedIndex = 2;

            var divider2 = new System.Windows.Forms.Panel { Location = new Point(28, 382), Size = new Size(460, 1), BackColor = BORDER };

            var lblMaxRounds = new System.Windows.Forms.Label { Text = "最大迭代轮次", Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold), ForeColor = SUBTITLE_FG, AutoSize = false, Size = new Size(120, 22), Location = new Point(28, 402), BackColor = Color.Transparent };
            var lblMaxRoundsDesc = new System.Windows.Forms.Label { Text = "智能体工具调用循环上限，越大则越复杂任务可完成", Font = new Font("Microsoft YaHei UI", 8.5F), ForeColor = LABEL_FG, AutoSize = false, Size = new Size(300, 18), Location = new Point(28, 426), BackColor = Color.Transparent };
            maxToolRoundsSelect = new AntdUI.Select { Size = new Size(120, 32), Location = new Point(420, 402), Font = new Font("Microsoft YaHei UI", 9F), Name = "MaxToolRoundsSelect" };
            maxToolRoundsSelect.Items.AddRange(new object[] { "10", "20", "50", "100" });
            maxToolRoundsSelect.SelectedIndex = 2;

            var divider4 = new System.Windows.Forms.Panel { Location = new Point(28, 462), Size = new Size(460, 1), BackColor = BORDER };
            var lblDev = new System.Windows.Forms.Label { Text = "开发者模式", Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold), ForeColor = SUBTITLE_FG, AutoSize = false, Size = new Size(120, 22), Location = new Point(28, 482), BackColor = Color.Transparent };
            var lblDevDesc = new System.Windows.Forms.Label { Text = "启用后显示调试日志和诊断信息", Font = new Font("Microsoft YaHei UI", 8.5F), ForeColor = LABEL_FG, AutoSize = false, Size = new Size(280, 18), Location = new Point(28, 506), BackColor = Color.Transparent };
            devModeSwitch = new AntdUI.Switch { Location = new Point(442, 482), Size = new Size(50, 28), Checked = false };

            var divider5 = new System.Windows.Forms.Panel { Location = new Point(28, 542), Size = new Size(460, 1), BackColor = BORDER };
            var lblApproval = new System.Windows.Forms.Label { Text = "操作审批确认", Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold), ForeColor = SUBTITLE_FG, AutoSize = false, Size = new Size(140, 22), Location = new Point(28, 562), BackColor = Color.Transparent };
            var lblApprovalDesc = new System.Windows.Forms.Label { Text = "执行脚本修改、批量操作、删除章节等操作前需要确认", Font = new Font("Microsoft YaHei UI", 8.5F), ForeColor = LABEL_FG, AutoSize = false, Size = new Size(340, 18), Location = new Point(28, 586), BackColor = Color.Transparent };
            approvalSwitch = new AntdUI.Switch { Location = new Point(442, 562), Size = new Size(50, 28), Checked = true };

            var divider6 = new System.Windows.Forms.Panel { Location = new Point(28, 622), Size = new Size(460, 1), BackColor = BORDER };
            var lblWarning = new System.Windows.Forms.Label { Text = "启动安全提示", Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold), ForeColor = SUBTITLE_FG, AutoSize = false, Size = new Size(140, 22), Location = new Point(28, 642), BackColor = Color.Transparent };
            var lblWarningDesc = new System.Windows.Forms.Label { Text = "每次打开插件时提醒备份文档", Font = new Font("Microsoft YaHei UI", 8.5F), ForeColor = LABEL_FG, AutoSize = false, Size = new Size(300, 18), Location = new Point(28, 666), BackColor = Color.Transparent };
            startupWarningSwitch = new AntdUI.Switch { Location = new Point(442, 642), Size = new Size(50, 28), Checked = true };

            var divider7 = new System.Windows.Forms.Panel { Location = new Point(28, 702), Size = new Size(460, 1), BackColor = BORDER };
            var lblMaxIter = new System.Windows.Forms.Label { Text = "脚本循环次数限制", Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold), ForeColor = SUBTITLE_FG, AutoSize = false, Size = new Size(160, 22), Location = new Point(28, 722), BackColor = Color.Transparent };
            var lblMaxIterDesc = new System.Windows.Forms.Label { Text = "脚本中循环结构的最大允许迭代次数，防止无限循环", Font = new Font("Microsoft YaHei UI", 8.5F), ForeColor = LABEL_FG, AutoSize = false, Size = new Size(340, 18), Location = new Point(28, 746), BackColor = Color.Transparent };
            maxScriptIterationsSelect = new AntdUI.Select { Size = new Size(120, 32), Location = new Point(420, 722), Font = new Font("Microsoft YaHei UI", 9F), Name = "MaxScriptIterationsSelect" };
            maxScriptIterationsSelect.Items.AddRange(new object[] { "20", "100", "200", "1000" });
            maxScriptIterationsSelect.SelectedIndex = 2;

            var spacer = new System.Windows.Forms.Panel { Location = new Point(0, 800), Size = new Size(10, 40), BackColor = Color.Transparent };

            content.Controls.AddRange(new Control[]
            {
                sectionTitle, lblUrl, baseUrlInput,
                lblKey, apiKeyInput,
                lblModel, modelNameInput, fetchModelsBtn,
                divider,
                lblContext, lblContextDesc, contextWindowSelect,
                divider2,
                lblMaxRounds, lblMaxRoundsDesc, maxToolRoundsSelect,
                divider4,
                lblDev, lblDevDesc, devModeSwitch,
                divider5,
                lblApproval, lblApprovalDesc, approvalSwitch,
                divider6,
                lblWarning, lblWarningDesc, startupWarningSwitch,
                divider7,
                lblMaxIter, lblMaxIterDesc, maxScriptIterationsSelect,
                spacer
            });

            scrollPanel.Controls.Add(content);
            scrollPanel.Resize += (s, e) =>
            {
                int panelWidth = scrollPanel.ClientSize.Width;
                content.Width = Math.Max(panelWidth, 520);
                int inputWidth = Math.Max(220, content.Width - 56);
                int modelInputW = Math.Max(160, inputWidth - fetchModelsBtn.Width - 8);
                baseUrlInput.Width = inputWidth;
                apiKeyInput.Width = inputWidth;
                modelNameInput.Width = modelInputW;
                fetchModelsBtn.Location = new Point(modelNameInput.Right + 8, fetchModelsBtn.Location.Y);
                divider.Width = inputWidth;
                divider2.Width = inputWidth;
                divider4.Width = inputWidth;
                divider5.Width = inputWidth;
                divider6.Width = inputWidth;
                divider7.Width = inputWidth;
                contextWindowSelect.Location = new Point(content.Width - 28 - contextWindowSelect.Width, contextWindowSelect.Location.Y);
                maxToolRoundsSelect.Location = new Point(content.Width - 28 - maxToolRoundsSelect.Width, maxToolRoundsSelect.Location.Y);
                devModeSwitch.Location = new Point(content.Width - 28 - devModeSwitch.Width, devModeSwitch.Location.Y);
                approvalSwitch.Location = new Point(content.Width - 28 - approvalSwitch.Width, approvalSwitch.Location.Y);
                startupWarningSwitch.Location = new Point(content.Width - 28 - startupWarningSwitch.Width, startupWarningSwitch.Location.Y);
                maxScriptIterationsSelect.Location = new Point(content.Width - 28 - maxScriptIterationsSelect.Width, maxScriptIterationsSelect.Location.Y);
            };

            Controls.Add(scrollPanel);
            Controls.Add(footer);
            Controls.Add(header);
        }

        private static System.Windows.Forms.Label MakeLabel(string text, int x, int y)
        {
            return new System.Windows.Forms.Label
            {
                Text = text,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = LABEL_FG,
                AutoSize = false,
                Size = new Size(200, 18),
                Location = new Point(x, y),
                BackColor = Color.Transparent
            };
        }

        private static AntdUI.Input MakeInput(string placeholder, int x, int y)
        {
            return new AntdUI.Input
            {
                Location = new Point(x, y),
                Size = new Size(400, 34),
                PlaceholderText = placeholder,
                BorderWidth = 1,
                BorderColor = BORDER,
                BackColor = INPUT_BG,
                Radius = 8,
                Font = new Font("Microsoft YaHei UI", 9.5F)
            };
        }

        private static AntdUI.Button MakeButton(string text, AntdUI.TTypeMini type, int width, bool bold)
        {
            return new AntdUI.Button
            {
                Text = text,
                Type = type,
                Size = new Size(width, 36),
                Radius = 8,
                Font = new Font("Microsoft YaHei UI", 9.5F, bold ? FontStyle.Bold : FontStyle.Regular)
            };
        }

        private void ResetButton_Click(object sender, EventArgs e)
        {
            baseUrlInput.Text = "http://127.0.0.1:8000";
            apiKeyInput.Text = "";
            modelNameInput.Text = "";
            devModeSwitch.Checked = false;
            contextWindowSelect.SelectedIndex = 2;
            maxToolRoundsSelect.SelectedIndex = 2;
            maxScriptIterationsSelect.SelectedIndex = 2;
            approvalSwitch.Checked = true;
            startupWarningSwitch.Checked = true;
            AntdUI.Notification.info(this, "提示", "已重置为默认设置", autoClose: 2);
        }

        private void LoadConfig()
        {
            var configLoader = new ConfigLoader();
            var config = configLoader.LoadConfig();
            baseUrlInput.Text = config.BaseURL ?? "http://127.0.0.1:8000";
            apiKeyInput.Text = config.ApiKey ?? "";
            modelNameInput.Text = config.ModelName ?? "";
            devModeSwitch.Checked = config.DeveloperMode;
            approvalSwitch.Checked = config.RequireApprovalForDangerousTools;
            startupWarningSwitch.Checked = config.ShowStartupWarning;

            switch (config.ContextWindowLimit)
            {
                case 32000: contextWindowSelect.SelectedIndex = 0; break;
                case 64000: contextWindowSelect.SelectedIndex = 1; break;
                default: contextWindowSelect.SelectedIndex = 2; break;
            }

            switch (config.MaxToolRounds)
            {
                case 10: maxToolRoundsSelect.SelectedIndex = 0; break;
                case 20: maxToolRoundsSelect.SelectedIndex = 1; break;
                case 100: maxToolRoundsSelect.SelectedIndex = 3; break;
                default: maxToolRoundsSelect.SelectedIndex = 2; break;
            }

            switch (config.MaxScriptIterations)
            {
                case 20: maxScriptIterationsSelect.SelectedIndex = 0; break;
                case 100: maxScriptIterationsSelect.SelectedIndex = 1; break;
                case 1000: maxScriptIterationsSelect.SelectedIndex = 3; break;
                default: maxScriptIterationsSelect.SelectedIndex = 2; break;
            }
        }

        private async void FetchModelsBtn_Click(object sender, EventArgs e)
        {
            var baseUrl = (baseUrlInput.Text ?? string.Empty).Trim().TrimEnd('/');
            var apiKey = apiKeyInput.Text ?? string.Empty;

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                AntdUI.Notification.warn(this, "提示", "请先填写 Base URL", autoClose: 2);
                return;
            }

            fetchModelsBtn.Loading = true;
            try
            {
                var options = new OpenAIClientOptions { Endpoint = new Uri(baseUrl) };
                var openaiClient = new OpenAIClient(new ApiKeyCredential(apiKey ?? "dummy"), options);
                var modelClient = openaiClient.GetOpenAIModelClient();

                var modelResult = await modelClient.GetModelsAsync();
                var rawBody = modelResult.GetRawResponse().Content.ToString();
                var root = JObject.Parse(rawBody);
                var data = root["data"] as JArray;

                modelMenu.Items.Clear();
                if (data != null)
                {
                    foreach (var item in data)
                    {
                        var modelId = item?["id"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(modelId))
                        {
                            var menuItem = new ToolStripMenuItem(modelId);
                            menuItem.Click += (ms, me) => { modelNameInput.Text = modelId; };
                            modelMenu.Items.Add(menuItem);
                        }
                    }
                }

                if (modelMenu.Items.Count == 0)
                {
                    AntdUI.Notification.warn(this, "提示", "未获取到可用模型", autoClose: 2);
                }
                else
                {
                    var location = modelNameInput.PointToScreen(new Point(0, modelNameInput.Height));
                    modelMenu.Show(location);
                }
            }
            catch (Exception ex)
            {
                AntdUI.Notification.warn(this, "提示", $"获取失败: {ex.Message}", autoClose: 3);
            }
            finally
            {
                fetchModelsBtn.Loading = false;
            }
        }

        private void setting_confirm_btn_Click(object sender, EventArgs e)
        {
            int contextLimit = 128000;
            switch (contextWindowSelect.SelectedIndex)
            {
                case 0: contextLimit = 32000; break;
                case 1: contextLimit = 64000; break;
                case 2: contextLimit = 128000; break;
            }

            int maxToolRounds = 50;
            switch (maxToolRoundsSelect.SelectedIndex)
            {
                case 0: maxToolRounds = 10; break;
                case 1: maxToolRounds = 20; break;
                case 2: maxToolRounds = 50; break;
                case 3: maxToolRounds = 100; break;
            }

            int maxScriptIterations = 200;
            switch (maxScriptIterationsSelect.SelectedIndex)
            {
                case 0: maxScriptIterations = 20; break;
                case 1: maxScriptIterations = 100; break;
                case 2: maxScriptIterations = 200; break;
                case 3: maxScriptIterations = 1000; break;
            }

            var config = new ConfigLoader.Config
            {
                BaseURL = baseUrlInput.Text.TrimEnd('/'),
                ApiKey = apiKeyInput.Text,
                ModelName = (modelNameInput.Text ?? string.Empty).Trim(),
                DeveloperMode = devModeSwitch.Checked,
                ContextWindowLimit = contextLimit,
                MaxToolRounds = maxToolRounds,
                MaxScriptIterations = maxScriptIterations,
                RequireApprovalForDangerousTools = approvalSwitch.Checked,
                ShowStartupWarning = startupWarningSwitch.Checked
            };

            var configLoader = new ConfigLoader();
            configLoader.SaveConfig(config);
            AntdUI.Notification.success(this, "提示", "保存成功！", autoClose: 1);
            OnConfigUpdated?.Invoke();
            Close();
        }

        private class ModernMenuColors : ProfessionalColorTable
        {
            public override Color MenuItemSelected => Color.FromArgb(240, 244, 248);
            public override Color MenuItemSelectedGradientBegin => Color.FromArgb(240, 244, 248);
            public override Color MenuItemSelectedGradientEnd => Color.FromArgb(240, 244, 248);
            public override Color MenuItemBorder => Color.FromArgb(228, 231, 236);
            public override Color MenuBorder => Color.FromArgb(228, 231, 236);
            public override Color MenuItemPressedGradientBegin => Color.FromArgb(230, 234, 240);
            public override Color MenuItemPressedGradientEnd => Color.FromArgb(230, 234, 240);
        }
    }
}
