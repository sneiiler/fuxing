using AntdUI;
using Newtonsoft.Json.Linq;
using System;
using System.Drawing;
using System.Net.Http;
using System.Windows.Forms;

namespace FuXing
{
    public partial class SettingForm : AntdUI.Window
    {
        private AntdUI.Input baseUrlInput;
        private AntdUI.Input apiKeyInput;
        private AntdUI.Input modelNameInput;
        private AntdUI.Button fetchModelsBtn;
        private AntdUI.Switch devModeSwitch;
        private System.Windows.Forms.ContextMenuStrip modelMenu;
        private static readonly HttpClient _httpClient = new HttpClient();

        public event Action OnConfigUpdated;

        // ── 设计色板（极简纯白） ──
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
            Size = new Size(560, 420);
            MinimumSize = new Size(460, 320);
            MaximizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            BackColor = BG;

            // ═══════════════════════════════════
            //  顶部标题栏（白底 + 底部描线）
            // ═══════════════════════════════════
            var header = new System.Windows.Forms.Panel
            {
                Dock = DockStyle.Top,
                Height = 56,
                BackColor = Color.White
            };
            header.Paint += (s, e) =>
            {
                using (var pen = new Pen(BORDER))
                    e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
            };

            var titleLabel = new System.Windows.Forms.Label
            {
                Text = "\u2699  插件设置",
                Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
                ForeColor = TITLE_FG,
                AutoSize = false,
                Size = new Size(300, 56),
                Location = new Point(24, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent
            };
            header.Controls.Add(titleLabel);

            // ═══════════════════════════════════
            //  底部按钮区
            // ═══════════════════════════════════
            var footer = new System.Windows.Forms.Panel
            {
                Dock = DockStyle.Bottom,
                Height = 60,
                BackColor = Color.White
            };

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

            // ═══════════════════════════════════
            //  中间 —— 可滚动纯白内容区
            // ═══════════════════════════════════
            var scrollPanel = new System.Windows.Forms.Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.White,
                Padding = new Padding(0)
            };

            var content = new System.Windows.Forms.Panel
            {
                BackColor = Color.White,
                Location = new Point(0, 0),
                Size = new Size(520, 420),
                Margin = new Padding(0)
            };

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

            var divider = new System.Windows.Forms.Panel
            {
                Location = new Point(28, 302),
                Size = new Size(460, 1),
                BackColor = BORDER
            };

            var lblDev = new System.Windows.Forms.Label
            {
                Text = "开发者模式",
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                ForeColor = SUBTITLE_FG,
                AutoSize = false,
                Size = new Size(120, 22),
                Location = new Point(28, 322),
                BackColor = Color.Transparent
            };

            var lblDevDesc = new System.Windows.Forms.Label
            {
                Text = "启用后显示调试日志和诊断信息",
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = LABEL_FG,
                AutoSize = false,
                Size = new Size(280, 18),
                Location = new Point(28, 346),
                BackColor = Color.Transparent
            };

            devModeSwitch = new AntdUI.Switch
            {
                Location = new Point(442, 322),
                Size = new Size(50, 28),
                Checked = false
            };

            var spacer = new System.Windows.Forms.Panel
            {
                Location = new Point(0, 420),
                Size = new Size(10, 40),
                BackColor = Color.Transparent
            };

            content.Controls.AddRange(new Control[]
            {
                sectionTitle, lblUrl, baseUrlInput,
                lblKey, apiKeyInput,
                lblModel, modelNameInput, fetchModelsBtn,
                divider, lblDev, lblDevDesc, devModeSwitch,
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
                devModeSwitch.Location = new Point(content.Width - 28 - devModeSwitch.Width, devModeSwitch.Location.Y);
            };

            // ═══════════════════════════════════
            //  组装（顺序：Top → Bottom → Fill）
            // ═══════════════════════════════════
            Controls.Add(scrollPanel);
            Controls.Add(footer);
            Controls.Add(header);
        }

        // ════════════════════════════════════════
        //  工厂方法
        // ════════════════════════════════════════

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

        // ════════════════════════════════════════
        //  业务逻辑
        // ════════════════════════════════════════

        private void ResetButton_Click(object sender, EventArgs e)
        {
            baseUrlInput.Text = "http://127.0.0.1:8000";
            apiKeyInput.Text = "";
            modelNameInput.Text = "";
            devModeSwitch.Checked = false;
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
                var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/models");
                if (!string.IsNullOrWhiteSpace(apiKey))
                    request.Headers.Add("Authorization", $"Bearer {apiKey}");

                using (var response = await _httpClient.SendAsync(request))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        AntdUI.Notification.warn(this, "提示", $"获取失败: HTTP {(int)response.StatusCode}", autoClose: 2);
                        return;
                    }

                    var body = await response.Content.ReadAsStringAsync();
                    var root = JObject.Parse(body);
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
            var config = new ConfigLoader.Config
            {
                BaseURL = baseUrlInput.Text.TrimEnd('/'),
                ApiKey = apiKeyInput.Text,
                ModelName = (modelNameInput.Text ?? string.Empty).Trim(),
                DeveloperMode = devModeSwitch.Checked
            };

            var configLoader = new ConfigLoader();
            configLoader.SaveConfig(config);
            AntdUI.Notification.success(this, "提示", "保存成功！", autoClose: 1);
            OnConfigUpdated?.Invoke();
            Close();
        }

        // ════════════════════════════════════════
        //  现代化菜单配色
        // ════════════════════════════════════════
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

