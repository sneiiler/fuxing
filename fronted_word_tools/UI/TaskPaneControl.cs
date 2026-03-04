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
using FuXing.Core;
using FuXing.UI;
using FuXing.SubAgents;


namespace FuXing
{
    [ComVisible(true)]
    [Guid("03326A51-B257-3623-917E-25A086B271B0")]
    [ProgId("FuXing.TaskPaneControl")]
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

        // ── 会话管理 ──
        private ChatSession _currentSession;
        private SessionListPanel _sessionListPanel;
        private AntdUI.Label _sessionTitleLabel;

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
        private volatile bool _isComBusy; // COM 工具执行中标记（Timer 轮询检查用）

        // ── 选中文本上下文附着 ──
        private AntdUI.Panel _contextBarPanel;
        private AntdUI.Tag _selectionTag;
        private System.Windows.Forms.Timer _selectionTimer;
        private string _attachedSelectionText;

        // ── 文档感知状态栏 ──
        private System.Windows.Forms.Panel _perceptionBar;
        private System.Windows.Forms.Label _perceptionStatusLabel;
        private AntdUI.Button _deepPerceptionBtn;
        private System.Windows.Forms.TreeView _docTreeView;
        private bool _treeExpanded;

        // ── 问候消息（隐藏提示词，重建历史时需跳过）──
        private const string GreetingPrompt = "请用简洁友好的方式向用户打招呼，介绍你作为 Word 文档助手的核心能力（例如：文档编辑、格式调整、内容生成、纠错润色、文档分析等），然后询问用户想要做什么。控制在100字以内。";

        // ── 会话标题生成 ──
        private bool _titleGenerationTriggered;

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

                // 启动 Word 选区轮询监视器
                StartSelectionMonitor();
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
                // 停止选区轮询监视器
                if (_selectionTimer != null)
                {
                    _selectionTimer.Stop();
                    _selectionTimer.Dispose();
                    _selectionTimer = null;
                }

                // 取消订阅缓存变更事件
                DocumentGraphCache.Instance.CacheChanged -= OnGraphCacheChanged;

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

                    // 自动执行快速感知（无缓存时）
                    AutoPerceiveIfNeeded();
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
            if (IsDisposed || Disposing) return;

            // 控件句柄未就绪时延迟到 HandleCreated 重试，避免静默丢弃。
            if (!IsHandleCreated)
            {
                EventHandler handler = null;
                handler = (s, e) =>
                {
                    HandleCreated -= handler;
                    ShowStartupWarningIfNeeded();
                };
                HandleCreated += handler;
                return;
            }

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

            // 延迟到 UI 线程空闲时再发起问候，确保面板先完成首次绘制
            BeginInvoke((MethodInvoker)delegate { RequestGreetingAsync(); });
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
                memory.AddUserMessage(GreetingPrompt);

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

            ProcessAssistantMessage(message, null, panel);
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
            SuspendLayout();

            // 主容器使用 TableLayoutPanel 实现真正的flex布局
            var mainContainer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(248, 249, 250),
                Name = "MainChatContainer",
                Padding = new Padding(0),
                Margin = new Padding(0),
                ColumnCount = 1,
                RowCount = 7,
                CellBorderStyle = TableLayoutPanelCellBorderStyle.None
            };

            // 开启 TableLayoutPanel 的双缓冲，极大减少拖动时的闪烁和卡顿
            typeof(TableLayoutPanel).GetProperty("DoubleBuffered", 
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(mainContainer, true, null);

            // 设置行的大小类型：会话栏-状态栏-功能区-聊天-上下文条-感知栏-输入
            mainContainer.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));  // Row0: 会话工具栏
            mainContainer.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));  // Row1: 状态栏
            mainContainer.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));  // Row2: 顶部功能区
            mainContainer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // Row3: 聊天区域自适应
            mainContainer.RowStyles.Add(new RowStyle(SizeType.Absolute, 0f));   // Row4: 上下文条（默认隐藏）
            mainContainer.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));  // Row5: 文档感知状态栏
            mainContainer.RowStyles.Add(new RowStyle(SizeType.Absolute, 68f));  // Row6: 底部输入区（可自适应）

            // 设置列宽为100%
            mainContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            // 创建七个区域
            var sessionBar = CreateSessionToolbar();
            var statusBar = CreateStatusBar();
            var topPanel = CreateTopControlsPanel();
            var chatPanel = CreateRichChatPanel();
            var contextBar = CreateContextBar();
            var perceptionBar = CreatePerceptionBar();
            var bottomPanel = CreateBottomInputPanel();

            // 设置每个面板的停靠方式
            sessionBar.Dock = DockStyle.Fill;
            statusBar.Dock = DockStyle.Fill;
            topPanel.Dock = DockStyle.Fill;
            chatPanel.Dock = DockStyle.Fill;
            contextBar.Dock = DockStyle.Fill;
            perceptionBar.Dock = DockStyle.Fill;
            bottomPanel.Dock = DockStyle.Fill;

            // 按顺序添加到表格布局中
            mainContainer.SuspendLayout();
            mainContainer.Controls.Add(sessionBar, 0, 0);     // 第0行：会话工具栏
            mainContainer.Controls.Add(statusBar, 0, 1);      // 第1行：状态栏
            mainContainer.Controls.Add(topPanel, 0, 2);       // 第2行：功能区
            mainContainer.Controls.Add(chatPanel, 0, 3);      // 第3行：聊天区
            mainContainer.Controls.Add(contextBar, 0, 4);     // 第4行：上下文条
            mainContainer.Controls.Add(perceptionBar, 0, 5);  // 第5行：文档感知状态栏
            mainContainer.Controls.Add(bottomPanel, 0, 6);    // 第6行：输入区
            mainContainer.ResumeLayout(false);

            _mainContainer = mainContainer;
            Controls.Add(mainContainer);
            ResumeLayout(true);

            // ── 初始化会话列表面板（覆盖在聊天区域上方，默认隐藏）──
            _sessionListPanel = new SessionListPanel { Visible = false };
            _sessionListPanel.SessionSelected += OnSessionSelected;
            _sessionListPanel.SessionDeleted += OnSessionDeleted;
            _sessionListPanel.BackRequested += () => ShowSessionList(false);
            Controls.Add(_sessionListPanel);
            _sessionListPanel.BringToFront();

            // 保持 SessionListPanel 覆盖整个面板
            this.Resize += (s, e) =>
            {
                if (_sessionListPanel.Visible)
                    _sessionListPanel.Bounds = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
            };

            // ── 创建初始会话 ──
            _currentSession = SessionManager.Instance.CreateSession();
        }

        // ═══════════════════════════════════════════════════════════
        //  会话工具栏（Row 0）
        // ═══════════════════════════════════════════════════════════

        /// <summary>创建顶部会话工具栏：[+ 新对话] 标题 [≡ 历史]</summary>
        private AntdUI.Panel CreateSessionToolbar()
        {
            var bar = new AntdUI.Panel
            {
                Back = Color.White,
                Name = "SessionToolbar",
                Padding = new Padding(0),
                BorderWidth = 0
            };

            var newChatBtn = new AntdUI.Button
            {
                Text = "+ 新对话",
                Size = new Size(72, 26),
                Location = new Point(6, 5),
                Type = AntdUI.TTypeMini.Primary,
                Font = new Font("Microsoft YaHei UI", 8.5F),
                Radius = 6
            };
            newChatBtn.Click += (s, e) => StartNewSession();

            _sessionTitleLabel = new AntdUI.Label
            {
                Text = "新对话",
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = Color.FromArgb(75, 85, 99),
                Location = new Point(84, 8),
                Size = new Size(200, 22),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            };

            var historyBtn = new AntdUI.Button
            {
                Text = "≡",
                Size = new Size(36, 26),
                Location = new Point(300, 5),
                Type = AntdUI.TTypeMini.Default,
                Font = new Font("Microsoft YaHei UI", 12F),
                Radius = 6
            };
            historyBtn.Click += (s, e) => ShowSessionList(true);

            // 响应式布局
            bar.Resize += (s, e) =>
            {
                int pw = bar.ClientSize.Width;
                historyBtn.Left = pw - historyBtn.Width - 6;
                int titleLeft = newChatBtn.Right + 6;
                int titleWidth = historyBtn.Left - titleLeft - 6;
                _sessionTitleLabel.SetBounds(titleLeft, 8, Math.Max(40, titleWidth), 22);
            };

            // 底部分隔线
            bar.Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(229, 231, 235)))
                    e.Graphics.DrawLine(pen, 0, bar.Height - 1, bar.Width, bar.Height - 1);
            };

            bar.Controls.AddRange(new Control[] { newChatBtn, _sessionTitleLabel, historyBtn });
            return bar;
        }

        // ═══════════════════════════════════════════════════════════
        //  会话管理操作
        // ═══════════════════════════════════════════════════════════

        /// <summary>新建对话：保存当前会话，创建新会话，清空聊天面板</summary>
        private void StartNewSession()
        {
            if (_isAssistantRunning) return;

            // 保存当前会话
            SaveCurrentSession();

            // 创建新会话
            _currentSession = SessionManager.Instance.CreateSession();
            _titleGenerationTriggered = false;

            // 重置 UI
            Memory.Clear();
            _richChatPanel?.ClearAll();
            _hasRequestedGreeting = false;
            UpdateSessionTitleLabel();

            // 重新请求问候
            CheckAndRequestGreeting();
        }

        /// <summary>切换到指定会话</summary>
        private void SwitchToSession(string sessionId)
        {
            if (_isAssistantRunning) return;
            if (_currentSession?.Id == sessionId) { ShowSessionList(false); return; }

            // 保存当前会话
            SaveCurrentSession();

            // 加载目标会话
            var session = SessionManager.Instance.LoadSession(sessionId);
            if (session == null) return;

            _currentSession = session;
            _titleGenerationTriggered = !string.IsNullOrEmpty(session.Title) && session.Title != "新对话";

            // 重建 ChatMemory
            Memory.Clear();
            Memory.ImportMessages(session.Messages);

            // 重建聊天面板 UI
            RebuildChatPanelFromMemory();
            UpdateSessionTitleLabel();
            ShowSessionList(false);

            // 不需要再请求问候（历史会话已有内容）
            _hasRequestedGreeting = true;
        }

        /// <summary>删除指定会话</summary>
        private void DeleteSession(string sessionId)
        {
            // 如果删除的是当前会话，先新建一个
            bool isDeletingCurrent = _currentSession?.Id == sessionId;

            SessionManager.Instance.DeleteSession(sessionId);

            if (isDeletingCurrent)
            {
                _currentSession = SessionManager.Instance.CreateSession();
                _titleGenerationTriggered = false;
                Memory.Clear();
                _richChatPanel?.ClearAll();
                _hasRequestedGreeting = false;
                UpdateSessionTitleLabel();
                CheckAndRequestGreeting();
            }

            // 刷新列表
            RefreshSessionListPanel();
        }

        /// <summary>保存当前会话到磁盘</summary>
        private void SaveCurrentSession()
        {
            if (_currentSession == null) return;
            try
            {
                SessionManager.Instance.SaveSession(_currentSession, Memory);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SaveCurrentSession error: {ex.Message}");
            }
        }

        /// <summary>显示/隐藏会话列表面板</summary>
        private void ShowSessionList(bool show)
        {
            if (InvokeRequired) { Invoke((MethodInvoker)delegate { ShowSessionList(show); }); return; }

            if (show)
            {
                // 保存当前会话，确保列表数据最新
                SaveCurrentSession();
                RefreshSessionListPanel();
            }

            _sessionListPanel.Visible = show;

            if (show)
            {
                // 覆盖在主容器上方，占据整个面板
                _sessionListPanel.BringToFront();
                _sessionListPanel.Bounds = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
            }
        }

        /// <summary>刷新会话列表面板数据</summary>
        private void RefreshSessionListPanel()
        {
            var sessions = SessionManager.Instance.ListSessions();
            _sessionListPanel.RefreshList(sessions, _currentSession?.Id);
        }

        /// <summary>更新会话标题 Label 显示</summary>
        private void UpdateSessionTitleLabel()
        {
            if (_sessionTitleLabel == null) return;
            if (InvokeRequired) { Invoke((MethodInvoker)UpdateSessionTitleLabel); return; }

            string title = _currentSession?.Title;
            _sessionTitleLabel.Text = string.IsNullOrEmpty(title) ? "新对话" : title;
        }

        /// <summary>从 Memory 重建聊天面板中的消息气泡</summary>
        private void RebuildChatPanelFromMemory()
        {
            if (_richChatPanel == null) return;
            _richChatPanel.ClearAll();

            var messages = Memory.ExportMessages();
            if (messages == null) return;

            var aiAvatar = GetAIAvatar();
            var userAvatar = CreateAvatarImage("用户", Color.FromArgb(34, 197, 94));

            foreach (var msg in messages)
            {
                // 跳过系统消息和工具结果消息
                if (msg.Role == ChatMessageRole.System || msg.Role == ChatMessageRole.Tool)
                    continue;

                if (msg.Role == ChatMessageRole.User)
                {
                    // 跳过隐藏的问候触发消息
                    if (msg.Content == GreetingPrompt) continue;
                    _richChatPanel.AddMessage("用户", userAvatar, ChatRole.User, msg.Content ?? "");
                }
                else if (msg.Role == ChatMessageRole.Assistant && !string.IsNullOrEmpty(msg.Content))
                {
                    string displayText = StripThinkBlock(msg.Content);
                    if (!string.IsNullOrEmpty(displayText))
                        _richChatPanel.AddMessage("AI福星", aiAvatar, ChatRole.AI, displayText);
                }
            }
        }

        /// <summary>会话列表：用户点击了某条会话</summary>
        private void OnSessionSelected(string sessionId)
        {
            SwitchToSession(sessionId);
        }

        /// <summary>会话列表：用户点击了删除按钮</summary>
        private void OnSessionDeleted(string sessionId)
        {
            DeleteSession(sessionId);
        }

        /// <summary>
        /// 触发 LLM 标题生成（fire-and-forget）。
        /// 在第一轮用户+AI 对话完成后调用一次。
        /// </summary>
        private async void TriggerTitleGeneration()
        {
            if (_titleGenerationTriggered || _currentSession == null) return;
            _titleGenerationTriggered = true;

            try
            {
                // 从 Memory 中提取前几条消息作为摘要
                var messages = Memory.ExportMessages();
                if (messages == null || messages.Count < 2) return;

                var sb = new StringBuilder();
                int count = 0;
                foreach (var m in messages)
                {
                    if (m.Role == ChatMessageRole.System || m.Role == ChatMessageRole.Tool) continue;
                    string role = m.Role == ChatMessageRole.User ? "用户" : "AI";
                    string content = m.Content ?? "";
                    // 去除 <think> 块，标题生成只需要正文
                    content = StripThinkBlock(content);
                    if (content.Length > 200) content = content.Substring(0, 200) + "...";
                    sb.AppendLine($"{role}: {content}");
                    if (++count >= 4) break;
                }

                string title = await _networkHelper.GenerateTitleAsync(sb.ToString(), CancellationToken.None);
                if (string.IsNullOrEmpty(title)) return;

                _currentSession.Title = title;
                SessionManager.Instance.UpdateTitle(_currentSession.Id, title);
                UpdateSessionTitleLabel();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TriggerTitleGeneration error: {ex.Message}");
            }
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
                    _sendButton.Text = "停止";
                    _sendButton.Size = new Size(64, _sendButton.Height);
                    _sendButton.Type = AntdUI.TTypeMini.Error;
                }
                else
                {
                    _sendButton.Text = "发送";
                    _sendButton.Size = new Size(64, _sendButton.Height);
                    _sendButton.Type = AntdUI.TTypeMini.Primary;
                }
                // 触发底部面板重新布局
                _sendButton.Parent?.PerformLayout();
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

        // 创建上下文附着条（选中文本时显示 Tag 标签）
        private AntdUI.Panel CreateContextBar()
        {
            _contextBarPanel = new AntdUI.Panel
            {
                Back = Color.FromArgb(248, 249, 250),
                Name = "ContextBarPanel",
                Padding = new Padding(6, 2, 6, 2),
                Visible = true // 始终可见，靠行高 0 隐藏
            };

            _selectionTag = new AntdUI.Tag
            {
                Visible = false,
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = Color.FromArgb(59, 130, 246),
                BackColor = Color.FromArgb(239, 246, 255),
                BorderWidth = 1,
                Radius = 4,
                CloseIcon = true, // 显示 × 关闭按钮
                AutoSize = true,
                AutoSizeMode = AntdUI.TAutoSize.Auto,
                Location = new Point(6, 4),
                Cursor = Cursors.Hand
            };

            _selectionTag.CloseChanged += (s, e) =>
            {
                ClearAttachedSelection();
                return true; // 确认关闭
            };

            _contextBarPanel.Controls.Add(_selectionTag);
            return _contextBarPanel;
        }

        // ═══════════════════════════════════════════════════════════
        //  文档感知状态栏（Row 5）
        // ═══════════════════════════════════════════════════════════

        /// <summary>创建文档感知状态栏：左侧状态文字（可点击展开文档树） + 右侧「深度感知」按钮</summary>
        private System.Windows.Forms.Panel CreatePerceptionBar()
        {
            _perceptionBar = new System.Windows.Forms.Panel
            {
                BackColor = Color.FromArgb(248, 250, 252),
                Name = "PerceptionBar",
                Height = 30,
                Padding = new Padding(0)
            };

            _perceptionStatusLabel = new System.Windows.Forms.Label
            {
                Text = "⚠ 未感知",
                Font = new Font("Microsoft YaHei UI", 8F),
                ForeColor = Color.FromArgb(156, 163, 175),
                AutoSize = true,
                Location = new Point(8, 7),
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            _perceptionStatusLabel.Click += OnPerceptionStatusClick;

            _deepPerceptionBtn = new AntdUI.Button
            {
                Text = "感知 ▾",
                Size = new Size(64, 24),
                Font = new Font("Microsoft YaHei UI", 8F),
                Type = AntdUI.TTypeMini.Primary,
                Radius = 4,
                Anchor = AnchorStyles.Right
            };
            _deepPerceptionBtn.Click += OnPerceptionBtnShowMenu;

            // 文档结构树（初始隐藏）
            _docTreeView = new System.Windows.Forms.TreeView
            {
                Visible = false,
                Dock = DockStyle.None,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(248, 250, 252),
                Font = new Font("Microsoft YaHei UI", 8F),
                ForeColor = Color.FromArgb(55, 65, 81),
                ShowLines = true,
                ShowPlusMinus = true,
                ShowRootLines = false,
                FullRowSelect = true,
                ItemHeight = 22,
                Indent = 16
            };

            // 顶部分隔线
            _perceptionBar.Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(229, 231, 235)))
                    e.Graphics.DrawLine(pen, 0, 0, _perceptionBar.Width, 0);
            };

            // 响应式布局：状态行固定在底部 30px，树填充剩余空间
            _perceptionBar.Resize += (s, e) =>
            {
                int pw = _perceptionBar.ClientSize.Width;
                int ph = _perceptionBar.ClientSize.Height;
                _deepPerceptionBtn.Location = new Point(pw - _deepPerceptionBtn.Width - 6, ph - 27);
                _perceptionStatusLabel.Location = new Point(8, ph - 23);
                if (_docTreeView.Visible)
                {
                    int treeHeight = ph - 30;
                    _docTreeView.SetBounds(0, 0, pw, treeHeight > 0 ? treeHeight : 0);
                }
            };

            _perceptionBar.Controls.AddRange(new Control[] { _docTreeView, _perceptionStatusLabel, _deepPerceptionBtn });

            // 订阅缓存变更事件
            DocumentGraphCache.Instance.CacheChanged += OnGraphCacheChanged;

            return _perceptionBar;
        }

        /// <summary>点击状态文字，展开/收起文档结构树</summary>
        private void OnPerceptionStatusClick(object sender, EventArgs e)
        {
            if (_treeExpanded)
                CollapseDocTree();
            else
                ExpandDocTree();
        }

        /// <summary>展开文档结构树</summary>
        private void ExpandDocTree()
        {
            PopulateDocTree();
            if (_docTreeView.Nodes.Count == 0) return; // 无数据不展开

            _treeExpanded = true;
            _docTreeView.Visible = true;

            // 根据节点数量动态计算高度，最大 220px
            int desiredHeight = Math.Min(_docTreeView.GetNodeCount(true) * 22 + 8, 220);
            int totalHeight = desiredHeight + 30; // 树高度 + 底部状态行高度

            if (_mainContainer != null)
            {
                _mainContainer.RowStyles[5].Height = totalHeight;
                _mainContainer.PerformLayout();
            }
        }

        /// <summary>收起文档结构树</summary>
        private void CollapseDocTree()
        {
            _treeExpanded = false;
            _docTreeView.Visible = false;

            if (_mainContainer != null)
            {
                _mainContainer.RowStyles[5].Height = 30f;
                _mainContainer.PerformLayout();
            }
        }

        /// <summary>根据当前文档图填充 TreeView 节点</summary>
        private void PopulateDocTree()
        {
            _docTreeView.Nodes.Clear();

            try
            {
                var doc = _application?.ActiveDocument;
                if (doc == null) return;

                var graph = DocumentGraphCache.Instance.GetCached(doc.FullName);
                if (graph?.Root == null) return;

                _docTreeView.BeginUpdate();
                foreach (var childId in graph.Root.ChildIds)
                    AddTreeNode(_docTreeView.Nodes, graph, childId);
                _docTreeView.ExpandAll();
                _docTreeView.EndUpdate();
            }
            catch
            {
                // 静默忽略
            }
        }

        /// <summary>递归添加文档图节点到 TreeView</summary>
        private void AddTreeNode(System.Windows.Forms.TreeNodeCollection parent, DocumentGraph graph, string nodeId)
        {
            var node = graph.GetById(nodeId);
            if (node == null) return;

            // 跳过 Heading 节点 — Section 已携带标题，Heading 是实现细节
            if (node.Type == DocNodeType.Heading) return;

            string icon = GetTreeNodeIcon(node.Type);
            string text = $"{icon} {node.Title}";

            var treeNode = new System.Windows.Forms.TreeNode(text) { Tag = node.Id };

            // 根据节点类型设置颜色
            switch (node.Type)
            {
                case DocNodeType.Section:
                    treeNode.ForeColor = Color.FromArgb(30, 64, 175);  // 蓝色
                    break;
                case DocNodeType.Table:
                    treeNode.ForeColor = Color.FromArgb(180, 83, 9);   // 琥珀
                    break;
                case DocNodeType.Image:
                    treeNode.ForeColor = Color.FromArgb(124, 58, 237); // 紫色
                    break;
                case DocNodeType.List:
                    treeNode.ForeColor = Color.FromArgb(4, 120, 87);   // 绿色
                    break;
            }

            parent.Add(treeNode);

            // Section 递归展示子节点（跳过 Heading 后只剩内容元素和子 Section）
            if (node.Type == DocNodeType.Section || node.Type == DocNodeType.Document)
            {
                foreach (var childId in node.ChildIds)
                    AddTreeNode(treeNode.Nodes, graph, childId);
            }
        }

        private static string GetTreeNodeIcon(DocNodeType type)
        {
            switch (type)
            {
                case DocNodeType.Section: return "§";
                case DocNodeType.Heading: return "H";
                case DocNodeType.Preamble: return "📝";
                case DocNodeType.Table: return "📋";
                case DocNodeType.Image: return "🖼";
                case DocNodeType.TextBlock: return "¶";
                case DocNodeType.List: return "📌";
                default: return "•";
            }
        }

        /// <summary>自动感知：TaskPane 可见时若当前文档无缓存则执行快速感知</summary>
        private async void AutoPerceiveIfNeeded()
        {
            try
            {
                // Word COM 可能尚未就绪，延迟等待 ActiveDocument 可用
                NetOffice.WordApi.Document doc = null;
                for (int retry = 0; retry < 6; retry++)
                {
                    try { doc = _application?.ActiveDocument; } catch { }
                    if (doc != null) break;
                    await Task.Delay(500);
                }

                if (doc == null) return;

                var cached = DocumentGraphCache.Instance.GetCached(doc.FullName);
                if (cached != null)
                {
                    RefreshPerceptionStatus();
                    return;
                }

                // 异步执行快速感知
                if (InvokeRequired)
                    Invoke((MethodInvoker)delegate {
                        _perceptionStatusLabel.Text = "⏳ 正在快速感知…";
                        _perceptionStatusLabel.ForeColor = Color.FromArgb(245, 158, 11);
                        _deepPerceptionBtn.Enabled = false;
                    });
                else
                {
                    _perceptionStatusLabel.Text = "⏳ 正在快速感知…";
                    _perceptionStatusLabel.ForeColor = Color.FromArgb(245, 158, 11);
                    _deepPerceptionBtn.Enabled = false;
                }

                await DocumentGraphCache.Instance.GetOrBuildAsync(doc, deep: false);
                // CacheChanged 事件会自动触发 RefreshPerceptionStatus
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AutoPerceive error: {ex.Message}");
                RefreshPerceptionStatus();
            }
            finally
            {
                if (IsHandleCreated && !IsDisposed)
                {
                    if (InvokeRequired)
                        BeginInvoke((MethodInvoker)delegate { _deepPerceptionBtn.Enabled = true; });
                    else
                        _deepPerceptionBtn.Enabled = true;
                }
            }
        }

        /// <summary>缓存变更时刷新感知状态栏</summary>
        private void OnGraphCacheChanged(object sender, GraphCacheChangedEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke((MethodInvoker)delegate { RefreshPerceptionStatus(); });
                return;
            }
            RefreshPerceptionStatus();
        }

        /// <summary>刷新文档感知状态栏显示</summary>
        private void RefreshPerceptionStatus()
        {
            if (_perceptionStatusLabel == null) return;
            if (InvokeRequired) { Invoke((MethodInvoker)RefreshPerceptionStatus); return; }

            string arrow = _treeExpanded ? "▾" : "▸";
            bool hasGraph = false;

            try
            {
                var doc = _application?.ActiveDocument;
                if (doc == null)
                {
                    _perceptionStatusLabel.Text = "⚠ 无文档";
                    _perceptionStatusLabel.ForeColor = Color.FromArgb(156, 163, 175);
                    _perceptionStatusLabel.Cursor = Cursors.Default;
                    return;
                }

                var graph = DocumentGraphCache.Instance.GetCached(doc.FullName);
                if (graph == null)
                {
                    _perceptionStatusLabel.Text = "⚠ 未感知";
                    _perceptionStatusLabel.ForeColor = Color.FromArgb(156, 163, 175);
                    _perceptionStatusLabel.Cursor = Cursors.Default;
                    return;
                }

                hasGraph = true;
                int sectionCount = graph.FindByType(DocNodeType.Section).Count;

                if (graph.IsDeepPerception)
                {
                    _perceptionStatusLabel.Text = $"{arrow} ✓ 深度感知 ({sectionCount}个章节)";
                    _perceptionStatusLabel.ForeColor = Color.FromArgb(34, 197, 94); // 绿色
                }
                else
                {
                    _perceptionStatusLabel.Text = $"{arrow} ✓ 快速感知 ({sectionCount}个章节)";
                    _perceptionStatusLabel.ForeColor = Color.FromArgb(59, 130, 246); // 蓝色
                }
                _perceptionStatusLabel.Cursor = Cursors.Hand;
            }
            catch
            {
                _perceptionStatusLabel.Text = "⚠ 未感知";
                _perceptionStatusLabel.ForeColor = Color.FromArgb(156, 163, 175);
                _perceptionStatusLabel.Cursor = Cursors.Default;
            }

            // 树展开中时同步刷新树内容
            if (_treeExpanded && hasGraph)
                PopulateDocTree();
        }

        /// <summary>点击感知按钮，弹出模式选择菜单</summary>
        private void OnPerceptionBtnShowMenu(object sender, EventArgs e)
        {
            var menu = new ContextMenuStrip();
            menu.Font = new Font("Microsoft YaHei UI", 9F);

            var fastItem = new ToolStripMenuItem("⚡ 快速感知");
            fastItem.ToolTipText = "基于大纲级别快速构建文档结构";
            fastItem.Click += async (s2, e2) => await ExecutePerception(false);

            var deepItem = new ToolStripMenuItem("🧠 深度感知");
            deepItem.ToolTipText = "使用 AI 推断标题层级，适用于未使用标题样式的文档";
            deepItem.Click += async (s2, e2) => await ExecutePerception(true);

            // 标记当前模式
            try
            {
                var doc = _application?.ActiveDocument;
                if (doc != null)
                {
                    var cached = DocumentGraphCache.Instance.GetCached(doc.FullName);
                    if (cached != null)
                    {
                        if (cached.IsDeepPerception)
                            deepItem.Checked = true;
                        else
                            fastItem.Checked = true;
                    }
                }
            }
            catch { }

            menu.Items.AddRange(new ToolStripItem[] { fastItem, deepItem });

            var btn = sender as Control;
            if (btn != null)
                menu.Show(btn, new Point(0, -menu.PreferredSize.Height));
            else
                menu.Show(Cursor.Position);
        }

        /// <summary>执行指定模式的文档感知</summary>
        private async Task ExecutePerception(bool deep)
        {
            if (_application?.ActiveDocument == null) return;

            var doc = _application.ActiveDocument;
            string modeLabel = deep ? "深度" : "快速";

            _deepPerceptionBtn.Enabled = false;
            _deepPerceptionBtn.Text = "感知中…";
            _perceptionStatusLabel.Text = $"⏳ 正在{modeLabel}感知…";
            _perceptionStatusLabel.ForeColor = Color.FromArgb(245, 158, 11);

            try
            {
                DocumentGraphCache.Instance.Invalidate(doc);
                await DocumentGraphCache.Instance.GetOrBuildAsync(doc, deep: deep);
                if (!_treeExpanded)
                    ExpandDocTree();
            }
            catch (Exception ex)
            {
                _perceptionStatusLabel.Text = $"✗ {modeLabel}感知失败";
                _perceptionStatusLabel.ForeColor = Color.FromArgb(239, 68, 68);
                System.Diagnostics.Debug.WriteLine($"Perception error: {ex.Message}");
            }
            finally
            {
                _deepPerceptionBtn.Enabled = true;
                _deepPerceptionBtn.Text = "感知 ▾";
                RefreshPerceptionStatus();
            }
        }

        /// <summary>设置附着的选中文本（显示 Tag + 展开上下文条）</summary>
        private void SetAttachedSelection(string fullText)
        {
            if (string.IsNullOrWhiteSpace(fullText)) return;

            _attachedSelectionText = fullText;

            // Tag 显示格式：📎 "前20字..." (N字)
            string preview = fullText.Length > 20
                ? fullText.Substring(0, 20) + "…"
                : fullText;
            _selectionTag.Text = $"📎 \"{preview}\" ({fullText.Length}字)";
            _selectionTag.Visible = true;

            // 展开上下文条行
            if (_mainContainer != null)
            {
                _mainContainer.RowStyles[4].Height = 34f;
                _mainContainer.PerformLayout();
            }
        }

        /// <summary>清除附着的选中文本（隐藏 Tag + 收起上下文条）</summary>
        private void ClearAttachedSelection()
        {
            _attachedSelectionText = null;
            if (_selectionTag != null)
                _selectionTag.Visible = false;

            // 收起上下文条行
            if (_mainContainer != null)
            {
                _mainContainer.RowStyles[4].Height = 0f;
                _mainContainer.PerformLayout();
            }
        }

        /// <summary>启动 Word 选区轮询监视器</summary>
        private void StartSelectionMonitor()
        {
            _selectionTimer = new System.Windows.Forms.Timer { Interval = 500 };
            _selectionTimer.Tick += (s, e) =>
            {
                // COM 工具执行中不更新，避免 COM 冲突
                // 注意：等待用户输入（ask_user）期间允许轮询，因为 COM 未被占用
                if (_isComBusy) return;

                // TaskPane 不可见时跳过 COM 轮询，节省资源
                try { if (_parentPane != null && !_parentPane.Visible) return; } catch { return; }

                try
                {
                    var app = _application;
                    if (app == null) return;

                    var sel = app.Selection;
                    if (sel == null) return;

                    bool hasSelection = sel.Start != sel.End;
                    if (hasSelection)
                    {
                        string text = sel.Text?.TrimEnd('\r', '\n');
                        if (!string.IsNullOrEmpty(text) && text != _attachedSelectionText)
                            SetAttachedSelection(text);
                    }
                    else
                    {
                        // 无选区 → 如果当前有附着则清除
                        if (_attachedSelectionText != null)
                            ClearAttachedSelection();
                    }
                }
                catch
                {
                    // Word COM 访问失败时静默忽略（文档切换、关闭等场景）
                }
            };
            _selectionTimer.Start();
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

            int gap = 6;

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
                Size = new Size(64, 40),
                Type = AntdUI.TTypeMini.Primary,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
                Name = "SendButton",
                Radius = 8
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
                        if (Math.Abs(_mainContainer.RowStyles[6].Height - desiredPanelH) > 2)
                        {
                            _mainContainer.RowStyles[6].Height = desiredPanelH;
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

                int btnW = sendBtn.Width;
                int inputW = pw - btnW - gap * 3;
                int inputH = ph - gap * 2;
                inputTextBox.SetBounds(gap, gap, inputW, inputH);
                sendBtn.SetBounds(pw - btnW - gap, ph - sendBtn.Height - gap, btnW, sendBtn.Height);
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

                // 捕获并清除附着的选中文本（发送前快照，发送后清空 Tag）
                string attachedText = _attachedSelectionText;
                ClearAttachedSelection();

                // 清空输入框
                inputTextBox.Text = "";

                // 处理消息并添加助手回复
                ProcessAssistantMessage(userMessage, attachedText, panel);

                System.Diagnostics.Debug.WriteLine($"User message added. Message count: {panel.MessageCount}");
            }
        }

        private async void ProcessAssistantMessage(string userMessage, string attachedSelectionText, RichChatPanel panel)
        {
            _agentCts?.Dispose();
            _agentCts = new CancellationTokenSource();
            var ct = _agentCts.Token;

            // 在发送消息的瞬间捕获光标位置快照，供后续工具使用
            // （生成过程中用户可能点击其他位置，不能依赖实时 Selection）
            try
            {
                var connect = Connect.CurrentInstance;
                var app = connect?.WordApplication;
                if (app != null)
                {
                    var sel = app.Selection;
                    connect.SelectionSnapshot = new CursorSnapshot(sel.Start, sel.End);
                    System.Diagnostics.Debug.WriteLine(
                        $"[CursorSnapshot] 已捕获: Start={sel.Start}, End={sel.End}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CursorSnapshot] 捕获失败: {ex.Message}");
            }

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

            // ── 自动注入上下文 ──
            // 优先使用用户显式附着的选中文本；否则回退到 CursorContextProvider 自动采集
            if (!string.IsNullOrEmpty(attachedSelectionText))
            {
                string selectionContext = CursorContextProvider.BuildSelectionContextFromText(attachedSelectionText);
                if (!string.IsNullOrEmpty(selectionContext))
                    userMessage = selectionContext + "\n" + userMessage;
            }
            else
            {
                string contextPrefix = CursorContextProvider.BuildContextPrefix(connect.WordApplication);
                if (!string.IsNullOrEmpty(contextPrefix))
                    userMessage = contextPrefix + "\n" + userMessage;
            }

            // 记录用户消息到上下文
            memory.AddUserMessage(userMessage);
            DebugLogger.Instance.LogSessionStart();
            DebugLogger.Instance.LogSystemPrompt(memory.GetSystemPrompt());
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
                    DebugLogger.Instance.LogError("SendStreamChat", "请求失败，result=" + (result == null ? "null" : "hasError") + ", accumulatedResponse=" + accumulatedResponse);
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
                    DebugLogger.Instance.LogAssistantToolCallMessage(result.Content, result.ToolCalls);
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
                            DebugLogger.Instance.LogToolSkipped(tc.FunctionName, tc.Id, skipMsg);
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

                            bool approved = await AwaitOrCancel(approvalCard.ResultTask, ct);
                            DebugLogger.Instance.LogApproval(tc.FunctionName, tc.Id, approved, summary);

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

                            string userAnswer = await AwaitOrCancel(askCard.ResultTask, ct);

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
                            string taskLabel = tc.Arguments?["agent_name"]?.ToString() ?? "子智能体";
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
                        _isComBusy = true;
                        try
                        {
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
                        }
                        finally
                        {
                            _isComBusy = false;
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
                    DebugLogger.Instance.LogTruncation(round);
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
                // 清除光标快照，本轮助手回合结束
                try { var c = Connect.CurrentInstance; if (c != null) c.SelectionSnapshot = null; } catch { }
                SetAssistantRunning(false);

                // ── 会话自动保存 & 标题生成 ──
                SaveCurrentSession();
                TriggerTitleGeneration();
            }
        }

        /// <summary>构建系统 Prompt，包含角色定义和行为规则</summary>
        private string BuildSystemPrompt(string mode, ToolRegistry toolRegistry, SkillManager skillManager)
        {
            var sb = new StringBuilder();

            // 1. 角色定义与模式
            sb.AppendLine("你是'福星'，深度集成在 Microsoft Word 中的智能文档助手。");
            sb.AppendLine($"当前工作模式：【{mode}】。");
            if (mode == "编辑") sb.AppendLine("专注于文本润色、改写和质量提升。");
            else if (mode == "审核") sb.AppendLine("专注于检查文档规范、逻辑准确性和错漏。");
            else sb.AppendLine("专注于精准解答问题和执行用户的文档操作指令。");
            sb.AppendLine("要求：简洁专业，始终用中文回复。");
            sb.AppendLine();

            // 2. 当前文档上下文
            try
            {
                var app = Connect.CurrentInstance?.WordApplication;
                if (app != null && app.Documents.Count > 0)
                {
                    var doc = app.ActiveDocument;
                    string docName = doc.Name ?? "(未知)";
                    sb.AppendLine($"[当前活动文档：{docName}]");
                    sb.AppendLine("⚠️ 绝对原则：你的所有操作和工具调用必须针对此活动文档进行。忽略对话历史中的其他文件名。");
                    sb.AppendLine();
                }
            }
            catch { /* 无活动文档时跳过 */ }

            // 3. 核心行为准则（极其重要）
            sb.AppendLine("### 核心行为准则（严格遵守）");
            sb.AppendLine("- **行动优先**：当用户请求文档操作时，**必须直接调用相关工具**执行，切勿仅停留在口头建议或步骤说明。");
            sb.AppendLine("- **提问与确认（ask_user）**：任何需要用户确认意图、选择方案、补充参数（如标题名、范围）的场景，**强制调用 `ask_user` 工具提问**。");
            sb.AppendLine("  🚫 严禁：严禁在你的常规文本回复 (content) 中向用户发问然后再结束生成。必须且只能通过 `ask_user` 工具来接收用户输入！");
            sb.AppendLine("- **错误处理**：当工具调用失败时，分析错误信息。若是参数问题则修正重试一次；若是系统错误则告知用户，避免无限重试。");
            sb.AppendLine();

            // 4. 输入上下文解析
            sb.AppendLine("### 隐式上下文解析");
            sb.AppendLine("- 用户的消息前可能会被系统自动注入前缀：");
            sb.AppendLine("  - `[用户附加了选中文本作为上下文]`：说明用户已选中目标文本，直接对其进行操作分析，无需再调用 get_selected_text。");
            sb.AppendLine("  - `[光标位置上下文]`：包含光标周围的段落。请利用前文/后文推断用户想在何处补充或修改什么。");
            sb.AppendLine();

            // 5. 工具使用策略（动态生成工具列表，避免硬编码工具名）
            var readOnlyTools = toolRegistry.GetToolNamesByCategory(ToolCategory.Query);
            var dangerousTools = toolRegistry.GetDangerousToolNames();
            string readOnlyList = string.Join(", ", readOnlyTools);
            string dangerousList = string.Join(", ", dangerousTools);

            sb.AppendLine("### 工具与最佳实践策略");
            sb.AppendLine($"- **“先看后动”原则**：优先使用只读工具（{readOnlyList} 等）获取状态，避免盲目修改。");
            sb.AppendLine("- **最小破坏原则**：");
            sb.AppendLine("  - 优先用轻量编辑工具（edit_document_text、format_content 等）。");
            sb.AppendLine("  - 当要写入的内容包含 Markdown 语法（如 #、-、*、```、表格）时，仍调用 edit_document_text，工具会自动解析 Markdown 并按默认格式写入文档。");
            sb.AppendLine($"  - 危险工具（{dangerousList}）需极为谨慎；执行大范围修改前，用 `ask_user` 询问确认。");
            sb.AppendLine("- **审核场景**：推荐用批注和修订追踪代替直接编辑，保留追踪痕迹。");
            sb.AppendLine("- **Skill（技能）先行**：应对复杂操作时，优先检查是否有现成实践经验可加载。");
            sb.AppendLine("- **委派子智能体（run_sub_agent）**：");
            sb.AppendLine("  - 面对大量阅读、多处交叉比对的高负载任务，为防止上下文撑爆，委派给子智能体。");
            sb.AppendLine("  - 必须为子智能体设定明确的 system_prompt（指派其专有角色和输出要求），并收紧 allowed_tools 只给必要工具。");

            // 6. 动态技能注入
            string catalog = skillManager.BuildCatalogSummary();
            if (!string.IsNullOrEmpty(catalog))
            {
                sb.AppendLine();
                sb.AppendLine("### 可用技能");
                sb.AppendLine(catalog);
            }

            string activatedContent = skillManager.BuildActivatedSkillsContent();
            if (!string.IsNullOrEmpty(activatedContent))
            {
                sb.AppendLine();
                sb.AppendLine("### 已激活的技能知识");
                sb.AppendLine(activatedContent);
            }

            // 7. 末尾强化记忆
            sb.AppendLine();
            sb.AppendLine("<CRITICAL_REMINDER>");
            sb.AppendLine("1. 能够调用工具完成的操作就立刻调用工具。");
            sb.AppendLine("2. 只要向用户提问、让用户做选择，绝对只能使用 `ask_user` 工具！！！");
            sb.AppendLine("</CRITICAL_REMINDER>");

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

            public void OnLlmCallStart()
            {
                OnUiThread(() => _block.AddThinkingPlaceholder());
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



        /// <summary>
        /// 将 TaskCompletionSource 结果与 CancellationToken 关联，使 Stop 按钮可中断等待。
        /// </summary>
        private static async Task<T> AwaitOrCancel<T>(Task<T> task, CancellationToken ct)
        {
            var cancelTcs = new TaskCompletionSource<T>();
            using (ct.Register(() => cancelTcs.TrySetCanceled()))
            {
                var winner = await Task.WhenAny(task, cancelTcs.Task);
                return await winner; // 若 cancelTcs 胜出则抛 OperationCanceledException
            }
        }

    }
}