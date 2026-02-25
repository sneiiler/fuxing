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
using HeyRed.MarkdownSharp;


namespace WordTools
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
                MessageBox.Show($"TaskPane initialization error: {ex.Message}", "WordTools Error",
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

        private void InitializeComponent()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("WordTools TaskPane InitializeComponent started");

                // Set control properties
                BackColor = Color.FromArgb(248, 249, 250);
                Name = "WordToolsTaskPane";
                Dock = DockStyle.Fill;

                // Create the main chat interface
                CreateChatInterface();

                System.Diagnostics.Debug.WriteLine("WordTools TaskPane InitializeComponent completed");
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
            mainContainer.RowStyles.Add(new RowStyle(SizeType.Absolute, 80f));  // 顶部功能区固定高度
            mainContainer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // 聊天区域自适应
            mainContainer.RowStyles.Add(new RowStyle(SizeType.Absolute, 70f));  // 底部输入区固定高度

            // 设置列宽为100%
            mainContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            // 创建三个区域
            var topPanel = CreateTopControlsPanel();
            var chatList = CreateChatMessagesPanel();
            var bottomPanel = CreateBottomInputPanel();

            // 设置每个面板的停靠方式
            topPanel.Dock = DockStyle.Fill;
            chatList.Dock = DockStyle.Fill;
            bottomPanel.Dock = DockStyle.Fill;

            // 按顺序添加到表格布局中
            mainContainer.Controls.Add(topPanel, 0, 0);      // 第1行：功能区
            mainContainer.Controls.Add(chatList, 0, 1);      // 第2行：聊天区
            mainContainer.Controls.Add(bottomPanel, 0, 2);   // 第3行：输入区

            Controls.Add(mainContainer);
        }



        // 创建顶部功能控制面板
        private AntdUI.Panel CreateTopControlsPanel()
        {
            var topPanel = new AntdUI.Panel
            {
                Back = Color.White,
                Name = "TopControlsPanel",
                Padding = new Padding(12, 12, 12, 12),
                BorderWidth = 1,
                BorderColor = Color.FromArgb(229, 231, 235),
                Radius = 0
            };

            // 使用FlowLayoutPanel实现简单的水平布局
            var controlsFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = false,
                WrapContents = false,
                BackColor = Color.Transparent,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };

            // 创建控件
            var modeLabel = new AntdUI.Label
            {
                Text = "模式:",
                Size = new Size(40, 30),
                Font = new Font("Microsoft YaHei UI", 10F),
                ForeColor = Color.Black,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 8, 5, 0)
            };

            _modeSelect = new AntdUI.Select
            {
                Size = new Size(80, 30),
                Font = new Font("Microsoft YaHei UI", 10F),
                Name = "ModeSelect",
                PlaceholderText = "问答",
                Margin = new Padding(0, 8, 10, 0)
            };
            _modeSelect.Items.AddRange(new object[] { "问答", "编辑", "审核" });
            _modeSelect.SelectedIndex = 0; // 默认选择第一个选项（问答）

            var knowledgeLabel = new AntdUI.Label
            {
                Text = "知识库:",
                Size = new Size(55, 30),
                Font = new Font("Microsoft YaHei UI", 10F),
                ForeColor = Color.Black,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 8, 5, 0)
            };

            _knowledgeSelect = new AntdUI.Select
            {
                Size = new Size(120, 30),
                Font = new Font("Microsoft YaHei UI", 10F),
                Name = "KnowledgeSelect",
                PlaceholderText = "遥感通用知识库",
                Margin = new Padding(0, 8, 10, 0)
            };
            _knowledgeSelect.Items.AddRange(new object[] { "遥感通用知识库", "质量库", "型号库" });
            _knowledgeSelect.SelectedIndex = 0; // 默认选择第一个选项（遥感通用知识库）

            var uploadButton = new AntdUI.Button
            {
                Text = "上传",
                Size = new Size(60, 30),
                Type = AntdUI.TTypeMini.Primary,
                Font = new Font("Microsoft YaHei UI", 10F),
                Name = "UploadButton",
                Radius = 4,
                Margin = new Padding(0, 8, 0, 0)
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

            // 添加到流布局中
            controlsFlow.Controls.AddRange(new Control[] {
                modeLabel, _modeSelect, knowledgeLabel, _knowledgeSelect, uploadButton
            });

            topPanel.Controls.Add(controlsFlow);
            return topPanel;
        }

        // 创建底部输入面板（只包含输入框）
        private AntdUI.Panel CreateBottomInputPanel()
        {
            var bottomPanel = new AntdUI.Panel
            {
                Back = Color.White,
                Name = "BottomInputPanel",
                Padding = new Padding(12),
                BorderWidth = 1,
                BorderColor = Color.FromArgb(229, 231, 235),
                Radius = 0
            };

            // 使用简单的布局
            var inputTextBox = new AntdUI.Input
            {
                Location = new Point(8, 15),
                Size = new Size(200, 40),
                Font = new Font("Microsoft YaHei UI", 10F),
                PlaceholderText = "输入您的问题...",
                Name = "InputTextBox",
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BorderWidth = 1,
                BorderColor = Color.FromArgb(217, 217, 217),
                Radius = 6,
                Multiline = true
            };

            var sendBtn = new AntdUI.Button
            {
                Text = "发送",
                Location = new Point(220, 15),
                Size = new Size(60, 40),
                Type = AntdUI.TTypeMini.Primary,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                Name = "SendButton",
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
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

            // 处理面板大小变化时的布局调整
            bottomPanel.Resize += (s, e) =>
            {
                if (bottomPanel.Width > 100)
                {
                    inputTextBox.Width = bottomPanel.Width - sendBtn.Width - 32;
                    sendBtn.Left = bottomPanel.Width - sendBtn.Width - 12;
                }
            };

            bottomPanel.Controls.AddRange(new Control[] { inputTextBox, sendBtn });
            return bottomPanel;
        }

        // 旧的控制面板方法已删除，现在使用简化的FlowLayoutPanel布局







        private System.Drawing.Image CreateAvatarImage(string initials, Color backgroundColor)
        {
            int size = 32; // 头像大小
            var bitmap = new Bitmap(size, size);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                // 设置抗锯齿
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                // 绘制圆形背景
                using (var brush = new SolidBrush(backgroundColor))
                {
                    graphics.FillEllipse(brush, 0, 0, size, size);
                }

                // 绘制边框
                using (var pen = new Pen(Color.FromArgb(200, 200, 200), 1))
                {
                    graphics.DrawEllipse(pen, 0, 0, size - 1, size - 1);
                }

                // 绘制文字
                using (var textBrush = new SolidBrush(Color.White))
                {
                    var font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
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

        private AntdUI.Chat.ChatList CreateChatMessagesPanel()
        {
            System.Diagnostics.Debug.WriteLine("Creating AntdUI ChatList panel...");

            // 创建 AntdUI ChatList 组件
            var chatList = new AntdUI.Chat.ChatList
            {
                Dock = DockStyle.Fill,
                Name = "ChatListPanel",
                BackColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 10F),
                Margin = new Padding(0, 5, 0, 5), // 添加上下边距，避免与顶部和底部面板重叠

                // 优化显示性能和流式更新支持
                AutoSize = false,
                // AutoSizeMode属性在AntdUI.Chat.ChatList中不存在，已移除

                // 启用双缓冲以减少闪烁
                // 注意：某些属性可能在AntdUI中不存在，会被忽略
            };

            // 设置AI消息背景色更深一些
            chatList.BackColor = Color.FromArgb(240, 242, 247); // AI消息的背景色

            // 尝试启用双缓冲（如果支持的话）
            try
            {
                var doubleBufferedProperty = typeof(Control).GetProperty("DoubleBuffered",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                doubleBufferedProperty?.SetValue(chatList, true);
                System.Diagnostics.Debug.WriteLine("启用了ChatList双缓冲");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"无法启用双缓冲: {ex.Message}");
            }



            System.Diagnostics.Debug.WriteLine($"ChatList created: {chatList.Name}");

            // 添加欢迎消息
            var aiAvatar = CreateAvatarImage("AI", Color.FromArgb(59, 130, 246)); // 蓝色AI头像
            var welcomeMessage = CreateMarkdownChatItem("AI助手", aiAvatar,
                                 "# <red>欢迎使用AI助手！</red>\n\n<b>我可以帮助您</b>进行：\n\n- **文本纠错**\n- **标准校验** \n- **表格格式化**\n- 其他文档处理操作\n\n> 请告诉我您需要什么帮助？", false);

            chatList.Items.Add(welcomeMessage);
            System.Diagnostics.Debug.WriteLine($"Welcome message added to ChatList. Items count: {chatList.Items.Count}");

            return chatList;
        }

                // 简单返回原始文本，不做任何处理
        private string RenderMarkdownToText(string markdown)
        {
            // 直接返回原始文本，保持原样
            return markdown ?? string.Empty;
        }

        // 创建带Markdown渲染的TextChatItem
        private AntdUI.Chat.TextChatItem CreateMarkdownChatItem(string name, Image avatar, string markdownText, bool isMe = false)
        {
            try
            {
                var renderedText = RenderMarkdownToText(markdownText);
                return new AntdUI.Chat.TextChatItem(name, avatar)
                {
                    Text = renderedText,
                    Me = isMe
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"创建Markdown聊天项错误: {ex.Message}");
                // 如果失败，创建普通的文本项
                return new AntdUI.Chat.TextChatItem(name, avatar)
                {
                    Text = markdownText,
                    Me = isMe
                };
            }
        }

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
            var chatList = this.Controls.Find("MainChatContainer", true).FirstOrDefault()
                ?.Controls.Find("ChatListPanel", true).FirstOrDefault() as AntdUI.Chat.ChatList;

            if (chatList != null)
            {
                var fileName = Path.GetFileName(filePath);

                // 添加用户上传文件消息
                var userAvatar = CreateAvatarImage("用", Color.FromArgb(34, 197, 94)); // 绿色用户头像
                var uploadMessage = new AntdUI.Chat.TextChatItem("用户", userAvatar)
                {
                    Text = $"已上传文件: {fileName}",
                    Me = true
                };
                chatList.Items.Add(uploadMessage);

                // 添加AI助手回复
                var aiAvatar = CreateAvatarImage("AI", Color.FromArgb(59, 130, 246)); // 蓝色AI头像
                var responseMessage = CreateMarkdownChatItem("AI助手", aiAvatar,
                    $"文件上传成功\n\n{Path.GetFileName(fileName)} 已成功上传到知识库中。\n\n您现在可以基于此文件内容进行对话。", false);
                chatList.Items.Add(responseMessage);

                // 刷新显示
                chatList.Invalidate();
                System.Diagnostics.Debug.WriteLine($"File upload messages added. ChatList items count: {chatList.Items.Count}");
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
            var chatList = this.Controls.Find("MainChatContainer", true).FirstOrDefault()
                ?.Controls.Find("ChatListPanel", true).FirstOrDefault() as AntdUI.Chat.ChatList;

            if (inputTextBox != null && chatList != null && !string.IsNullOrWhiteSpace(inputTextBox.Text))
            {
                var userMessage = inputTextBox.Text.Trim();

                // 添加用户消息到 ChatList
                var userAvatar = CreateAvatarImage("用户", Color.FromArgb(34, 197, 94)); // 绿色用户头像
                var userChatItem = new AntdUI.Chat.TextChatItem("用户", userAvatar)
                {
                    Text = userMessage,
                    Me = true // true 表示是用户的消息
                };

                chatList.Items.Add(userChatItem);

                // 清空输入框
                inputTextBox.Text = "";

                // 处理消息并添加助手回复
                ProcessAssistantMessage(userMessage, chatList);

                // 刷新 ChatList 显示
                chatList.Invalidate();
                System.Diagnostics.Debug.WriteLine($"User message added. ChatList items count: {chatList.Items.Count}");
            }
        }

        private async void ProcessAssistantMessage(string userMessage, AntdUI.Chat.ChatList chatList)
        {
            // 获取当前选择的模式和知识库
            string currentMode = SelectedMode;
            string currentKnowledgeBase = SelectedKnowledgeBase;

            // 先添加一个"正在思考"的占位消息
            var aiAvatar = CreateAvatarImage("AI", Color.FromArgb(59, 130, 246)); // 蓝色AI头像
            var thinkingMessage = CreateMarkdownChatItem("AI助手", aiAvatar, "🤔 **正在思考中...**", false);

            chatList.Items.Add(thinkingMessage);
            chatList.Invalidate();
            System.Diagnostics.Debug.WriteLine("Added thinking message to ChatList");

            // 用于累积流式响应的文本
            string accumulatedResponse = "";
            int lastUpdateLength = 0; // 记录上次更新的长度，避免过度刷新

            try
            {
                await _networkHelper.SendStreamChatRequestAsync(
                    userMessage,
                    currentMode,
                    currentKnowledgeBase,
                    // onChunkReceived: 每当收到新的文本片段时调用
                    (chunk) =>
                    {
                        try
                        {
                            accumulatedResponse += chunk;

                            // 实时更新 - 减少延迟，让用户看到真实的流式效果
                            // 每次收到新内容都立即更新，除非是很短的片段
                            bool shouldUpdate = chunk.Length > 0; // 简化条件，立即更新

                            if (shouldUpdate)
                            {
                                if (this.InvokeRequired)
                                {
                                    this.Invoke((MethodInvoker)delegate
                                    {
                                        UpdateChatMessage(chatList, accumulatedResponse);
                                        lastUpdateLength = accumulatedResponse.Length;
                                    });
                                }
                                else
                                {
                                    UpdateChatMessage(chatList, accumulatedResponse);
                                    lastUpdateLength = accumulatedResponse.Length;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"更新流式响应时出错: {ex.Message}");
                        }
                    },
                    // onCompleted: 流式响应完成时调用
                    () =>
                    {
                        try
                        {
                            if (this.InvokeRequired)
                            {
                                this.Invoke((MethodInvoker)delegate
                                {
                                    // 确保最终完整的文本被显示
                                    UpdateChatMessage(chatList, accumulatedResponse);
                                    System.Diagnostics.Debug.WriteLine($"流式响应完成，总长度: {accumulatedResponse.Length}");
                                });
                            }
                            else
                            {
                                UpdateChatMessage(chatList, accumulatedResponse);
                                System.Diagnostics.Debug.WriteLine($"流式响应完成，总长度: {accumulatedResponse.Length}");
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"完成流式响应时出错: {ex.Message}");
                        }
                    },
                    // onError: 发生错误时调用
                    (error) =>
                    {
                        try
                        {
                            if (this.InvokeRequired)
                            {
                                this.Invoke((MethodInvoker)delegate
                                {
                                    UpdateChatMessage(chatList, $"抱歉，发生了错误: {error}");
                                });
                            }
                            else
                            {
                                UpdateChatMessage(chatList, $"抱歉，发生了错误: {error}");
                            }

                            System.Diagnostics.Debug.WriteLine($"聊天请求错误: {error}");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"处理错误时出错: {ex.Message}");
                        }
                    }
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"发送聊天请求时出错: {ex.Message}");

                // 如果完全失败，更新消息显示错误
                UpdateChatMessage(chatList, $"无法连接到AI服务: {ex.Message}");
            }
        }

        // 辅助方法：更新聊天消息
        private void UpdateChatMessage(AntdUI.Chat.ChatList chatList, string text)
        {
            try
            {
                if (chatList.Items.Count > 0)
                {
                    var lastItem = chatList.Items[chatList.Items.Count - 1] as AntdUI.Chat.TextChatItem;
                    if (lastItem != null && !lastItem.Me)
                    {
                        // 更新文本内容（应用Markdown渲染）
                        lastItem.Text = RenderMarkdownToText(text);

                        // 调试信息
                        System.Diagnostics.Debug.WriteLine($"更新消息文本，长度: {text.Length}");

                        // 强制刷新显示 - 尝试多种方法确保更新生效
                        chatList.Invalidate(true); // 强制重绘，包括子控件
                        chatList.Update(); // 立即处理绘制消息

                        // 确保控件重新布局
                        chatList.PerformLayout();

                        // 尝试滚动到底部
                        Application.DoEvents(); // 处理所有待处理的UI事件

                        // 尝试多种滚动方法
                        try
                        {
                            // 方法1：反射查找ScrollToBottom
                            var scrollMethod = chatList.GetType().GetMethod("ScrollToBottom");
                            if (scrollMethod != null)
                            {
                                scrollMethod.Invoke(chatList, null);
                                System.Diagnostics.Debug.WriteLine("使用ScrollToBottom方法滚动");
                            }
                            else
                            {
                                // 方法2：尝试查找滚动相关属性或方法
                                var scrollProperty = chatList.GetType().GetProperty("AutoScrollPosition");
                                if (scrollProperty != null)
                                {
                                    // 设置滚动位置到底部
                                    scrollProperty.SetValue(chatList, new Point(0, chatList.Height));
                                    System.Diagnostics.Debug.WriteLine("使用AutoScrollPosition滚动");
                                }
                                else
                                {
                                    // 方法3：尝试使用Control的通用方法
                                    try
                                    {
                                        // 检查是否有AutoScroll相关属性
                                        var autoScrollProperty = chatList.GetType().GetProperty("AutoScroll");
                                        if (autoScrollProperty != null)
                                        {
                                            autoScrollProperty.SetValue(chatList, true);
                                            System.Diagnostics.Debug.WriteLine("启用了AutoScroll");
                                        }
                                    }
                                    catch (Exception autoScrollEx)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"无法启用AutoScroll: {autoScrollEx.Message}");
                                    }
                                }
                            }
                        }
                        catch (Exception scrollEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"滚动失败: {scrollEx.Message}");
                        }
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("ChatList为空，无法更新消息");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateChatMessage出错: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
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

                // 获取聊天控件
                var chatList = this.Controls.Find("MainChatContainer", true).FirstOrDefault()
                    ?.Controls.Find("ChatListPanel", true).FirstOrDefault() as AntdUI.Chat.ChatList;

                if (chatList == null)
                {
                    MessageBox.Show("聊天界面未初始化", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // 创建头像
                var aiAvatar = CreateAvatarImage("AI", Color.FromArgb(59, 130, 246)); // 蓝色AI头像
                var userAvatar = CreateAvatarImage("用户", Color.FromArgb(34, 197, 94)); // 绿色用户头像

                // 显示上传中的提示
                var uploadingMessage = new AntdUI.Chat.TextChatItem("系统", aiAvatar)
                {
                    Text = $"正在上传文件: {fileName}...",
                    Me = false
                };
                chatList.Items.Add(uploadingMessage);
                chatList.Invalidate();

                // 调用网络助手上传文件
                var uploadResponse = await _networkHelper.UploadFileAsync(filePath);

                // 移除上传中的消息
                chatList.Items.Remove(uploadingMessage);

                // 添加用户消息
                var userMessage = new AntdUI.Chat.TextChatItem("用户", userAvatar)
                {
                    Text = $"已上传文件: {fileName}",
                    Me = true
                };
                chatList.Items.Add(userMessage);

                // 添加成功消息
                string successMessage = $"文件上传成功!\n\n" +
                                       $"文件名: {fileName}\n" +
                                       $"文件类型: {fileExtension}\n" +
                                       $"文件大小: {FormatFileSize(fileSize)}\n" +
                                       $"文件ID: {uploadResponse.file_id}\n" +
                                       $"处理模式: {currentMode}\n" +
                                       $"目标知识库: {currentKnowledgeBase}\n\n" +
                                       "您现在可以基于此文件内容进行对话。";

                var botMessage = CreateMarkdownChatItem("AI助手", aiAvatar, successMessage, false);
                chatList.Items.Add(botMessage);
                chatList.Invalidate();

                System.Diagnostics.Debug.WriteLine($"文件上传成功，文件ID: {uploadResponse.file_id}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"文件处理错误: {ex.Message}");
                
                // 获取聊天控件
                var chatList = this.Controls.Find("MainChatContainer", true).FirstOrDefault()
                    ?.Controls.Find("ChatListPanel", true).FirstOrDefault() as AntdUI.Chat.ChatList;

                if (chatList != null)
                {
                    var aiAvatar = CreateAvatarImage("AI", Color.FromArgb(59, 130, 246)); // 蓝色AI头像
                    
                    // 添加错误消息到聊天
                    var errorMessage = new AntdUI.Chat.TextChatItem("系统", aiAvatar)
                    {
                        Text = $"文件上传失败: {ex.Message}",
                        Me = false
                    };
                    chatList.Items.Add(errorMessage);
                    chatList.Invalidate();
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