using AntdUI;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace WordTools
{
    public partial class SettingForm : AntdUI.Window
    {
        // 定义输入控件
        private AntdUI.Input llmServerIP_text;
        private AntdUI.Input llmServerPort_text;
        private AntdUI.Input updatePort_text;
        private AntdUI.Input otherPort_text;
        private AntdUI.Input CheckStandardIP_text;
        private AntdUI.Input CheckStandardPort_text;

        // 定义一个事件用于通知配置更新
        public event Action OnConfigUpdated;
        
        public SettingForm()
        {
            InitializeComponent();
            // 设置窗体在屏幕中间显示
            this.StartPosition = FormStartPosition.CenterScreen;
            // 支持ESC键关闭
            this.KeyPreview = true;
            this.KeyDown += SettingForm_KeyDown;
            // 加载配置并设置默认值
            LoadConfig();
        }

        private void SettingForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                this.Close();
            }
        }

        private void InitializeComponent()
        {
            // 基础窗体设置 - 支持缩放
            Text = "插件设置";
            Size = new Size(800, 650);  // 增加高度以容纳更多内容
            MinimumSize = new Size(600, 500);  // 增加最小高度
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = true;
            MinimizeBox = true;
            FormBorderStyle = FormBorderStyle.Sizable;  // 允许调整大小
            BackColor = Color.FromArgb(248, 249, 250);

            // 主容器使用TableLayoutPanel实现响应式布局
            var mainContainer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(248, 249, 250),
                Padding = new Padding(20),
                RowCount = 4,
                ColumnCount = 1
            };

            // 设置行的大小类型
            mainContainer.RowStyles.Add(new RowStyle(SizeType.Absolute, 70F)); // 标题行 - 固定高度
            mainContainer.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // 内容区域
            mainContainer.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 按钮行
            mainContainer.RowStyles.Add(new RowStyle(SizeType.Absolute, 10F)); // 底部间距

            // 标题
            var titleLabel = new AntdUI.Label
            {
                Text = "插件设置",
                Font = new Font("Microsoft YaHei UI", 20F, FontStyle.Bold),
                ForeColor = Color.FromArgb(38, 38, 38),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 10, 0, 10)
            };

            // 内容区域 - 2x2网格布局
            var contentPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                RowCount = 2,
                ColumnCount = 2,
                Margin = new Padding(0, 0, 0, 20)
            };

            // 设置内容区域的行列样式 - 增加行高以容纳更多内容
            contentPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 200F)); // 第一行固定200px
            contentPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));   // 第二行占用剩余空间
            contentPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            contentPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            // 创建四个配置卡片 - 2x2布局
            var aiServerCard = CreateConfigCard("AI 服务器设置", Color.FromArgb(24, 144, 255));
            var standardServerCard = CreateConfigCard("标准检验服务器", Color.FromArgb(82, 196, 26));
            var otherSettingsCard = CreateConfigCard("其他设置", Color.FromArgb(111, 54, 178));
            var reservedCard = CreateConfigCard("预留设置", Color.FromArgb(245, 34, 45));

            // AI服务器设置 - 反向添加以获得正确的显示顺序
            CreateInputInCard("端口:", "11434", aiServerCard, out llmServerPort_text, 5);  // 减少第一个输入框的顶部边距
            CreateInputInCard("服务器地址:", "127.0.0.1", aiServerCard, out llmServerIP_text, 8);  // 减少后续输入框的边距

            // 标准检验服务器设置 - 反向添加以获得正确的显示顺序
            CreateInputInCard("端口:", "80", standardServerCard, out CheckStandardPort_text, 5);
            CreateInputInCard("服务器地址:", "192.168.1.1", standardServerCard, out CheckStandardIP_text, 8);

            // 其他设置 - 反向添加以获得正确的显示顺序
            CreateInputInCard("其他端口:", "0", otherSettingsCard, out otherPort_text, 5);
            CreateInputInCard("更新端口:", "11450", otherSettingsCard, out updatePort_text, 8);

            // 预留设置区域（暂时为空）
            var reservedLabel = new AntdUI.Label
            {
                Text = "此区域预留给未来功能扩展",
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Italic),
                ForeColor = Color.FromArgb(140, 140, 140),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };
            reservedCard.Controls.Add(reservedLabel);

            // 将卡片添加到2x2网格
            contentPanel.Controls.Add(aiServerCard, 0, 0);
            contentPanel.Controls.Add(standardServerCard, 1, 0);
            contentPanel.Controls.Add(otherSettingsCard, 0, 1);
            contentPanel.Controls.Add(reservedCard, 1, 1);

            // 底部按钮区域
            var buttonPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                WrapContents = false,
                Margin = new Padding(0, 10, 0, 0)
            };

            var saveButton = new AntdUI.Button
            {
                Text = "保存设置",
                Type = AntdUI.TTypeMini.Primary,
                Size = new Size(100, 36),
                Radius = 6,
                Font = new Font("Microsoft YaHei UI", 10F),
                Margin = new Padding(10, 0, 0, 0)
            };

            var cancelButton = new AntdUI.Button
            {
                Text = "取消",
                Type = AntdUI.TTypeMini.Default,
                Size = new Size(80, 36),
                Radius = 6,
                Font = new Font("Microsoft YaHei UI", 10F),
                Margin = new Padding(10, 0, 0, 0)
            };

            var resetButton = new AntdUI.Button
            {
                Text = "重置默认",
                Type = AntdUI.TTypeMini.Default,
                Size = new Size(100, 36),
                Radius = 6,
                Font = new Font("Microsoft YaHei UI", 10F),
                Margin = new Padding(10, 0, 0, 0)
            };

            saveButton.Click += setting_confirm_btn_Click;
            cancelButton.Click += (s, e) => Close();
            resetButton.Click += ResetButton_Click;

            buttonPanel.Controls.AddRange(new Control[] { saveButton, cancelButton, resetButton });

            // 添加所有组件到主容器
            mainContainer.Controls.Add(titleLabel, 0, 0);
            mainContainer.Controls.Add(contentPanel, 0, 1);
            mainContainer.Controls.Add(buttonPanel, 0, 2);

            Controls.Add(mainContainer);
        }

        private AntdUI.Panel CreateConfigCard(string title, Color themeColor)
        {
            var card = new AntdUI.Panel
            {
                Dock = DockStyle.Fill,
                BorderWidth = 1,
                BorderColor = Color.FromArgb(240, 240, 240),
                Radius = 8,
                Back = Color.White,
                Padding = new Padding(15, 10, 15, 15),
                Margin = new Padding(5)
            };

            // 卡片标题 - 放在左上角
            var cardTitle = new AntdUI.Label
            {
                Text = title,
                Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
                Dock = DockStyle.Top,
                Height = 35,  // 减少标题高度从50到35
                ForeColor = themeColor,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.TopLeft,
                Margin = new Padding(0, 0, 0, 5)  // 减少底部边距
            };

            // 标题下方的分割线
            var divider = new AntdUI.Divider
            {
                Dock = DockStyle.Top,
                Height = 1,
                Margin = new Padding(0, 0, 0, 8)  // 减少底部边距从10到8
            };

            // 内容区域容器 - 专门放置输入控件
            var contentContainer = new AntdUI.Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(0)
            };

            // 添加控件：标题 -> 分割线 -> 内容容器
            card.Controls.AddRange(new Control[] { contentContainer, divider, cardTitle });
            
            // 将内容容器存储到卡片的Tag中，供CreateInputInCard使用
            card.Tag = contentContainer;
            
            return card;
        }

        private void CreateInputInCard(string labelText, string placeholder, AntdUI.Panel card, out AntdUI.Input input, int topMargin = 10)
        {
            var inputContainer = new AntdUI.Panel
            {
                Dock = DockStyle.Top,
                Height = 55,  // 减少高度从60到55
                BackColor = Color.Transparent,
                Margin = new Padding(0, topMargin, 0, 0)
            };

            var label = new AntdUI.Label
            {
                Text = labelText,
                Font = new Font("Microsoft YaHei UI", 10F),
                Dock = DockStyle.Top,
                Height = 20,  // 减少标签高度从25到20
                ForeColor = Color.FromArgb(102, 102, 102),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.BottomLeft
            };

            input = new AntdUI.Input
            {
                Dock = DockStyle.Top,
                Height = 32,
                PlaceholderText = placeholder,
                BorderWidth = 1,
                BorderColor = Color.FromArgb(217, 217, 217),
                BackColor = Color.White,
                Radius = 6,
                Font = new Font("Microsoft YaHei UI", 9F)
            };

            // 先添加输入框，再添加标签（因为Dock.Top的堆叠顺序）
            inputContainer.Controls.AddRange(new Control[] { input, label });
            
            // 获取内容容器并添加输入容器
            var contentContainer = card.Tag as AntdUI.Panel;
            if (contentContainer == null)
            {
                throw new InvalidOperationException("卡片必须包含内容容器");
            }
            contentContainer.Controls.Add(inputContainer);
        }

        private void ResetButton_Click(object sender, EventArgs e)
        {
            // 重置为默认值
            llmServerIP_text.Text = "127.0.0.1";
            llmServerPort_text.Text = "11434";
            updatePort_text.Text = "11450";
            otherPort_text.Text = "0";
            CheckStandardIP_text.Text = "192.168.1.1";
            CheckStandardPort_text.Text = "80";
            
            AntdUI.Notification.info(this, "提示", "已重置为默认设置", autoClose: 2);
        }

        private void LoadConfig()
        {
            var configLoader = new ConfigLoader();
            var config = configLoader.LoadConfig();

            // 设置文本框的默认值或加载配置中的值
            llmServerIP_text.Text = config.llmServerIP ?? "127.0.0.1";
            llmServerPort_text.Text = config.llmServerPort.ToString() ?? "11434";
            updatePort_text.Text = config.UpdatePort.ToString() ?? "11450";
            otherPort_text.Text = config.OtherPort.ToString() ?? "0";
            CheckStandardIP_text.Text = config.CheckStandardIP ?? "192.168.1.1";
            CheckStandardPort_text.Text = config.CheckStandardPort.ToString() ?? "80";
        }

        private void setting_confirm_btn_Click(object sender, EventArgs e)
        {
            // 获取用户输入的配置
            var config = new ConfigLoader.Config
            {
                llmServerIP = llmServerIP_text.Text,
                llmServerPort = int.TryParse(llmServerPort_text.Text, out int llmPort) ? llmPort : 11434,
                UpdatePort = int.TryParse(updatePort_text.Text, out int updatePort) ? updatePort : 11450,
                OtherPort = int.TryParse(otherPort_text.Text, out int otherPort) ? otherPort : 0,
                CheckStandardIP = CheckStandardIP_text.Text,
                CheckStandardPort = int.TryParse(CheckStandardPort_text.Text, out int checkStandardPort) ? checkStandardPort : 0
            };
            
            // 保存到配置文件
            var configLoader = new ConfigLoader();
            configLoader.SaveConfig(config);

            // 提示用户保存成功
            AntdUI.Notification.success(this, "提示", "保存成功！", autoClose: 1);
            // 触发事件，通知 ThisAddIn 更新配置
            OnConfigUpdated?.Invoke();
            this.Close();
        }
    }
}
