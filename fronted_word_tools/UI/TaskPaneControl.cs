using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Text.RegularExpressions;
using FuXing.UI;


namespace FuXing
{
    [ComVisible(true)]
    public partial class TaskPaneControl : UserControl, NetOffice.WordApi.Tools.ITaskPane
    {
        private NetOffice.WordApi.Application _application;
        private NetOffice.OfficeApi._CustomTaskPane _parentPane;

        // 选择控件的引用
        private AntdUI.Select _modeSelect;
        private AntdUI.Select _knowledgeSelect;

        // 网络助手实例
        private NetWorkHelper _networkHelper;
        private TableLayoutPanel _mainContainer;

        // 富文本聊天面板
        private RichChatPanel _richChatPanel;

        // 缓存的AI头像（从logo.png加载）
        private System.Drawing.Image _cachedAIAvatar;

        // ── 选中文本附件功能 ──
        private System.Windows.Forms.Timer _selectionPollTimer;
        private string _attachedSelectionText;   // 当前附加的选中文本
        private string _lastDetectedSelection;   // 上次检测到的选中文本（用于去重）
        private AntdUI.Panel _selectionTagPanel;  // 选中文本标签栏

        /// <summary>当前实例引用，供外部（如 FuXing.Connect）访问聊天面板</summary>
        public static TaskPaneControl CurrentInstance { get; private set; }

        // 公共属性用于获取当前选择的值
        public string SelectedMode => _modeSelect?.SelectedIndex >= 0 ? _modeSelect.Items[_modeSelect.SelectedIndex].ToString() : "问答";
        public string SelectedKnowledgeBase => _knowledgeSelect?.SelectedIndex >= 0 ? _knowledgeSelect.Items[_knowledgeSelect.SelectedIndex].ToString() : "遥感通用知识库";

        public TaskPaneControl()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("TaskPaneControl constructor started");

                // Initialize component first, before setting any styles
                InitializeComponent();

                // Set buffering styles after initialization
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.DoubleBuffer, true);

                // 初始化网络助手
                _networkHelper = new NetWorkHelper();

                System.Diagnostics.Debug.WriteLine("TaskPaneControl constructor completed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TaskPaneControl constructor exception: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Exception details: {ex}");
                // Don't rethrow in constructor as it can cause issues with COM registration
                MessageBox.Show($"TaskPane initialization error: {ex.Message}", "FuXing Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #region ITaskPane Implementation

        public void OnConnection(NetOffice.WordApi.Application application, NetOffice.OfficeApi._CustomTaskPane parentPane, object[] customArguments)
        {
            try
            {
                _application = application;
                _parentPane = parentPane;
                CurrentInstance = this;
                System.Diagnostics.Debug.WriteLine("TaskPane connected to Word application");

                // 设置任务面板的宽度
                if (_parentPane != null)
                {
                    _parentPane.Width = 500; // 设置宽度为500像素
                    System.Diagnostics.Debug.WriteLine($"TaskPane width set to: {_parentPane.Width}");
                }

                // Perform any additional initialization here if needed
                if (this.InvokeRequired)
                {
                    this.Invoke((MethodInvoker)delegate
                    {
                        this.Refresh();
                    });
                }
                else
                {
                    this.Refresh();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OnConnection error: {ex.Message}");
            }
        }

        public void OnDisconnection()
        {
            try
            {
                // 停止选中文本轮询
                if (_selectionPollTimer != null)
                {
                    _selectionPollTimer.Stop();
                    _selectionPollTimer.Dispose();
                    _selectionPollTimer = null;
                }

                if (_application != null)
                {
                    _application = null;
                }
                if (_parentPane != null)
                {
                    _parentPane = null;
                }
                System.Diagnostics.Debug.WriteLine("TaskPane disconnected");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OnDisconnection error: {ex.Message}");
            }
        }

        public void OnDockPositionChanged(NetOffice.OfficeApi.Enums.MsoCTPDockPosition position)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"TaskPane dock position changed to: {position}");
                // Handle dock position changes if needed
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OnDockPositionChanged error: {ex.Message}");
            }
        }

        public void OnVisibleStateChanged(bool visible)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"TaskPane visibility changed to: {visible}");
                // Handle visibility changes if needed
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OnVisibleStateChanged error: {ex.Message}");
            }
        }

        #endregion

        // ════════════════════════════════════════════════════════════
        //  公共消息 API — 供纠错 / 其他功能向聊天面板发送进度和结果
        // ════════════════════════════════════════════════════════════

        /// <summary>获取 RichChatPanel 控件引用</summary>
        public RichChatPanel GetChatPanel()
        {
            return _richChatPanel;
        }

        /// <summary>添加一条 AI 消息到聊天面板并自动滚到底部</summary>
        public void AddAIMessage(string markdownText)
        {
            if (InvokeRequired) { Invoke((MethodInvoker)delegate { AddAIMessage(markdownText); }); return; }

            var panel = GetChatPanel();
            if (panel == null) return;

            var aiAvatar = GetAIAvatar();
            panel.AddMessage("AI助手", aiAvatar, ChatRole.AI, markdownText);
        }

        /// <summary>更新最后一条 AI 消息文本（用于实时进度更新）</summary>
        public void UpdateLastAIMessage(string markdownText)
        {
            if (InvokeRequired) { Invoke((MethodInvoker)delegate { UpdateLastAIMessage(markdownText); }); return; }

            var panel = GetChatPanel();
            if (panel == null) return;

            var lastMsg = panel.LastAIMessage;
            if (lastMsg != null)
            {
                lastMsg.SetText(markdownText);
            }
        }

        // ── 结构化消息 API（工具调用卡片） ──

        /// <summary>添加一条包含工具调用卡片的 AI 消息</summary>
        public ToolCallCard AddToolCallMessage(string toolName, string statusText)
        {
            if (InvokeRequired)
            {
                return (ToolCallCard)Invoke((Func<ToolCallCard>)delegate { return AddToolCallMessage(toolName, statusText); });
            }

            var panel = GetChatPanel();
            if (panel == null) return null;

            var aiAvatar = GetAIAvatar();
            var msg = panel.AddMessage("AI助手", aiAvatar, ChatRole.AI);
            return msg.AddToolCall(toolName, statusText);
        }

        /// <summary>更新最后一条 AI 消息中的工具调用卡片</summary>
        public void UpdateToolCall(ToolCallStatus status, string statusText, List<string> details = null)
        {
            if (InvokeRequired) { Invoke((MethodInvoker)delegate { UpdateToolCall(status, statusText, details); }); return; }

            var panel = GetChatPanel();
            if (panel == null) return;

            var lastMsg = panel.LastAIMessage;
            var card = lastMsg?.GetToolCall();
            if (card != null)
            {
                card.Update(status, statusText, details);
                lastMsg.NotifyContentChanged();
            }
        }

        /// <summary>在最后一条 AI 消息中添加结果文本（在工具卡片下方）</summary>
        public void AppendResultText(string text)
        {
            if (InvokeRequired) { Invoke((MethodInvoker)delegate { AppendResultText(text); }); return; }

            var panel = GetChatPanel();
            if (panel == null) return;

            // 添加新消息展示结果
            var aiAvatar = GetAIAvatar();
            panel.AddMessage("AI助手", aiAvatar, ChatRole.AI, text);
        }

        /// <summary>刷新聊天面板并滚动到底部</summary>
        private void RefreshChatPanel()
        {
            var panel = GetChatPanel();
            if (panel == null) return;
            panel.Invalidate(true);
            panel.ScrollToEnd();
        }

        private void InitializeComponent()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("FuXing TaskPane InitializeComponent started");

                // Set control properties
                BackColor = Color.FromArgb(248, 249, 250);
                Name = "FuXingTaskPane";
                Dock = DockStyle.Fill;

                // Create the main chat interface
                CreateChatInterface();

                System.Diagnostics.Debug.WriteLine("FuXing TaskPane InitializeComponent completed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"InitializeComponent exception: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Exception details: {ex}");
                throw;
            }
        }

        private void CreateChatInterface()
        {
            // 主容器使用 TableLayoutPanel 实现真正的flex布局
            var mainContainer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(248, 249, 250),
                Name = "MainChatContainer",
                Padding = new Padding(0),
                Margin = new Padding(0),
                ColumnCount = 1,
                RowCount = 3,
                CellBorderStyle = TableLayoutPanelCellBorderStyle.None
            };

            // 设置行的大小类型：固定-自适应-固定
            mainContainer.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));  // 顶部功能区固定高度
            mainContainer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // 聊天区域自适应
            mainContainer.RowStyles.Add(new RowStyle(SizeType.Absolute, 68f));  // 底部输入区（可自适应）

            // 设置列宽为100%
            mainContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            // 创建三个区域
            var topPanel = CreateTopControlsPanel();
            var chatPanel = CreateRichChatPanel();
            var bottomPanel = CreateBottomInputPanel();

            // 设置每个面板的停靠方式
            topPanel.Dock = DockStyle.Fill;
            chatPanel.Dock = DockStyle.Fill;
            bottomPanel.Dock = DockStyle.Fill;

            // 按顺序添加到表格布局中
            mainContainer.Controls.Add(topPanel, 0, 0);       // 第1行：功能区
            mainContainer.Controls.Add(chatPanel, 0, 1);      // 第2行：聊天区
            mainContainer.Controls.Add(bottomPanel, 0, 2);    // 第3行：输入区

            _mainContainer = mainContainer;
            Controls.Add(mainContainer);
        }



        // 创建顶部功能控制面板
        private AntdUI.Panel CreateTopControlsPanel()
        {
            var topPanel = new AntdUI.Panel
            {
                Back = Color.White,
                Name = "TopControlsPanel",
                Padding = new Padding(0),
                BorderWidth = 1,
                BorderColor = Color.FromArgb(229, 231, 235),
                Radius = 0
            };

            int ctrlH = 28;
            int ctrlY = 6;

            var modeLabel = new AntdUI.Label
            {
                Text = "模式:",
                Size = new Size(36, ctrlH),
                Location = new Point(8, ctrlY),
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = Color.Black,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _modeSelect = new AntdUI.Select
            {
                Size = new Size(72, ctrlH),
                Location = new Point(44, ctrlY),
                Font = new Font("Microsoft YaHei UI", 9F),
                Name = "ModeSelect",
                PlaceholderText = "问答"
            };
            _modeSelect.Items.AddRange(new object[] { "问答", "编辑", "审核" });
            _modeSelect.SelectedIndex = 0;

            var knowledgeLabel = new AntdUI.Label
            {
                Text = "知识库:",
                Size = new Size(48, ctrlH),
                Location = new Point(122, ctrlY),
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = Color.Black,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _knowledgeSelect = new AntdUI.Select
            {
                Size = new Size(120, ctrlH),
                Location = new Point(170, ctrlY),
                Font = new Font("Microsoft YaHei UI", 9F),
                Name = "KnowledgeSelect",
                PlaceholderText = "遥感通用知识库"
            };
            _knowledgeSelect.Items.AddRange(new object[] { "遥感通用知识库", "质量库", "型号库" });
            _knowledgeSelect.SelectedIndex = 0;

            var uploadButton = new AntdUI.Button
            {
                Text = "上传",
                Size = new Size(50, ctrlH),
                Location = new Point(296, ctrlY),
                Type = AntdUI.TTypeMini.Default,
                Font = new Font("Microsoft YaHei UI", 9F),
                Name = "UploadButton",
                Radius = 4
            };

            // 顶部栏响应式布局：知识库下拉框和上传按钮自动适配宽度
            topPanel.Resize += (s, e) =>
            {
                int pw = topPanel.ClientSize.Width;
                // 上传按钮固定在右侧
                uploadButton.Left = pw - uploadButton.Width - 8;
                // 知识库下拉框填充中间剩余空间
                int kbRight = uploadButton.Left - 6;
                _knowledgeSelect.Width = Math.Max(80, kbRight - _knowledgeSelect.Left);
            };

            // 添加事件处理器
            _modeSelect.SelectedIndexChanged += (s, e) =>
            {
                var selectedMode = _modeSelect.SelectedIndex >= 0 ? _modeSelect.Items[_modeSelect.SelectedIndex].ToString() : "";
                System.Diagnostics.Debug.WriteLine($"模式已切换到: {selectedMode}");
                // 这里可以添加模式切换的逻辑
            };

            _knowledgeSelect.SelectedIndexChanged += (s, e) =>
            {
                var selectedKnowledge = _knowledgeSelect.SelectedIndex >= 0 ? _knowledgeSelect.Items[_knowledgeSelect.SelectedIndex].ToString() : "";
                System.Diagnostics.Debug.WriteLine($"知识库已切换到: {selectedKnowledge}");
                // 这里可以添加知识库切换的逻辑
            };

            uploadButton.Click += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine("上传按钮被点击");
                OpenFileForUpload();
            };

            topPanel.Controls.AddRange(new Control[] {
                modeLabel, _modeSelect, knowledgeLabel, _knowledgeSelect, uploadButton
            });
            return topPanel;
        }

        // 创建底部输入面板（支持高度自适应）
        private AntdUI.Panel CreateBottomInputPanel()
        {
            var bottomPanel = new AntdUI.Panel
            {
                Back = Color.White,
                Name = "BottomInputPanel",
                Padding = new Padding(0),
                BorderWidth = 1,
                BorderColor = Color.FromArgb(229, 231, 235),
                Radius = 0
            };

            int sendW = 56;
            int gap = 4;
            int tagBarH = 0; // 选中文本标签栏高度（隐藏时为0）

            // ── 选中文本附件标签栏 ──
            _selectionTagPanel = new AntdUI.Panel
            {
                Name = "SelectionTagPanel",
                Back = Color.FromArgb(248, 249, 250),
                Visible = false,
                Height = 32,
                Padding = new Padding(0)
            };

            // 标签容器（圆角蓝底标签）
            var tagContainer = new Panel
            {
                Name = "SelectionTagContainer",
                BackColor = Color.FromArgb(219, 234, 254),
                Location = new Point(gap, 4),
                Height = 24,
                Width = 200
            };

            var tagLabel = new Label
            {
                Name = "SelectionTagLabel",
                Text = "",
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = Color.FromArgb(37, 99, 235),
                BackColor = Color.Transparent,
                AutoSize = false,
                Location = new Point(4, 0),
                Height = 24,
                TextAlign = ContentAlignment.MiddleLeft
            };

            var tagCloseBtn = new Label
            {
                Name = "SelectionTagClose",
                Text = "✕",
                Font = new Font("Microsoft YaHei UI", 8F),
                ForeColor = Color.FromArgb(96, 165, 250),
                BackColor = Color.Transparent,
                Size = new Size(20, 24),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            tagCloseBtn.Click += (s, e) => DetachSelection();
            tagCloseBtn.MouseEnter += (s, e) => tagCloseBtn.ForeColor = Color.FromArgb(220, 38, 38);
            tagCloseBtn.MouseLeave += (s, e) => tagCloseBtn.ForeColor = Color.FromArgb(96, 165, 250);

            tagContainer.Controls.Add(tagCloseBtn);
            tagContainer.Controls.Add(tagLabel);
            _selectionTagPanel.Controls.Add(tagContainer);

            var inputTextBox = new AntdUI.Input
            {
                Location = new Point(gap, gap),
                Size = new Size(200, 56),
                Font = new Font("Microsoft YaHei UI", 10F),
                PlaceholderText = "输入您的问题...",
                Name = "InputTextBox",
                BorderWidth = 1,
                BorderColor = Color.FromArgb(217, 217, 217),
                Radius = 6,
                Multiline = true
            };

            var sendBtn = new AntdUI.Button
            {
                Text = "发送",
                Size = new Size(sendW, 40),
                Type = AntdUI.TTypeMini.Primary,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                Name = "SendButton",
                Radius = 6
            };

            sendBtn.Click += SendBtn_Click;

            // 处理文本框中的回车键
            inputTextBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && !e.Shift)
                {
                    e.Handled = true;
                    SendBtn_Click(sendBtn, EventArgs.Empty);
                }
            };

            // 输入框高度自适应
            inputTextBox.TextChanged += (s, e) =>
            {
                if (_mainContainer == null) return;
                try
                {
                    using (var g = inputTextBox.CreateGraphics())
                    {
                        var text = string.IsNullOrEmpty(inputTextBox.Text) ? "X" : inputTextBox.Text;
                        var textSize = g.MeasureString(text, inputTextBox.Font, Math.Max(1, inputTextBox.Width - 16));
                        int lineCount = Math.Max(1, (int)Math.Ceiling(textSize.Height / inputTextBox.Font.GetHeight(g)));
                        int desiredPanelH = Math.Max(68, Math.Min(160, lineCount * 24 + 28));
                        int totalH = desiredPanelH + (_selectionTagPanel.Visible ? _selectionTagPanel.Height : 0);
                        if (Math.Abs(_mainContainer.RowStyles[2].Height - totalH) > 2)
                        {
                            _mainContainer.RowStyles[2].Height = totalH;
                            _mainContainer.PerformLayout();
                        }
                    }
                }
                catch { /* ignore measurement errors */ }
            };

            // 面板大小变化时手动布局所有控件
            bottomPanel.Resize += (s, e) =>
            {
                int pw = bottomPanel.ClientSize.Width;
                int ph = bottomPanel.ClientSize.Height;
                if (pw < 50 || ph < 20) return;

                tagBarH = _selectionTagPanel.Visible ? _selectionTagPanel.Height : 0;
                _selectionTagPanel.SetBounds(0, 0, pw, tagBarH);

                // 调整标签容器宽度自适应
                var container = _selectionTagPanel.Controls["SelectionTagContainer"] as Panel;
                if (container != null)
                {
                    var lbl = container.Controls["SelectionTagLabel"] as Label;
                    var closeBtn = container.Controls["SelectionTagClose"] as Label;
                    if (lbl != null && closeBtn != null)
                    {
                        using (var gfx = lbl.CreateGraphics())
                        {
                            int textW = (int)gfx.MeasureString(lbl.Text, lbl.Font).Width + 12;
                            int containerW = Math.Min(pw - gap * 2, textW + closeBtn.Width + 8);
                            container.Width = containerW;
                            lbl.Width = containerW - closeBtn.Width - 4;
                            closeBtn.Left = lbl.Right;
                        }
                    }
                }

                int inputY = tagBarH + gap;
                int inputW = pw - sendW - gap * 3;
                int inputH = ph - tagBarH - gap * 2;
                inputTextBox.SetBounds(gap, inputY, inputW, inputH);
                sendBtn.SetBounds(pw - sendW - gap, ph - sendBtn.Height - gap, sendW, sendBtn.Height);
            };

            bottomPanel.Controls.AddRange(new Control[] { _selectionTagPanel, inputTextBox, sendBtn });

            // ── 启动 Word 选中文本轮询 ──
            StartSelectionPolling();

            return bottomPanel;
        }

        // ── 选中文本附件管理 ──

        /// <summary>启动定时轮询 Word 选中文本</summary>
        private void StartSelectionPolling()
        {
            _selectionPollTimer = new System.Windows.Forms.Timer { Interval = 500 };
            _selectionPollTimer.Tick += SelectionPollTimer_Tick;
            _selectionPollTimer.Start();
        }

        /// <summary>轮询检测 Word 选中文本变化</summary>
        private void SelectionPollTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                var connect = Connect.CurrentInstance;
                if (connect?.WordApplication == null) return;

                var selection = connect.WordApplication.Selection;
                if (selection == null) return;

                // 只有真正选中了文本（非光标位置）才视为有效选中
                string text = null;
                if (selection.Type != NetOffice.WordApi.Enums.WdSelectionType.wdSelectionIP)
                    text = selection.Text?.Trim();

                // 空文本或仅空白字符视为无选中
                if (string.IsNullOrWhiteSpace(text) || text.Length <= 1)
                    text = null;

                // 与上次检测结果相同则跳过
                if (text == _lastDetectedSelection) return;
                _lastDetectedSelection = text;

                if (text != null)
                    AttachSelection(text);
                else if (_attachedSelectionText != null)
                    DetachSelection();
            }
            catch
            {
                // COM 调用可能在 Word 忙时失败，静默忽略
            }
        }

        /// <summary>附加选中文本到输入区域</summary>
        private void AttachSelection(string text)
        {
            _attachedSelectionText = text;

            string preview = text.Length > 30 ? text.Substring(0, 30) + "..." : text;
            // 移除换行符使预览保持单行
            preview = preview.Replace("\r", "").Replace("\n", " ");
            string tagText = $"\U0001f4ce 选中文本({text.Length}字): {preview}";

            var container = _selectionTagPanel?.Controls["SelectionTagContainer"] as Panel;
            var lbl = container?.Controls["SelectionTagLabel"] as Label;
            if (lbl != null)
                lbl.Text = tagText;

            if (_selectionTagPanel != null && !_selectionTagPanel.Visible)
            {
                _selectionTagPanel.Visible = true;
                RecalcBottomPanelHeight();
            }
        }

        /// <summary>移除附加的选中文本</summary>
        private void DetachSelection()
        {
            _attachedSelectionText = null;
            _lastDetectedSelection = null;

            if (_selectionTagPanel != null && _selectionTagPanel.Visible)
            {
                _selectionTagPanel.Visible = false;
                RecalcBottomPanelHeight();
            }
        }

        /// <summary>重新计算底部面板高度（标签栏显隐变化时调用）</summary>
        private void RecalcBottomPanelHeight()
        {
            if (_mainContainer == null) return;
            int tagH = _selectionTagPanel?.Visible == true ? _selectionTagPanel.Height : 0;
            int baseH = 68; // 最小输入区高度
            _mainContainer.RowStyles[2].Height = baseH + tagH;
            _mainContainer.PerformLayout();

            // 触发底部面板重新布局
            var bottomPanel = _mainContainer.GetControlFromPosition(0, 2);
            bottomPanel?.PerformLayout();
        }

        // 旧的控制面板方法已删除，现在使用简化的FlowLayoutPanel布局







        /// <summary>获取AI头像（从 Resources/logo.png 加载）</summary>
        private System.Drawing.Image GetAIAvatar()
        {
            if (_cachedAIAvatar != null) return _cachedAIAvatar;

            var logo = ResourceManager.GetIcon("logo.png")
                ?? throw new FileNotFoundException("AI头像资源 Resources/logo.png 未找到，请确认文件已部署到输出目录");

            var scaled = new Bitmap(40, 40);
            using (var g = Graphics.FromImage(scaled))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(logo, 0, 0, 40, 40);
            }
            _cachedAIAvatar = scaled;
            return _cachedAIAvatar;
        }

        private System.Drawing.Image CreateAvatarImage(string initials, Color backgroundColor)
        {
            int size = 32;
            var bitmap = new Bitmap(size, size);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                using (var brush = new SolidBrush(backgroundColor))
                {
                    graphics.FillEllipse(brush, 0, 0, size, size);
                }

                // 根据文字长度自适应字号
                bool hasChinese = initials.Any(c => c > 127);
                float fontSize = 10F;
                if (hasChinese) fontSize = initials.Length > 1 ? 8F : 10F;
                else if (initials.Length > 2) fontSize = 8F;

                using (var textBrush = new SolidBrush(Color.White))
                using (var font = new Font("Microsoft YaHei UI", fontSize, FontStyle.Bold))
                {
                    var stringFormat = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    };
                    var rect = new RectangleF(0, 0, size, size);
                    graphics.DrawString(initials, font, textBrush, rect, stringFormat);
                }
            }
            return bitmap;
        }

        private RichChatPanel CreateRichChatPanel()
        {
            System.Diagnostics.Debug.WriteLine("Creating RichChatPanel...");

            _richChatPanel = new RichChatPanel
            {
                Dock = DockStyle.Fill,
                Name = "ChatListPanel",
                Margin = new Padding(0, 5, 0, 5),
            };

            // 添加欢迎消息
            var aiAvatar = GetAIAvatar();
            _richChatPanel.AddMessage("AI助手", aiAvatar, ChatRole.AI,
                "欢迎使用AI助手！\n\n我可以帮助您进行：\n\n- 文本纠错\n- 标准校验\n- 表格格式化\n- 其他文档处理操作\n\n请告诉我您需要什么帮助？");

            System.Diagnostics.Debug.WriteLine("RichChatPanel created with welcome message");

            return _richChatPanel;
        }

        // Markdown 渲染已移至 RichChatPanel 组件内部

        private System.Drawing.Drawing2D.GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int radius)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            int diameter = radius * 2;
            var arc = new Rectangle(rect.Location, new Size(diameter, diameter));

            // Top left arc
            path.AddArc(arc, 180, 90);

            // Top right arc
            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270, 90);

            // Bottom right arc
            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0, 90);

            // Bottom left arc
            arc.X = rect.Left;
            path.AddArc(arc, 90, 90);

            path.CloseFigure();
            return path;
        }

        private void HandleFileUpload(string filePath)
        {
            var panel = GetChatPanel();
            if (panel != null)
            {
                var fileName = Path.GetFileName(filePath);

                // 添加用户上传文件消息
                var userAvatar = CreateAvatarImage("用户", Color.FromArgb(34, 197, 94));
                panel.AddMessage("用户", userAvatar, ChatRole.User, $"已上传文件: {fileName}");

                // 添加AI助手回复
                var aiAvatar = GetAIAvatar();
                panel.AddMessage("AI助手", aiAvatar, ChatRole.AI,
                    $"文件上传成功\n\n{fileName} 已成功上传到知识库中。\n\n您现在可以基于此文件内容进行对话。");

                System.Diagnostics.Debug.WriteLine($"File upload messages added.");
            }
        }

        private void UploadBtn_Click(object sender, EventArgs e)
        {
            using (var openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "所有文件 (*.*)|*.*|文本文件 (*.txt)|*.txt|Word文档 (*.docx)|*.docx";
                openFileDialog.Title = "选择要上传的文件";
                openFileDialog.Multiselect = true;

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    foreach (string fileName in openFileDialog.FileNames)
                    {
                        HandleFileUpload(fileName);
                    }
                }
            }
        }

        private void SendBtn_Click(object sender, EventArgs e)
        {
            var inputTextBox = this.Controls.Find("MainChatContainer", true).FirstOrDefault()
                ?.Controls.Find("InputTextBox", true).FirstOrDefault() as AntdUI.Input;
            var panel = GetChatPanel();

            if (inputTextBox != null && panel != null && !string.IsNullOrWhiteSpace(inputTextBox.Text))
            {
                var userMessage = inputTextBox.Text.Trim();

                // 捕获当前附加的选中文本，然后清除附件
                string attachedText = _attachedSelectionText;
                DetachSelection();

                // 在 UI 上显示用户消息（不含附件原文，仅显示标记）
                string displayMessage = userMessage;
                if (!string.IsNullOrEmpty(attachedText))
                {
                    string preview = attachedText.Length > 50
                        ? attachedText.Substring(0, 50) + "..."
                        : attachedText;
                    preview = preview.Replace("\r", "").Replace("\n", " ");
                    displayMessage = $"\U0001f4ce [选中文本({attachedText.Length}字): {preview}]\n{userMessage}";
                }

                var userAvatar = CreateAvatarImage("用户", Color.FromArgb(34, 197, 94));
                panel.AddMessage("用户", userAvatar, ChatRole.User, displayMessage);

                // 清空输入框
                inputTextBox.Text = "";

                // 处理消息并添加助手回复（传入附件文本）
                ProcessAssistantMessage(userMessage, panel, attachedText);

                System.Diagnostics.Debug.WriteLine($"User message added. Message count: {panel.MessageCount}");
            }
        }

        private async void ProcessAssistantMessage(string userMessage, RichChatPanel panel, string attachedSelection = null)
        {
            var connect = Connect.CurrentInstance;
            var memory = connect?.Memory;
            var toolRegistry = connect?.ToolRegistry;

            if (memory == null || toolRegistry == null)
            {
                System.Diagnostics.Debug.WriteLine("Connect 实例不可用，使用旧接口");
                await ProcessAssistantMessageLegacy(userMessage, panel);
                return;
            }

            // 确保系统 Prompt 已设置
            if (string.IsNullOrEmpty(memory.GetSystemPrompt()))
            {
                string currentMode = SelectedMode;
                string currentKnowledgeBase = SelectedKnowledgeBase;
                memory.SetSystemPrompt(BuildSystemPrompt(currentMode, currentKnowledgeBase));
            }

            // 构建完整的用户消息（附加选中文本上下文）
            string fullUserMessage = userMessage;
            if (!string.IsNullOrEmpty(attachedSelection))
            {
                fullUserMessage = $"[用户附加了当前选中的文本作为上下文({attachedSelection.Length}字符):\n{attachedSelection}]\n\n{userMessage}";
            }

            // 记录用户消息到上下文
            memory.AddUserMessage(fullUserMessage);

            // 先添加一个"正在思考"的占位消息
            var aiAvatar = GetAIAvatar();
            var aiMsg = panel.AddMessage("AI助手", aiAvatar, ChatRole.AI, "🤔 正在思考中...");

            // 工具调用循环（最多 10 轮，防止无限循环）
            const int maxToolRounds = 10;
            for (int round = 0; round < maxToolRounds; round++)
            {
                string accumulatedResponse = "";
                bool hasError = false;

                var result = await _networkHelper.SendStreamChatWithMemoryAsync(
                    memory,
                    toolRegistry.GetToolDefinitions(),
                    // onChunkReceived
                    (chunk) =>
                    {
                        try
                        {
                            accumulatedResponse += chunk;
                            if (this.InvokeRequired)
                                this.Invoke((MethodInvoker)delegate { aiMsg.SetText(accumulatedResponse); });
                            else
                                aiMsg.SetText(accumulatedResponse);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"更新流式响应时出错: {ex.Message}");
                        }
                    },
                    // onError
                    (error) =>
                    {
                        hasError = true;
                        try
                        {
                            if (this.InvokeRequired)
                                this.Invoke((MethodInvoker)delegate { aiMsg.SetText($"抱歉，发生了错误: {error}"); });
                            else
                                aiMsg.SetText($"抱歉，发生了错误: {error}");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"处理错误时出错: {ex.Message}");
                        }
                    }
                );

                if (hasError || result == null)
                {
                    memory.AddAssistantMessage("(请求失败)");
                    break;
                }

                // 大模型返回了工具调用
                if (result.HasToolCalls)
                {
                    // 记录 assistant 消息（含 tool_calls）到上下文
                    memory.AddAssistantMessage(result.Content, result.ToolCalls);

                    // 逐一执行工具调用
                    foreach (var tc in result.ToolCalls)
                    {
                        System.Diagnostics.Debug.WriteLine($"执行工具调用: {tc.FunctionName} (id={tc.Id})");

                        // 在 UI 上显示工具调用卡片
                        UI.ToolCallCard toolCard = null;
                        if (this.InvokeRequired)
                        {
                            this.Invoke((MethodInvoker)delegate
                            {
                                toolCard = aiMsg.AddToolCall(tc.FunctionName, "正在执行...");
                            });
                        }
                        else
                        {
                            toolCard = aiMsg.AddToolCall(tc.FunctionName, "正在执行...");
                        }

                        // 执行工具（需在 UI 线程，因为涉及 Word COM）
                        ToolExecutionResult toolResult = null;
                        if (this.InvokeRequired)
                        {
                            toolResult = await (Task<ToolExecutionResult>)this.Invoke(
                                (Func<Task<ToolExecutionResult>>)(async () =>
                                    await toolRegistry.ExecuteAsync(tc.FunctionName, tc.Arguments)));
                        }
                        else
                        {
                            toolResult = await toolRegistry.ExecuteAsync(tc.FunctionName, tc.Arguments);
                        }

                        // 更新工具卡片状态
                        var status = toolResult.Success ? UI.ToolCallStatus.Success : UI.ToolCallStatus.Error;
                        if (this.InvokeRequired)
                        {
                            this.Invoke((MethodInvoker)delegate
                            {
                                toolCard?.Update(status, toolResult.Output);
                                aiMsg.NotifyContentChanged();
                            });
                        }
                        else
                        {
                            toolCard?.Update(status, toolResult.Output);
                            aiMsg.NotifyContentChanged();
                        }

                        // 记录工具结果到上下文
                        memory.AddToolResult(tc.Id, tc.FunctionName, toolResult.Output);
                    }

                    // 工具执行完毕，创建新的 AI 消息用于下一轮回复
                    if (this.InvokeRequired)
                    {
                        this.Invoke((MethodInvoker)delegate
                        {
                            aiMsg = panel.AddMessage("AI助手", aiAvatar, ChatRole.AI, "🤔 正在思考中...");
                        });
                    }
                    else
                    {
                        aiMsg = panel.AddMessage("AI助手", aiAvatar, ChatRole.AI, "🤔 正在思考中...");
                    }

                    // 继续循环，让大模型看到工具结果后再次回复
                    continue;
                }

                // 大模型返回了纯文本回复，会话本轮结束
                memory.AddAssistantMessage(accumulatedResponse);

                // 最终更新 UI
                if (this.InvokeRequired)
                {
                    this.Invoke((MethodInvoker)delegate
                    {
                        aiMsg.SetText(accumulatedResponse);
                    });
                }
                else
                {
                    aiMsg.SetText(accumulatedResponse);
                }

                break;  // 正常结束
            }
        }

        /// <summary>旧版消息处理（不使用记忆系统，作为后备）</summary>
        private async Task ProcessAssistantMessageLegacy(string userMessage, RichChatPanel panel)
        {
            string currentMode = SelectedMode;
            string currentKnowledgeBase = SelectedKnowledgeBase;
            var aiAvatar = GetAIAvatar();
            var aiMsg = panel.AddMessage("AI助手", aiAvatar, ChatRole.AI, "🤔 正在思考中...");
            string accumulatedResponse = "";

            try
            {
                await _networkHelper.SendStreamChatRequestAsync(
                    userMessage, currentMode, currentKnowledgeBase,
                    (chunk) =>
                    {
                        accumulatedResponse += chunk;
                        if (this.InvokeRequired)
                            this.Invoke((MethodInvoker)delegate { aiMsg.SetText(accumulatedResponse); });
                        else
                            aiMsg.SetText(accumulatedResponse);
                    },
                    () =>
                    {
                        if (this.InvokeRequired)
                            this.Invoke((MethodInvoker)delegate { aiMsg.SetText(accumulatedResponse); });
                        else
                            aiMsg.SetText(accumulatedResponse);
                    },
                    (error) =>
                    {
                        if (this.InvokeRequired)
                            this.Invoke((MethodInvoker)delegate { aiMsg.SetText($"抱歉，发生了错误: {error}"); });
                        else
                            aiMsg.SetText($"抱歉，发生了错误: {error}");
                    }
                );
            }
            catch (Exception ex)
            {
                aiMsg.SetText($"无法连接到AI服务: {ex.Message}");
            }
        }

        /// <summary>构建系统 Prompt，包含角色定义和可用工具说明</summary>
        private string BuildSystemPrompt(string mode, string knowledgeBase)
        {
            var sb = new StringBuilder();

            sb.AppendLine("你是「福星」Word 文档智能助手，深度集成在 Microsoft Word 插件中。");
            sb.AppendLine("你拥有以下能力，可以通过工具调用直接操作用户的 Word 文档：");
            sb.AppendLine();
            sb.AppendLine("可用工具：");
            sb.AppendLine("- correct_selected_text: 对选中文本执行 AI 纠错");
            sb.AppendLine("- correct_all_text: 对全文执行 AI 纠错");
            sb.AppendLine("- format_selected_table: 格式化光标所在表格");
            sb.AppendLine("- format_all_tables: 格式化文档中所有表格");
            sb.AppendLine("- check_standard: 校验选中文本是否符合标准规范");
            sb.AppendLine("- get_selected_text: 获取当前选中的文本");
            sb.AppendLine("- get_document_info: 获取文档基本信息");
            sb.AppendLine("- load_default_styles: 载入默认 AI 样式库");
            sb.AppendLine("- insert_text: 在光标位置插入文本");
            sb.AppendLine("- replace_selected_text: 替换选中的文本");
            sb.AppendLine();
            sb.AppendLine("规则：");
            sb.AppendLine("1. 当用户要求你做文档操作时，直接调用对应工具，不要只给建议");
            sb.AppendLine("2. 你可以记住整个对话中发生的所有操作");
            sb.AppendLine("3. 如果用户问你之前做了什么，根据对话历史如实回答");
            sb.AppendLine("4. 回答要简洁专业，使用中文");
            sb.AppendLine("5. 如果用户消息开头包含 [用户附加了当前选中的文本作为上下文...]，说明用户在 Word 中选中了文本并自动附加到对话中。你可以直接基于这段文本进行分析、纠错、改写等操作，无需再调用 get_selected_text 工具获取");

            switch (mode)
            {
                case "编辑":
                    sb.AppendLine("5. 当前处于编辑模式，重点帮助用户改进文本质量");
                    break;
                case "审核":
                    sb.AppendLine("5. 当前处于审核模式，重点检查文档规范性和准确性");
                    break;
                default:
                    sb.AppendLine("5. 当前处于问答模式，回答用户的各种问题");
                    break;
            }

            switch (knowledgeBase)
            {
                case "遥感通用知识库":
                    sb.AppendLine("6. 你具备遥感技术相关的专业知识");
                    break;
                case "质量库":
                    sb.AppendLine("6. 你熟悉质量管理和质量控制的相关标准");
                    break;
                case "型号库":
                    sb.AppendLine("6. 你了解各种产品型号和技术规格");
                    break;
            }

            return sb.ToString();
        }

        private void TriggerWordFunction(string functionName)
        {
            try
            {
                var connectInstance = Connect.CurrentInstance;
                if (connectInstance == null)
                {
                    MessageBox.Show("无法获取AI助手插件实例", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

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
                MessageBox.Show("执行操作时错误: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                System.Diagnostics.Debug.WriteLine($"TaskPane操作失败: {ex.Message}");
            }
        }

        // 打开文件选择对话框并处理上传
        private void OpenFileForUpload()
        {
            try
            {
                using (OpenFileDialog openFileDialog = new OpenFileDialog())
                {
                    // 设置文件过滤器
                    openFileDialog.Filter = "文档文件|*.doc;*.docx;*.pdf;*.txt;*.rtf|Word文档|*.doc;*.docx|PDF文件|*.pdf|文本文件|*.txt;*.rtf|所有文件|*.*";
                    openFileDialog.FilterIndex = 1;
                    openFileDialog.Title = "选择要上传的文件";
                    openFileDialog.Multiselect = false;

                    if (openFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        string selectedFile = openFileDialog.FileName;
                        string fileName = Path.GetFileName(selectedFile);

                        System.Diagnostics.Debug.WriteLine($"选择的文件: {selectedFile}");

                        // 显示文件选择成功的反馈
                        MessageBox.Show($"已选择文件: {fileName}\n\n文件路径: {selectedFile}",
                                      "文件选择成功",
                                      MessageBoxButtons.OK,
                                      MessageBoxIcon.Information);

                        // 处理文件上传
                        ProcessSelectedFile(selectedFile);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"文件选择失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                System.Diagnostics.Debug.WriteLine($"文件选择错误: {ex.Message}");
            }
        }

        // 处理选中的文件
        private async void ProcessSelectedFile(string filePath)
        {
            try
            {
                string fileName = Path.GetFileName(filePath);
                string fileExtension = Path.GetExtension(filePath).ToLower();
                long fileSize = new FileInfo(filePath).Length;

                System.Diagnostics.Debug.WriteLine($"处理文件: {fileName}");
                System.Diagnostics.Debug.WriteLine($"文件类型: {fileExtension}");
                System.Diagnostics.Debug.WriteLine($"文件大小: {fileSize} bytes");

                // 获取当前选择的模式和知识库
                string currentMode = SelectedMode;
                string currentKnowledgeBase = SelectedKnowledgeBase;

                System.Diagnostics.Debug.WriteLine($"当前模式: {currentMode}");
                System.Diagnostics.Debug.WriteLine($"当前知识库: {currentKnowledgeBase}");

                var panel = GetChatPanel();
                if (panel == null)
                {
                    MessageBox.Show("聊天界面未初始化", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // 创建头像
                var aiAvatar = GetAIAvatar();
                var userAvatar = CreateAvatarImage("用户", Color.FromArgb(34, 197, 94));

                // 显示上传工具调用卡片
                var uploadMsg = panel.AddMessage("AI助手", aiAvatar, ChatRole.AI);
                var uploadCard = uploadMsg.AddToolCall("文件上传", $"正在上传 {fileName}...");

                // 调用网络助手上传文件
                var uploadResponse = await _networkHelper.UploadFileAsync(filePath);

                // 更新上传卡片状态为成功
                uploadCard.Update(ToolCallStatus.Success, "上传完成", new List<string>
                {
                    $"文件名: {fileName}",
                    $"文件类型: {fileExtension}",
                    $"文件大小: {FormatFileSize(fileSize)}",
                    $"文件ID: {uploadResponse.file_id}"
                });
                uploadMsg.NotifyContentChanged();

                // 添加用户消息
                panel.AddMessage("用户", userAvatar, ChatRole.User, $"已上传文件: {fileName}");

                // 添加成功消息
                panel.AddMessage("AI助手", aiAvatar, ChatRole.AI,
                    $"文件上传成功！\n\n您现在可以基于此文件内容进行对话。\n\n处理模式: {currentMode}\n目标知识库: {currentKnowledgeBase}");

                System.Diagnostics.Debug.WriteLine($"文件上传成功，文件ID: {uploadResponse.file_id}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"文件处理错误: {ex.Message}");

                var panel = GetChatPanel();
                if (panel != null)
                {
                    var aiAvatar = GetAIAvatar();

                    // 尝试更新已有的工具调用卡片
                    var lastMsg = panel.LastAIMessage;
                    var card = lastMsg?.GetToolCall();
                    if (card != null)
                    {
                        card.Update(ToolCallStatus.Error, $"上传失败: {ex.Message}");
                        lastMsg.NotifyContentChanged();
                    }
                    else
                    {
                        panel.AddMessage("AI助手", aiAvatar, ChatRole.AI, $"❌ 文件上传失败: {ex.Message}");
                    }
                }

                MessageBox.Show($"文件上传失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 格式化文件大小显示
        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}