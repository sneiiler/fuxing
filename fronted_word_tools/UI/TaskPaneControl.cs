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
using FuXing.SubAgents;


namespace FuXing
{
    [ComVisible(true)]
    [Guid("03326A51-B257-3623-917E-25A086B271B0")]
    [ProgId("FuXing.TaskPaneControl")]
    public partial class TaskPaneControl : UserControl, NetOffice.WordApi.Tools.ITaskPane
    {
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_MOUSEHOVER = 0x02A1;
        private const int WH_MOUSE_LL = 14;

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

        // ── 状态栏相关 ──
        private System.Windows.Forms.Panel _statusBar;
        private System.Windows.Forms.Label _statusRunning;
        private System.Windows.Forms.Label _statusServerLoad;
        private System.Windows.Forms.Label _statusContext;
        private AntdUI.Button _sendButton;
        private bool _isAssistantRunning;
        private CancellationTokenSource _agentCts;
        private bool _hasRequestedGreeting;
        private bool _isLLMAvailable = true;

        // ── 启动安全提示 ──
        // 约束："每次应用会话仅显示一次"。
        // 说明：TaskPaneControl 在多窗口场景会有多个实例，
        // 因此使用 static 标志在整个插件进程内全局去重。
        private static bool _globalStartupWarningShown;
        // 实例级去重：防止同一窗口实例重复触发。
        private bool _hasShownWarning;

        /// <summary>每个窗口独立的会话记忆</summary>
        public ChatMemory Memory { get; } = new ChatMemory();

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

                // 安装鼠标钩子以捕获滚轮消息
                if (this.InvokeRequired)
                {
                    this.Invoke((MethodInvoker)delegate
                    {
                        this.InstallMouseHook();
                        this.Invalidate();
                    });
                }
                else
                {
                    InstallMouseHook();
                    this.Invalidate();
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
                // 卸载鼠标钩子
                UninstallMouseHook();
                _instance = null;

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
                // 当 TaskPane 变为可见时，更新 CurrentInstance 指向当前活动窗口的 TaskPaneControl
                if (visible)
                {
                    CurrentInstance = this;

                    // 首次可见时显示安全提示弹窗
                    ShowStartupWarningIfNeeded();

                    // 首次可见时，向 LLM 发送问候请求作为健康检查
                    CheckAndRequestGreeting();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OnVisibleStateChanged error: {ex.Message}");
            }
        }

        #endregion

        // ════════════════════════════════════════════════════════════
        //  启动安全提示 — 独立弹窗
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// 根据配置决定是否显示启动安全提示弹窗。
        /// 维护约定：此方法由多个入口触发（OnVisibleStateChanged / EnsureTaskPaneVisible），
        /// 但必须保证在同一应用会话内最多弹出一次。
        /// </summary>
        public void ShowStartupWarningIfNeeded()
        {
            // 控件句柄未就绪时不执行，避免跨线程/生命周期异常。
            if (!IsHandleCreated || IsDisposed || Disposing)
                return;

            if (InvokeRequired)
            {
                BeginInvoke((MethodInvoker)ShowStartupWarningIfNeeded);
                return;
            }

            // 双重去重：实例级 + 全局会话级。
            if (_hasShownWarning || _globalStartupWarningShown) return;

            var config = new ConfigLoader().LoadConfig();
            if (!config.ShowStartupWarning) return;

            bool shown = ShowStartupWarningDialog();
            if (shown)
            {
                // 仅在弹窗实际显示成功后才置位，避免“未显示但被误判已显示”。
                _hasShownWarning = true;
                _globalStartupWarningShown = true;
            }
        }

        private sealed class NativeWindowOwner : IWin32Window
        {
            public IntPtr Handle { get; }
            public NativeWindowOwner(IntPtr handle) { Handle = handle; }
        }

        /// <summary>弹出独立的安全提示窗口（AntdUI.Window，不依赖宿主 Form）。</summary>
        private bool ShowStartupWarningDialog()
        {
            try
            {
                if (!IsHandleCreated || IsDisposed || Disposing)
                    return false;

                using (var dialog = new StartupWarningDialog())
                {
                    IntPtr hwnd = GetWordWindowHandle();

                    if (hwnd != IntPtr.Zero)
                        dialog.ShowDialog(new NativeWindowOwner(hwnd));
                    else
                        dialog.ShowDialog();
                }
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ShowStartupWarningDialog error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 安全获取 Word 窗口句柄，兼容 Office 2010。
        /// Office 2010 的 COM 对象模型可能不支持 Hwnd 属性。
        /// </summary>
        private IntPtr GetWordWindowHandle()
        {
            try
            {
                if (_application == null || _application.ActiveWindow == null)
                    return IntPtr.Zero;

                // 尝试通过 COM 方式获取 Hwnd
                dynamic activeWindow = _application.ActiveWindow;
                try
                {
                    int hwndValue = (int)activeWindow.Hwnd;
                    return new IntPtr(hwndValue);
                }
                catch
                {
                    // Office 2010 可能不支持 Hwnd 属性，尝试其他方式
                    // 通过 FindWindow 查找 Word 窗口
                    return FindWordWindow();
                }
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// 通过 Win32 API 查找 Word 窗口句柄。
        /// </summary>
        private IntPtr FindWordWindow()
        {
            try
            {
                // 尝试获取 Word 主窗口句柄
                var process = System.Diagnostics.Process.GetProcessesByName("WINWORD");
                if (process.Length > 0 && process[0].MainWindowHandle != IntPtr.Zero)
                    return process[0].MainWindowHandle;
                return IntPtr.Zero;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        // ════════════════════════════════════════════════════════════
        //  LLM 健康检查 — TaskPane 首次可见时自动请求问候
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// TaskPane 首次显示时自动向 LLM 发送问候请求。
        /// 成功则展示 AI 自我介绍，失败则将状态设为"不可用"。
        /// 由外部（toggle / EnsureVisible）在面板变为可见后调用。
        /// </summary>
        public void CheckAndRequestGreeting()
        {
            if (_hasRequestedGreeting) return;
            _hasRequestedGreeting = true;
            RequestGreetingAsync();
        }

        private async void RequestGreetingAsync()
        {
            var panel = GetChatPanel();
            if (panel == null) return;

            SetAssistantRunning(true);
            try
            {
                var memory = this.Memory;

                // 构建精简的系统提示
                memory.SetSystemPrompt(
                    "你是'福星'，一个集成在 Microsoft Word 中的智能文档助手。请用中文回复。");

                // 添加隐藏的用户消息（不在聊天面板显示），驱动 LLM 生成问候
                string greetingPrompt = "请用简洁友好的方式向用户打招呼，介绍你作为 Word 文档助手的核心能力（例如：文档编辑、格式调整、内容生成、纠错润色、文档分析等），然后询问用户想要做什么。控制在100字以内。";
                memory.AddUserMessage(greetingPrompt);

                // 在聊天面板添加 AI 消息占位
                var aiAvatar = GetAIAvatar();
                var aiMsg = panel.AddMessage("AI福星", aiAvatar, ChatRole.AI, "正在连接 AI 服务...");

                string accumulatedResponse = "";
                bool hasError = false;

                var result = await _networkHelper.SendStreamChatWithMemoryAsync(
                    memory,
                    tools: null,  // 问候不需要工具
                    onChunkReceived: (chunk) =>
                    {
                        try
                        {
                            accumulatedResponse += chunk;

                            // 问候消息不显示思考过程：等 <think> 块结束后才开始显示
                            string displayText = StripThinkBlock(accumulatedResponse);
                            if (string.IsNullOrEmpty(displayText))
                                return;  // 还在思考中，不更新 UI

                            if (this.InvokeRequired)
                                this.Invoke((MethodInvoker)delegate { aiMsg.SetText(displayText); });
                            else
                                aiMsg.SetText(displayText);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"问候流式更新出错: {ex.Message}");
                        }
                    },
                    onError: (error) =>
                    {
                        hasError = true;
                        System.Diagnostics.Debug.WriteLine($"问候请求失败: {error}");
                    }
                );

                if (hasError || result == null)
                {
                    // LLM 不可用
                    SetLLMUnavailable(aiMsg);
                    memory.AddAssistantMessage("(问候请求失败)");
                    return;
                }

                // 成功：记录完整响应到上下文（含 think），但 UI 只显示正文
                memory.AddAssistantMessage(accumulatedResponse);
                _isLLMAvailable = true;

                string finalDisplay = StripThinkBlock(accumulatedResponse);
                if (this.InvokeRequired)
                    this.Invoke((MethodInvoker)delegate { aiMsg.SetText(finalDisplay); });
                else
                    aiMsg.SetText(finalDisplay);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RequestGreetingAsync exception: {ex}");
                // 异常也视为不可用
                var aiAvatar = GetAIAvatar();
                var aiMsg = panel.AddMessage("AI福星", aiAvatar, ChatRole.AI, "");
                SetLLMUnavailable(aiMsg);
            }
            finally
            {
                SetAssistantRunning(false);
            }
        }

        /// <summary>
        /// 移除文本中的 &lt;think&gt;...&lt;/think&gt; 块（含未闭合的情况）。
        /// 用于问候消息不显示思考过程。
        /// </summary>
        private static string StripThinkBlock(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            // 已闭合的 <think>...</think>
            string result = Regex.Replace(text, @"<think>[\s\S]*?</think>", "", RegexOptions.IgnoreCase).TrimStart();

            // 未闭合的 <think>...（流式还没收到 </think>）→ 返回空，表示还在思考中
            if (Regex.IsMatch(result, @"<think>", RegexOptions.IgnoreCase))
                return "";

            return result;
        }

        /// <summary>
        /// 将状态设为"不可用"：更新状态栏、在聊天面板显示错误提示。
        /// </summary>
        private void SetLLMUnavailable(UI.MessageGroup aiMsg)
        {
            _isLLMAvailable = false;

            if (InvokeRequired)
            {
                Invoke((MethodInvoker)delegate { SetLLMUnavailable(aiMsg); });
                return;
            }

            // 更新状态栏
            if (_statusRunning != null)
            {
                _statusRunning.Text = "● 不可用";
                _statusRunning.ForeColor = Color.FromArgb(156, 163, 175);  // 灰色
            }

            // 在聊天面板显示不可用消息
            aiMsg.SetText(
                "⚠️ **AI 服务暂时不可用**\n\n" +
                "无法连接到 AI 服务，可能的原因：\n" +
                "- 服务器未启动或网络不通\n" +
                "- API 配置错误（地址 / 密钥 / 模型名）\n\n" +
                "请检查设置或联系开发者获取帮助。\n" +
                "您可以尝试发送一条消息，系统将自动重新检测连接。");
        }

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
            panel.AddMessage("AI福星", aiAvatar, ChatRole.AI, markdownText);
        }

        /// <summary>
        /// 以编程方式注入一条用户消息并触发 AI 响应。
        /// 等价于用户在输入框中输入消息并点击发送。
        /// 由 Ribbon 按钮等外部入口调用，将操作委托给 AI 处理。
        /// </summary>
        public void InjectUserMessage(string message)
        {
            if (InvokeRequired) { Invoke((MethodInvoker)delegate { InjectUserMessage(message); }); return; }

            if (_isAssistantRunning || string.IsNullOrWhiteSpace(message)) return;

            var panel = GetChatPanel();
            if (panel == null) return;

            var userAvatar = CreateAvatarImage("用户", Color.FromArgb(34, 197, 94));
            panel.AddMessage("用户", userAvatar, ChatRole.User, message);

            ProcessAssistantMessage(message, panel);
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
            var msg = panel.AddMessage("AI福星", aiAvatar, ChatRole.AI);
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
            panel.AddMessage("AI福星", aiAvatar, ChatRole.AI, text);
        }

        /// <summary>刷新聊天面板并滚动到底部</summary>
        private void RefreshChatPanel()
        {
            var panel = GetChatPanel();
            if (panel == null) return;
            panel.Invalidate();
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

            // 开启 TableLayoutPanel 的双缓冲，极大减少拖动时的闪烁和卡顿
            typeof(TableLayoutPanel).GetProperty("DoubleBuffered", 
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(mainContainer, true, null);

            // 设置行的大小类型：状态栏-功能区-聊天-输入
            mainContainer.RowCount = 4;
            mainContainer.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));  // 状态栏固定高度
            mainContainer.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));  // 顶部功能区固定高度
            mainContainer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // 聊天区域自适应
            mainContainer.RowStyles.Add(new RowStyle(SizeType.Absolute, 68f));  // 底部输入区（可自适应）

            // 设置列宽为100%
            mainContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            // 创建四个区域
            var statusBar = CreateStatusBar();
            var topPanel = CreateTopControlsPanel();
            var chatPanel = CreateRichChatPanel();
            var bottomPanel = CreateBottomInputPanel();

            // 设置每个面板的停靠方式
            statusBar.Dock = DockStyle.Fill;
            topPanel.Dock = DockStyle.Fill;
            chatPanel.Dock = DockStyle.Fill;
            bottomPanel.Dock = DockStyle.Fill;

            // 按顺序添加到表格布局中
            mainContainer.Controls.Add(statusBar, 0, 0);      // 第1行：状态栏
            mainContainer.Controls.Add(topPanel, 0, 1);       // 第2行：功能区
            mainContainer.Controls.Add(chatPanel, 0, 2);      // 第3行：聊天区
            mainContainer.Controls.Add(bottomPanel, 0, 3);    // 第4行：输入区

            _mainContainer = mainContainer;
            Controls.Add(mainContainer);
        }



        // 创建顶部状态栏
        private System.Windows.Forms.Panel CreateStatusBar()
        {
            _statusBar = new System.Windows.Forms.Panel
            {
                BackColor = Color.FromArgb(241, 243, 245),
                Name = "StatusBar",
                Height = 28,
                Padding = new Padding(0)
            };

            var statusFont = new Font("Microsoft YaHei UI", 8F);
            var statusFg = Color.FromArgb(107, 114, 128);

            _statusRunning = new System.Windows.Forms.Label
            {
                Text = "● 就绪",
                Font = statusFont,
                ForeColor = Color.FromArgb(34, 197, 94),
                AutoSize = true,
                Location = new Point(8, 6),
                BackColor = Color.Transparent
            };

            _statusServerLoad = new System.Windows.Forms.Label
            {
                Text = "负载 70%",
                Font = statusFont,
                ForeColor = statusFg,
                AutoSize = true,
                Location = new Point(80, 6),
                BackColor = Color.Transparent
            };

            _statusContext = new System.Windows.Forms.Label
            {
                Text = "上下文 0/128K",
                Font = statusFont,
                ForeColor = statusFg,
                AutoSize = true,
                Location = new Point(160, 6),
                BackColor = Color.Transparent
            };

            // 底部描线
            _statusBar.Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(229, 231, 235)))
                    e.Graphics.DrawLine(pen, 0, _statusBar.Height - 1, _statusBar.Width, _statusBar.Height - 1);
            };

            // 响应式布局：各元素间距自适应
            _statusBar.Resize += (s, e) =>
            {
                int pw = _statusBar.ClientSize.Width;
                // 均匀分布三个状态指示
                _statusRunning.Left = 8;
                _statusServerLoad.Left = Math.Max(80, pw / 3);
                _statusContext.Left = Math.Max(160, pw * 2 / 3);
            };

            _statusBar.Controls.AddRange(new Control[] { _statusRunning, _statusServerLoad, _statusContext });

            // 初始刷新上下文窗口显示
            RefreshContextStatus();

            return _statusBar;
        }

        // ── 状态栏更新方法 ──

        /// <summary>设置助手运行状态并同步更新发送按钮、状态栏指示</summary>
        private void SetAssistantRunning(bool running)
        {
            if (InvokeRequired) { Invoke((MethodInvoker)delegate { SetAssistantRunning(running); }); return; }

            _isAssistantRunning = running;

            // 更新状态栏
            if (_statusRunning != null)
            {
                _statusRunning.Text = running ? "● 运行中" : "● 就绪";
                _statusRunning.ForeColor = running
                    ? Color.FromArgb(239, 68, 68)   // 红色
                    : Color.FromArgb(34, 197, 94);   // 绿色
            }

            // 切换发送按钮外观：运行中 → 停止按钮，就绪 → 发送按钮
            if (_sendButton != null)
            {
                if (running)
                {
                    _sendButton.Text = "■ 停止";
                    _sendButton.Type = AntdUI.TTypeMini.Error;
                }
                else
                {
                    _sendButton.Text = "发送";
                    _sendButton.Type = AntdUI.TTypeMini.Primary;
                }
            }

            // 运行结束时刷新上下文
            if (!running)
            {
                RefreshContextStatus();
            }
        }

        /// <summary>刷新上下文窗口 token 用量显示</summary>
        private void RefreshContextStatus()
        {
            if (_statusContext == null) return;
            if (InvokeRequired) { Invoke((MethodInvoker)RefreshContextStatus); return; }

            int currentTokens = Memory.EstimateTotalTokens();
            int limit = Memory.ContextWindow;

            // 加载配置中的上下文窗口限制
            var config = new ConfigLoader().LoadConfig();
            if (config.ContextWindowLimit > 0)
            {
                limit = config.ContextWindowLimit;
                Memory.ContextWindow = limit;
                Memory.MaxAllowedTokens = (int)(limit * 0.76);
            }

            string currentStr = FormatTokenCount(currentTokens);
            string limitStr = FormatTokenCount(limit);
            _statusContext.Text = $"上下文 {currentStr}/{limitStr}";

            // 接近上限变色
            double ratio = limit > 0 ? (double)currentTokens / limit : 0;
            if (ratio > 0.8)
                _statusContext.ForeColor = Color.FromArgb(239, 68, 68);   // 红色
            else if (ratio > 0.5)
                _statusContext.ForeColor = Color.FromArgb(245, 158, 11);  // 橙色
            else
                _statusContext.ForeColor = Color.FromArgb(107, 114, 128); // 灰色
        }

        /// <summary>格式化 token 数显示（如 1.2K、128K）</summary>
        private static string FormatTokenCount(int tokens)
        {
            if (tokens >= 1000)
                return $"{tokens / 1000.0:0.#}K";
            return tokens.ToString();
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
            _sendButton = sendBtn;

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
                        if (Math.Abs(_mainContainer.RowStyles[3].Height - desiredPanelH) > 2)
                        {
                            _mainContainer.RowStyles[3].Height = desiredPanelH;
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

                int inputW = pw - sendW - gap * 3;
                int inputH = ph - gap * 2;
                inputTextBox.SetBounds(gap, gap, inputW, inputH);
                sendBtn.SetBounds(pw - sendW - gap, ph - sendBtn.Height - gap, sendW, sendBtn.Height);
            };

            bottomPanel.Controls.AddRange(new Control[] { inputTextBox, sendBtn });
            return bottomPanel;
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

            // 欢迎消息由 RequestGreetingAsync() 在 TaskPane 首次可见时动态请求 LLM 生成

            System.Diagnostics.Debug.WriteLine("RichChatPanel created");

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

                // 添加AI福星回复
                var aiAvatar = GetAIAvatar();
                panel.AddMessage("AI福星", aiAvatar, ChatRole.AI,
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
            // 运行中 → 停止
            if (_isAssistantRunning)
            {
                _agentCts?.Cancel();
                return;
            }

            var inputTextBox = this.Controls.Find("MainChatContainer", true).FirstOrDefault()
                ?.Controls.Find("InputTextBox", true).FirstOrDefault() as AntdUI.Input;
            var panel = GetChatPanel();

            if (inputTextBox != null && panel != null && !string.IsNullOrWhiteSpace(inputTextBox.Text))
            {
                var userMessage = inputTextBox.Text.Trim();

                // LLM 之前标记为不可用 → 乐观重置状态，若再次失败会被错误处理重新标记
                if (!_isLLMAvailable)
                {
                    _isLLMAvailable = true;
                    if (_statusRunning != null)
                    {
                        _statusRunning.Text = "● 就绪";
                        _statusRunning.ForeColor = Color.FromArgb(34, 197, 94);
                    }
                }

                // 添加用户消息
                var userAvatar = CreateAvatarImage("用户", Color.FromArgb(34, 197, 94));
                panel.AddMessage("用户", userAvatar, ChatRole.User, userMessage);

                // 清空输入框
                inputTextBox.Text = "";

                // 处理消息并添加助手回复
                ProcessAssistantMessage(userMessage, panel);

                System.Diagnostics.Debug.WriteLine($"User message added. Message count: {panel.MessageCount}");
            }
        }

        private async void ProcessAssistantMessage(string userMessage, RichChatPanel panel)
        {
            _agentCts?.Dispose();
            _agentCts = new CancellationTokenSource();
            var ct = _agentCts.Token;

            SetAssistantRunning(true);
            ChatMemory memory = null;
            MessageGroup aiMsg = null;
            try
            {
                var connect = Connect.CurrentInstance;
                memory = this.Memory;
                var toolRegistry = connect?.ToolRegistry;
                var skillManager = connect?.SkillManager;

                if (connect == null || toolRegistry == null)
                    throw new InvalidOperationException("Connect instance unavailable: ToolRegistry is null");

            // 发现 skills（基于当前活动文档所在目录）
            string documentDir = "";
            try
            {
                var doc = connect.WordApplication?.ActiveDocument;
                if (doc != null && !string.IsNullOrEmpty(doc.Path))
                    documentDir = doc.Path;
            }
            catch { /* 无活动文档时忽略 */ }
            skillManager.LoadFromDocumentDir(documentDir);

            // 构建系统 Prompt（每次都重建，确保包含最新的 skill 状态）
            string currentMode = SelectedMode;
            memory.SetSystemPrompt(BuildSystemPrompt(currentMode, toolRegistry, skillManager));

            // 设置工具定义的 token 预留量，用于精确裁剪判断
            memory.ToolTokenReserve = EstimateToolDefinitionTokens(toolRegistry);

            // 记录用户消息到上下文
            memory.AddUserMessage(userMessage);
            DebugLogger.Instance.LogUserMessage(userMessage);

            // 先添加一个"正在思考"的占位消息
            var aiAvatar = GetAIAvatar();
            aiMsg = panel.AddMessage("AI福星", aiAvatar, ChatRole.AI, "🤔 正在思考中...");

            // 工具调用循环（从配置读取最大轮次，防止无限循环）
            int maxToolRounds = new ConfigLoader().LoadConfig().MaxToolRounds;
            if (maxToolRounds <= 0) maxToolRounds = 10;
            int round;
            for (round = 0; round < maxToolRounds; round++)
            {
                ct.ThrowIfCancellationRequested();
                DebugLogger.Instance.LogRoundStart(round, maxToolRounds);
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
                    },
                    ct
                );

                if (hasError || result == null)
                {
                    DebugLogger.Instance.LogError("SendStreamChat", "请求失败，result=" + (result == null ? "null" : "hasError"));
                    memory.AddAssistantMessage("(请求失败)");

                    // 标记 LLM 不可用
                    _isLLMAvailable = false;
                    if (InvokeRequired)
                        Invoke((MethodInvoker)delegate {
                            if (_statusRunning != null)
                            {
                                _statusRunning.Text = "● 不可用";
                                _statusRunning.ForeColor = Color.FromArgb(156, 163, 175);
                            }
                        });
                    else if (_statusRunning != null)
                    {
                        _statusRunning.Text = "● 不可用";
                        _statusRunning.ForeColor = Color.FromArgb(156, 163, 175);
                    }

                    break;
                }

                // 大模型返回了工具调用
                if (result.HasToolCalls)
                {
                    // 记录 assistant 消息（含 tool_calls）到上下文
                    memory.AddAssistantMessage(result.Content, result.ToolCalls);

                    // 单轮最大工具调用数量限制，防止 LLM 一次返回几十个调用导致 UI 卡死
                    const int maxToolCallsPerRound = 8;
                    int toolCallIndex = 0;

                    // 逐一执行工具调用
                    foreach (var tc in result.ToolCalls)
                    {
                        ct.ThrowIfCancellationRequested();

                        // 超过单轮上限则跳过剩余调用，将截断信息记入上下文
                        if (toolCallIndex >= maxToolCallsPerRound)
                        {
                            string skipMsg = $"已跳过（单轮工具调用数超过 {maxToolCallsPerRound} 上限，请减少每轮调用量）";
                            memory.AddToolResult(tc.Id, tc.FunctionName, skipMsg);
                            System.Diagnostics.Debug.WriteLine($"跳过工具调用: {tc.FunctionName} (id={tc.Id})，超出单轮上限");
                            continue;
                        }
                        toolCallIndex++;

                        System.Diagnostics.Debug.WriteLine($"执行工具调用: {tc.FunctionName} (id={tc.Id})");
                        DebugLogger.Instance.LogToolCall(tc.FunctionName, tc.Id, tc.Arguments);

                        // ── 操作审批拦截（嵌入聊天面板，非弹窗）──
                        if (toolRegistry.RequiresApproval(tc.FunctionName, tc.Arguments))
                        {
                            string summary = toolRegistry.BuildApprovalSummary(tc.FunctionName, tc.Arguments);

                            UI.ApprovalCard approvalCard = null;
                            if (this.InvokeRequired)
                                this.Invoke((MethodInvoker)delegate { approvalCard = aiMsg.AddApprovalCard(toolRegistry.GetDisplayName(tc.FunctionName), tc.FunctionName, summary); });
                            else
                                approvalCard = aiMsg.AddApprovalCard(toolRegistry.GetDisplayName(tc.FunctionName), tc.FunctionName, summary);

                            // 滚动到底部，确保审批卡片可见
                            if (this.InvokeRequired)
                                this.Invoke((MethodInvoker)delegate { panel.ScrollToEnd(); });
                            else
                                panel.ScrollToEnd();

                            bool approved = await approvalCard.ResultTask;

                            if (!approved)
                            {
                                string rejectionMsg = "用户拒绝了此操作";
                                DebugLogger.Instance.LogToolResult(tc.FunctionName, tc.Id, false, rejectionMsg);
                                memory.AddToolResult(tc.Id, tc.FunctionName, rejectionMsg);
                                continue;
                            }
                        }

                        // ── ask_user 工具：嵌入问答卡片，等待用户回答 ──
                        if (tc.FunctionName == "ask_user")
                        {
                            string question = tc.Arguments?["question"]?.ToString() ?? "";
                            bool allowFreeInput = (bool)(tc.Arguments?["allow_free_input"] ?? true);

                            var options = new List<UI.AskUserOption>();
                            var optionsArr = tc.Arguments?["options"] as Newtonsoft.Json.Linq.JArray;
                            if (optionsArr != null)
                            {
                                foreach (var optItem in optionsArr)
                                {
                                    options.Add(new UI.AskUserOption
                                    {
                                        Label = optItem["label"]?.ToString() ?? "",
                                        Description = optItem["description"]?.ToString()
                                    });
                                }
                            }

                            UI.AskUserCard askCard = null;
                            if (this.InvokeRequired)
                                this.Invoke((MethodInvoker)delegate { askCard = aiMsg.AddAskUserCard(question, options, allowFreeInput); });
                            else
                                askCard = aiMsg.AddAskUserCard(question, options, allowFreeInput);

                            // 滚动到底部，确保问答卡片可见
                            if (this.InvokeRequired)
                                this.Invoke((MethodInvoker)delegate { panel.ScrollToEnd(); });
                            else
                                panel.ScrollToEnd();

                            string userAnswer = await askCard.ResultTask;

                            DebugLogger.Instance.LogToolResult(tc.FunctionName, tc.Id, true, userAnswer);
                            memory.AddToolResult(tc.Id, tc.FunctionName, $"User answered: {userAnswer}");
                            System.Windows.Forms.Application.DoEvents();
                            continue;
                        }

                        bool isSubAgent = tc.FunctionName == "run_sub_agent";
                        UI.ToolCallCard toolCard = null;
                        UI.SubAgentBlock subAgentBlock = null;

                        // 在 UI 上显示卡片（子智能体使用专用块）
                        if (isSubAgent)
                        {
                            string taskType = tc.Arguments?["task"]?.ToString() ?? "custom";
                            string taskLabel;
                            switch (taskType)
                            {
                                case "analyze_structure": taskLabel = "分析文档结构"; break;
                                case "extract_key_info": taskLabel = "提取关键信息"; break;
                                default: taskLabel = "自定义分析"; break;
                            }
                            if (this.InvokeRequired)
                                this.Invoke((MethodInvoker)delegate { subAgentBlock = aiMsg.AddSubAgent(taskLabel); });
                            else
                                subAgentBlock = aiMsg.AddSubAgent(taskLabel);

                            RunSubAgentTool.ProgressSink = new SubAgentUiProgress(subAgentBlock, this);
                        }
                        else
                        {
                            if (this.InvokeRequired)
                                this.Invoke((MethodInvoker)delegate { toolCard = aiMsg.AddToolCall(tc.FunctionName, "正在执行..."); });
                            else
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

                        // 更新卡片状态
                        var status = toolResult.Success ? UI.ToolCallStatus.Success : UI.ToolCallStatus.Error;
                        if (isSubAgent)
                        {
                            RunSubAgentTool.ProgressSink = null;
                            if (this.InvokeRequired)
                                this.Invoke((MethodInvoker)delegate { subAgentBlock?.SetComplete(status); aiMsg.NotifyContentChanged(); });
                            else
                            {
                                subAgentBlock?.SetComplete(status);
                                aiMsg.NotifyContentChanged();
                            }
                        }
                        else
                        {
                            // execute_word_script：将原始代码附加到卡片
                            if (tc.FunctionName == "execute_word_script" && toolCard != null)
                            {
                                string codeSnippet = tc.Arguments?["code"]?.ToString();
                                if (!string.IsNullOrEmpty(codeSnippet))
                                    toolCard.CodeSnippet = codeSnippet;
                            }

                            if (this.InvokeRequired)
                                this.Invoke((MethodInvoker)delegate { toolCard?.Update(status, toolResult.Output); aiMsg.NotifyContentChanged(); });
                            else
                            {
                                toolCard?.Update(status, toolResult.Output);
                                aiMsg.NotifyContentChanged();
                            }
                        }

                        // 记录工具结果到上下文
                        DebugLogger.Instance.LogToolResult(tc.FunctionName, tc.Id, toolResult.Success, toolResult.Output);
                        memory.AddToolResult(tc.Id, tc.FunctionName, toolResult.Output);

                        // 让出 UI 线程，防止连续多次工具调用导致界面卡死
                        System.Windows.Forms.Application.DoEvents();
                    }

                    // Skill 可能在工具调用中被激活，刷新 system prompt
                    memory.SetSystemPrompt(BuildSystemPrompt(currentMode, toolRegistry, skillManager));

                    // 工具执行完毕，在同一消息中开始新的文本段落
                    if (this.InvokeRequired)
                    {
                        this.Invoke((MethodInvoker)delegate
                        {
                            aiMsg.PrepareNewStreamSection("🤔 正在思考中...");
                        });
                    }
                    else
                    {
                        aiMsg.PrepareNewStreamSection("🤔 正在思考中...");
                    }

                    // 继续循环，让大模型看到工具结果后再次回复
                    continue;
                }

                // 大模型返回了纯文本回复，会话本轮结束
                DebugLogger.Instance.LogAssistantMessage(accumulatedResponse);
                memory.AddAssistantMessage(accumulatedResponse);

                // 响应被 max_tokens 截断 → 插入续写指令后自动续传
                if (result.IsTruncated)
                {
                    System.Diagnostics.Debug.WriteLine($"[Agent] Round {round}: 响应被截断 (finish_reason=length)，自动续传");
                    memory.AddUserMessage("Your previous response was truncated. Please continue exactly where you left off.");
                    if (this.InvokeRequired)
                    {
                        this.Invoke((MethodInvoker)delegate
                        {
                            aiMsg.PrepareNewStreamSection("🤔 正在思考中...");
                        });
                    }
                    else
                    {
                        aiMsg.PrepareNewStreamSection("🤔 正在思考中...");
                    }
                    continue;
                }

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

            // 循环因轮次耗尽退出 → 通知用户
            if (round >= maxToolRounds)
            {
                string exhaustionMsg = $"\n\n⚠️ 已达到最大执行轮次 ({maxToolRounds})，操作自动终止。如果任务未完成，请缩小指令范围后重试。";
                memory.AddAssistantMessage(exhaustionMsg);
                if (this.InvokeRequired)
                {
                    this.Invoke((MethodInvoker)delegate
                    {
                        aiMsg.PrepareNewStreamSection(exhaustionMsg);
                    });
                }
                else
                {
                    aiMsg.PrepareNewStreamSection(exhaustionMsg);
                }
            }
            }
            catch (OperationCanceledException)
            {
                // 用户点击停止按钮
                string stopMsg = "\n\n⏹ 已手动停止";
                memory?.AddAssistantMessage(stopMsg);
                if (aiMsg != null)
                {
                    if (this.InvokeRequired)
                        this.Invoke((MethodInvoker)delegate { aiMsg.PrepareNewStreamSection(stopMsg); });
                    else
                        aiMsg.PrepareNewStreamSection(stopMsg);
                }
            }
            finally
            {
                SetAssistantRunning(false);
            }
        }

        /// <summary>构建系统 Prompt，包含角色定义和行为规则</summary>
        private string BuildSystemPrompt(string mode, ToolRegistry toolRegistry, SkillManager skillManager)
        {
            var sb = new StringBuilder();

            sb.AppendLine("你是'福星'，一个深度集成到 Microsoft Word 插件中的智能文档助手。");
            sb.AppendLine("你可以通过工具调用直接操作用户的 Word 文档。");
            sb.AppendLine();

            // ── 注入当前活动文档信息，防止跨文档操作时上下文混淆 ──
            try
            {
                var app = Connect.CurrentInstance?.WordApplication;
                if (app != null && app.Documents.Count > 0)
                {
                    var doc = app.ActiveDocument;
                    string docName = doc.Name ?? "(未知)";
                    sb.AppendLine($"[活动文档：{docName}]");
                    sb.AppendLine("重要：所有文档操作必须以上方显示的活动文档为目标。忽略之前工具结果中出现的任何其他文件路径或文档名称（例如合并操作中的源文件）。始终对当前活动文档进行操作。");
                    sb.AppendLine();
                }
            }
            catch { /* 无活动文档时跳过 */ }

            sb.AppendLine("规则：");
            sb.AppendLine("1. 当用户请求文档操作时，直接调用适当的工具，而不是仅提供建议。");
            sb.AppendLine("2. 你会记住本次对话中执行的所有操作。");
            sb.AppendLine("3. 如果用户问你之前做了什么，根据对话历史如实回答。");
            sb.AppendLine("4. 简洁专业，始终用中文回复。");
            sb.AppendLine("5. 如果用户消息以'[用户附加了选中文本作为上下文...]'开头，说明用户在 Word 中选中了文本并自动附加。你可以直接分析、纠正或重写它，而无需调用 get_selected_text。");
            sb.AppendLine("6. 当工具调用失败时，分析错误信息。如果是参数问题，修复参数并重试一次。如果是系统错误，清楚地告知用户，而不是盲目重试。");
            sb.AppendLine("7. 如果你需要向用户确认信息、征求意见或请求额外输入，**必须首先调用 ask_user 工具**来提问，而不是直接发送文本消息。");
            sb.AppendLine();
            sb.AppendLine("工具安全规则：");
            sb.AppendLine("- 优先使用只读工具（get_document_info、get_document_map、get_selected_text、read_document_section、read_table、list_files）来收集信息。不要仅仅为了查看而修改文档。");
            sb.AppendLine("- execute_word_script 是一个危险的工具，可以运行任意代码。只有在其他专业工具无法完成任务时才将其作为最后手段使用。不要对现有工具已支持的任务调用它（例如格式化、插入文本/表格、页面设置）。");
            sb.AppendLine("- batch_operations 和 delete_section 是破坏性操作。在使用它们之前，特别是更改范围较大时，请确认用户的意图。");
            sb.AppendLine("- 始终优先选择破坏性最小的方法：使用 edit_document_text 而非 execute_word_script，使用 format_content 而非批量脚本重写。");
            sb.AppendLine("- 对于审核场景，优先使用 add_comment 和 toggle_track_changes 而非直接编辑，让用户可以接受/拒绝更改。");
            sb.AppendLine("- 使用 undo_redo 来撤销错误，而不是让用户手动操作。");
            sb.AppendLine("- 在执行复杂或不熟悉的操作之前，检查是否有相关的 Skill 并首先使用 load_skill 加载它。Skill 包含最佳实践和正确的 API 使用模式。");

            switch (mode)
            {
                case "编辑":
                    sb.AppendLine("8. 当前模式：编辑。专注于提高文本质量。");
                    break;
                case "审核":
                    sb.AppendLine("8. 当前模式：审核。专注于检查文档标准和准确性。");
                    break;
                default:
                    sb.AppendLine("8. 当前模式：问答。回答用户的问题。");
                    break;
            }

            // ── Skills 注入 ──
            string catalog = skillManager.BuildCatalogSummary();
            if (!string.IsNullOrEmpty(catalog))
            {
                sb.AppendLine();
                sb.AppendLine(catalog);
            }

            string activatedContent = skillManager.BuildActivatedSkillsContent();
            if (!string.IsNullOrEmpty(activatedContent))
            {
                sb.AppendLine();
                sb.AppendLine(activatedContent);
            }

            return sb.ToString();
        }

        /// <summary>估算工具定义的 token 占用（JSON Schema 主要是英文，约 4 字符/token）</summary>
        private static int EstimateToolDefinitionTokens(ToolRegistry toolRegistry)
        {
            var defs = toolRegistry.GetToolDefinitions();
            int totalChars = defs.ToString(Newtonsoft.Json.Formatting.None).Length;
            return (int)(totalChars / 4.0);
        }

        private void TriggerWordFunction(string functionName)
        {
            try
            {
                var connectInstance = Connect.CurrentInstance;
                if (connectInstance == null)
                {
                    MessageBox.Show("无法获取AI福星插件实例", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                switch (functionName)
                {
                    case "ai_text_correction_btn":
                        connectInstance.ai_text_correction_btn_Click(null);
                        break;
                    // CheckStandardValidityButton 已迁移到 AI 对话流程，不再通过 TriggerWordFunction 调用
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
                var uploadMsg = panel.AddMessage("AI福星", aiAvatar, ChatRole.AI);
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
                panel.AddMessage("AI福星", aiAvatar, ChatRole.AI,
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
                        panel.AddMessage("AI福星", aiAvatar, ChatRole.AI, $"❌ 文件上传失败: {ex.Message}");
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

        // ════════════════════════════════════════════════════════════════
        //  子智能体 UI 进度桥接
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// 将 ISubAgentProgress 回调桥接到 SubAgentBlock UI 更新。
        /// 自动处理 UI 线程调度。
        /// </summary>
        private class SubAgentUiProgress : ISubAgentProgress
        {
            private readonly UI.SubAgentBlock _block;
            private readonly Control _control;

            public SubAgentUiProgress(UI.SubAgentBlock block, Control control)
            {
                _block = block;
                _control = control;
            }

            private void OnUiThread(Action action)
            {
                if (_control.InvokeRequired)
                    _control.Invoke((MethodInvoker)delegate { action(); });
                else
                    action();
            }

            public void OnRoundStart(int round, int maxRounds)
            {
                OnUiThread(() => _block.AddRound(round, maxRounds));
            }

            public void OnThinking(string content)
            {
                if (string.IsNullOrEmpty(content)) return;
                OnUiThread(() => _block.AddThinking(content));
            }

            public void OnToolCallStart(string toolName)
            {
                OnUiThread(() => _block.AddToolCallStep(toolName));
            }

            public void OnToolCallEnd(string toolName, bool success, string output)
            {
                OnUiThread(() => _block.UpdateLastToolCallStep(success));
            }

            public void OnComplete(bool success, string output)
            {
                OnUiThread(() => _block.AddOutput(output, success));
            }
        }

        // 鼠标钩子相关
        private static IntPtr _mouseHookHandle = IntPtr.Zero;
        private static LowLevelMouseProc _mouseProc;
        private static TaskPaneControl _instance;

        /// <summary>
        /// 安装鼠标钩子以捕获滚轮消息
        /// </summary>
        private void InstallMouseHook()
        {
            if (_mouseHookHandle != IntPtr.Zero) return;

            _instance = this;
            _mouseProc = MouseHookCallback;
            using (var curProcess = System.Diagnostics.Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                _mouseHookHandle = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        /// <summary>
        /// 卸载鼠标钩子
        /// </summary>
        private void UninstallMouseHook()
        {
            if (_mouseHookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHookHandle);
                _mouseHookHandle = IntPtr.Zero;
            }
        }

        private static IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _instance != null && _instance._richChatPanel != null)
            {
                // WM_MOUSEWHEEL = 0x020A
                if (wParam == (IntPtr)WM_MOUSEWHEEL)
                {
                    var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    // 检查鼠标位置是否在 TaskPane 区域内
                    if (_instance.IsMouseInTaskPane(hookStruct.pt.x, hookStruct.pt.y))
                    {
                        // 将消息转发到 RichChatPanel
                        PostMessage(_instance._richChatPanel.Handle, WM_MOUSEWHEEL, wParam, lParam);
                        return (IntPtr)1; // 拦截消息
                    }
                }
            }
            return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
        }

        /// <summary>
        /// 检查鼠标是否在 TaskPane 区域内
        /// </summary>
        private bool IsMouseInTaskPane(int x, int y)
        {
            if (!IsHandleCreated) return false;
            var pt = PointToClient(new Point(x, y));
            return ClientRectangle.Contains(pt);
        }

        // P/Invoke 声明
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }
    }
}