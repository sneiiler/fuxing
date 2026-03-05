using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FuXingAgent.Agents;
using FuXingAgent.Core;
using FuXingAgent.UI;
using Word = Microsoft.Office.Interop.Word;
using UChatRole = FuXingAgent.UI.ChatRole;
using System.Collections.Generic;
using Microsoft.Extensions.AI;
using Microsoft.Agents.AI;

namespace FuXingAgent
{
#pragma warning disable MEAI001
    /// <summary>
    /// 主任务窗格 — 三区布局：顶栏 / 聊天 / 底部输入。
    /// 辅助信息（选中文本、感知状态、上下文用量）内联到顶栏和底部 Chip 栏，
    /// 最大化聊天区可用空间。
    /// </summary>
    [ComVisible(true)]
    [Guid("E1F2A3B4-C5D6-7890-ABCD-EF1234567890")]
    [ProgId("FuXingAgent.TaskPaneHost")]
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public class TaskPaneHost : UserControl
    {
        // ═══════════════════════════════════════════════════════════════
        //  核心状态
        // ═══════════════════════════════════════════════════════════════

        private Connect _connect;
        private ChatSession _currentSession;
        private AgentSession _agentSession;
        private CancellationTokenSource _agentCts;

        // ═══════════════════════════════════════════════════════════════
        //  UI — 顶栏
        // ═══════════════════════════════════════════════════════════════

        private Panel _header;
        private AntdUI.Button _newChatBtn;
        private AntdUI.Label _titleLabel;
        private Label _ctxDot;
        private ToolTip _tip;
        private AntdUI.Button _historyBtn;

        // ═══════════════════════════════════════════════════════════════
        //  UI — 聊天区
        // ═══════════════════════════════════════════════════════════════

        private RichChatPanel _chatPanel;

        // ═══════════════════════════════════════════════════════════════
        //  UI — 底部
        // ═══════════════════════════════════════════════════════════════

        private Panel _bottom;
        private Panel _chipBar;
        private AntdUI.Tag _selChip;
        private AntdUI.Button _percBtn;
        private Panel _inputRow;
        private AntdUI.Input _inputBox;
        private AntdUI.Button _sendBtn;

        // ═══════════════════════════════════════════════════════════════
        //  UI — 浮层
        // ═══════════════════════════════════════════════════════════════

        private Panel _treePopup;
        private TreeView _treeView;
        private Label _treeLbl;          // 进度行右侧短文字
        private Label _treeEmpty;        // 空状态 / 感知中中心提示
        private Panel _percRow;          // 进度行容器
        private AntdUI.Progress _percProgress;
        private SessionListPanel _sessionList;

        private sealed class DocTreeAnchor
        {
            public int Start { get; }
            public DocTreeAnchor(int start) { Start = start; }
        }

        // ═══════════════════════════════════════════════════════════════
        //  运行时
        // ═══════════════════════════════════════════════════════════════

        private bool _inSessionView;
        private bool _isRunning;
        private bool _sendGuard;
        private bool _greetDone;
        private Image _aiAvatar;
        private Image _userAvatar;

        private string _selText;
        private System.Windows.Forms.Timer _selTimer;
        private string _selSnapshot;

        private readonly object _sLock = new object();
        private readonly StringBuilder _sPending = new StringBuilder();
        private readonly StringBuilder _sAll = new StringBuilder();
        private MessageGroup _sTarget;
        private System.Windows.Forms.Timer _sTimer;
        private bool _sStripThink;
        private string _percSig;
        private bool _deepPerception;
        private bool _isPerceiving;
        private string _cachedSoulContent;
        private DateTime _cachedSoulModTime;
        private ConfigLoader.Config _cachedConfig;
        private DateTime _configCacheTime;

        private const string GreetPrompt =
            "请用简洁友好的方式向用户打招呼，介绍你作为 Word 文档助手的核心能力，然后询问用户想做什么。控制在100字以内。";

        // 布局基准（96 DPI）
        private const int B_Header = 44;
        private const int B_ChipBar = 42;
        private const int B_InputBase = 52;
        private const int B_InputMax = 128;
        private const int B_BottomPad = 8;

        private int H_Header => S(B_Header);
        private int H_ChipBar => S(B_ChipBar);
        private int H_InputBase => S(B_InputBase);
        private int H_InputMax => S(B_InputMax);
        private int H_BottomPad => S(B_BottomPad);

        private int S(int v) => UiScale.Scale(this, v);

        // 颜色
        private static readonly Color Bg = Color.White;
        private static readonly Color BgChat = Color.FromArgb(249, 250, 251);
        private static readonly Color Border = Color.FromArgb(229, 231, 235);
        private static readonly Color Fg1 = Color.FromArgb(17, 24, 39);
        private static readonly Color Fg2 = Color.FromArgb(107, 114, 128);
        private static readonly Color CBlue = Color.FromArgb(59, 130, 246);
        private static readonly Color CGreen = Color.FromArgb(34, 197, 94);
        private static readonly Color CYellow = Color.FromArgb(245, 158, 11);
        private static readonly Color CRed = Color.FromArgb(239, 68, 68);

        // ═══════════════════════════════════════════════════════════════
        //  构造 / 公开接口
        // ═══════════════════════════════════════════════════════════════

        public TaskPaneHost() { InitUI(); }

        public void Initialize(Connect connect)
        {
            _connect = connect ?? throw new InvalidOperationException("Connect 不能为空");
            _aiAvatar = ResourceManager.GetIcon("logo.png");
            _userAvatar = ResourceManager.GetIcon("user.png") ?? throw new InvalidOperationException("缺少资源文件: Resources/user.png");

            DocumentGraphCache.Instance.CacheChanged += OnGraphCacheChanged;

            BindSessionEvents();
            StartSelectionMonitor();
            StartNewSession();
            RefreshCtx();
        }

        public void InjectUserMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            if (_inSessionView) ToggleSessions(false);
            _inputBox.Text = message;
            _ = SendAsync();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DocumentGraphCache.Instance.CacheChanged -= OnGraphCacheChanged;
                _agentCts?.Cancel(); _agentCts?.Dispose();
                _selTimer?.Stop(); _selTimer?.Dispose();
                _sTimer?.Stop(); _sTimer?.Dispose();
                _aiAvatar?.Dispose(); _userAvatar?.Dispose();
            }
            base.Dispose(disposing);
        }

        // ═══════════════════════════════════════════════════════════════
        //  UI 构建
        // ═══════════════════════════════════════════════════════════════

        private void InitUI()
        {
            SuspendLayout();
            BackColor = Bg;
            Dock = DockStyle.Fill;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 9F * UiScale.GetScaleForControl(this));

            _header = BuildHeader();
            _bottom = BuildBottom();
            _chatPanel = new RichChatPanel { Dock = DockStyle.Fill, BackColor = BgChat };

            // Dock 加入顺序：Fill → Bottom → Top
            Controls.Add(_chatPanel);
            Controls.Add(_bottom);
            Controls.Add(_header);

            _sessionList = new SessionListPanel { Visible = false };
            Controls.Add(_sessionList);
            _sessionList.BringToFront();

            _treePopup = BuildTreePopup();
            Controls.Add(_treePopup);
            _treePopup.BringToFront();

            _tip = new ToolTip();

            Resize += (s, e) =>
            {
                if (_sessionList.Visible)
                    _sessionList.Bounds = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
                if (_treePopup.Visible)
                    PlaceTreePopup();
            };

            _bottom.SizeChanged += (s, e) =>
            {
                if (_treePopup.Visible) PlaceTreePopup();
            };

            _sTimer = new System.Windows.Forms.Timer { Interval = 40 };
            _sTimer.Tick += (s, e) => FlushStream();

            ResumeLayout(true);
        }

        // ── 顶栏 ──────────────────────────────────────────────────────

        private Panel BuildHeader()
        {
            var p = new Panel { Dock = DockStyle.Top, Height = H_Header, BackColor = Bg };
            DB(p);

            _newChatBtn = new AntdUI.Button
            {
                Text = "+",
                Size = new Size(S(32), S(32)),
                Type = AntdUI.TTypeMini.Default,
                Font = new Font("Microsoft YaHei UI", 14F * UiScale.GetScaleForControl(this)),
                Radius = 2
            };
            _newChatBtn.Click += (s, e) => StartNewSession();

            _titleLabel = new AntdUI.Label
            {
                Text = "新对话",
                Font = new Font("Microsoft YaHei UI", 10F * UiScale.GetScaleForControl(this), FontStyle.Bold),
                ForeColor = Fg1,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            };

            _ctxDot = new Label
            {
                Text = "●",
                Font = new Font("Microsoft YaHei UI", 9F * UiScale.GetScaleForControl(this)),
                ForeColor = CGreen,
                AutoSize = true,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };

            _historyBtn = new AntdUI.Button
            {
                Text = "≡",
                Size = new Size(S(32), S(32)),
                Type = AntdUI.TTypeMini.Default,
                Font = new Font("Microsoft YaHei UI", 14F * UiScale.GetScaleForControl(this)),
                Radius = 2
            };
            _historyBtn.Click += (s, e) => ToggleSessions(true);

            p.Resize += (s, e) =>
            {
                int w = p.ClientSize.Width;
                _newChatBtn.Location = new Point(S(8), S(6));
                _historyBtn.Location = new Point(w - S(40), S(6));
                _ctxDot.Location = new Point(w - S(64), S(13));
                int tl = _newChatBtn.Right + S(8);
                int tw = _ctxDot.Left - tl - S(8);
                _titleLabel.SetBounds(tl, S(6), Math.Max(S(40), tw), S(32));
            };

            p.Paint += (s, e) =>
            {
                using (var pen = new Pen(Border))
                    e.Graphics.DrawLine(pen, 0, p.Height - 1, p.Width, p.Height - 1);
            };

            p.Controls.AddRange(new Control[] { _newChatBtn, _titleLabel, _ctxDot, _historyBtn });
            return p;
        }

        // ── 底部区域 ──────────────────────────────────────────────────

        private Panel BuildBottom()
        {
            var sec = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = H_ChipBar + H_InputBase + H_BottomPad,
                BackColor = Bg
            };
            DB(sec);

            sec.Paint += (s, e) =>
            {
                using (var pen = new Pen(Border))
                    e.Graphics.DrawLine(pen, 0, 0, sec.Width, 0);
            };

            // ── Chip 栏 ──
            _chipBar = new Panel { Height = H_ChipBar, Dock = DockStyle.Top, BackColor = Color.Transparent };

            _selChip = new AntdUI.Tag
            {
                Visible = false,
                Text = "📎 已选中",
                Font = new Font("Microsoft YaHei UI", 8F * UiScale.GetScaleForControl(this)),
                ForeColor = Color.FromArgb(194, 65, 12),
                BackColor = Color.FromArgb(255, 247, 237),
                BorderWidth = 1,
                Radius = 2,
                CloseIcon = true,
                AutoSize = true,
                AutoSizeMode = AntdUI.TAutoSize.Auto
            };
            _selChip.CloseChanged += (s, e) => { ClearSel(); return true; };

            _percBtn = new AntdUI.Button
            {
                Text = "文档感知 ▴",
                Size = new Size(S(110), S(30)),
                Font = new Font("Microsoft YaHei UI", 8.5F * UiScale.GetScaleForControl(this)),
                Type = AntdUI.TTypeMini.Default,
                Radius = 2,
                WaveSize = 0,
                ForeColor = CBlue,
                BackColor = Color.FromArgb(239, 246, 255),
                BorderWidth = 1
            };
            _percBtn.Click += (s, e) => ToggleTreePopup();

            _chipBar.Resize += (s, e) => LayoutChips();
            _chipBar.Paint += (s, e) =>
            {
                using (var pen = new Pen(Border))
                    e.Graphics.DrawLine(pen, 0, _chipBar.Height - 1, _chipBar.Width, _chipBar.Height - 1);
            };
            _chipBar.Controls.AddRange(new Control[] { _selChip, _percBtn });

            // ── 输入行 ──
            _inputRow = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };

            _inputBox = new AntdUI.Input
            {
                Font = new Font("Microsoft YaHei UI", 9.5F * UiScale.GetScaleForControl(this)),
                BorderWidth = 1,
                BorderColor = Color.FromArgb(209, 213, 219),
                Radius = 2,
                Multiline = true,
                PlaceholderText = "输入消息，Enter 发送",
                BackColor = Color.FromArgb(249, 250, 251),
                ForeColor = Fg1
            };

            _sendBtn = new AntdUI.Button
            {
                Text = "发送",
                Size = new Size(S(88), S(36)),
                Type = AntdUI.TTypeMini.Primary,
                Font = new Font("Microsoft YaHei UI", 9F * UiScale.GetScaleForControl(this), FontStyle.Bold),
                Radius = 2,
                WaveSize = 0
            };
            _sendBtn.Click += async (s, e) => await SendAsync();

            _inputBox.TextChanged += (s, e) => ResizeBottom();
            _inputBox.KeyDown += async (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && !e.Shift)
                {
                    e.SuppressKeyPress = true;
                    await SendAsync();
                }
            };

            _inputRow.Resize += (s, e) => LayoutInput();
            _inputRow.Controls.AddRange(new Control[] { _inputBox, _sendBtn });

            sec.Controls.Add(_inputRow); // Fill
            sec.Controls.Add(_chipBar);  // Top
            return sec;
        }

        private void LayoutChips()
        {
            int x = S(10);
            int cy = (H_ChipBar - _percBtn.Height) / 2;
            _percBtn.Location = new Point(x, cy);
            x += _percBtn.Width + S(8);
            if (_selChip.Visible)
            {
                int scy = (H_ChipBar - _selChip.Height) / 2;
                _selChip.Location = new Point(x, scy);
                x += _selChip.Width + S(6);
            }
        }

        private void LayoutInput()
        {
            int w = _inputRow.ClientSize.Width;
            int h = _inputRow.ClientSize.Height;
            int pad = S(10);
            int gap = S(8);
            int btnH = _sendBtn.Height;
            int ih = Math.Max(S(32), h - H_BottomPad - S(4));
            int btnY = Math.Max(0, S(2) + ih - btnH);

            _sendBtn.Location = new Point(w - pad - _sendBtn.Width, btnY);
            int iw = _sendBtn.Left - gap - pad;
            _inputBox.SetBounds(pad, S(2), Math.Max(S(60), iw), ih);
        }

        // ── 文档感知弹出面板 ──────────────────────────────────────────

        private Panel BuildTreePopup()
        {
            var p = new Panel { Visible = false, BackColor = Bg, BorderStyle = BorderStyle.None };
            DB(p);

            float fScale = UiScale.GetScaleForControl(this);

            // ── 顶部工具栏：状态文字 + 按钮 ──
            var toolbar = new Panel
            {
                Dock = DockStyle.Top,
                Height = S(36),
                BackColor = Color.FromArgb(249, 250, 251),
                Padding = Padding.Empty
            };
            DB(toolbar);

            _treeLbl = new Label
            {
                Text = "未感知文档",
                Font = new Font("Microsoft YaHei UI", 8.5F * fScale),
                ForeColor = Fg2,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Location = new Point(S(10), 0),
                Size = new Size(S(160), S(36))
            };

            var quickBtn = new AntdUI.Button
            {
                Text = "快速感知",
                Size = new Size(S(76), S(26)),
                Font = new Font("Microsoft YaHei UI", 8F * fScale, FontStyle.Bold),
                Type = AntdUI.TTypeMini.Default,
                Radius = 2,
                WaveSize = 0
            };
            quickBtn.Click += async (s, e) =>
            {
                _deepPerception = false;
                await ExecutePerceptionAsync(false);
            };

            var deepBtn = new AntdUI.Button
            {
                Text = "深度感知",
                Size = new Size(S(76), S(26)),
                Font = new Font("Microsoft YaHei UI", 8F * fScale, FontStyle.Bold),
                Type = AntdUI.TTypeMini.Primary,
                Radius = 2,
                WaveSize = 0
            };
            deepBtn.Click += async (s, e) =>
            {
                _deepPerception = true;
                await ExecutePerceptionAsync(true);
            };

            toolbar.Controls.AddRange(new Control[] { _treeLbl, quickBtn, deepBtn });

            // 工具栏布局：按钮靠右，标签占剩余空间
            toolbar.Resize += (s, e) =>
            {
                int bY = (toolbar.Height - deepBtn.Height) / 2;
                deepBtn.Location = new Point(toolbar.Width - deepBtn.Width - S(8), bY);
                quickBtn.Location = new Point(deepBtn.Left - quickBtn.Width - S(4), bY);
                _treeLbl.Size = new Size(Math.Max(S(60), quickBtn.Left - S(10)), toolbar.Height);
            };

            // ── 进度条 ──
            _percProgress = new AntdUI.Progress
            {
                Dock = DockStyle.Top,
                Height = S(4),
                Shape = AntdUI.TShapeProgress.Round,
                Radius = 0,
                Fill = CBlue,
                Back = Color.FromArgb(229, 231, 235),
                Loading = false,
                LoadingFull = true,
                Visible = false,
                Value = 0F
            };

            // ── 空状态占位（感知前的友好提示）──
            _treeEmpty = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "📄  点击上方按钮\n开始感知文档结构",
                Font = new Font("Microsoft YaHei UI", 9.5F * fScale),
                ForeColor = Color.FromArgb(156, 163, 175),
                BackColor = Bg,
                Visible = true
            };

            // ── 文档树 ──
            _treeView = new TreeView
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = Bg,
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = Color.FromArgb(55, 65, 81),
                ShowLines = true,
                ShowPlusMinus = true,
                ShowRootLines = false,
                FullRowSelect = true,
                ItemHeight = S(24),
                Indent = S(18),
                Visible = false
            };
            _treeView.NodeMouseClick += (s, e) => OnTreeNodeClicked(e.Node);

            // 添加顺序：Fill 区域先加，Top 区域后加（WinForms Dock 顺序）
            p.Controls.Add(_treeView);
            p.Controls.Add(_treeEmpty);
            p.Controls.Add(_percProgress);
            p.Controls.Add(toolbar);

            // 底部线条边框
            p.Paint += (s, e) =>
            {
                using (var pen = new Pen(Border))
                    e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
            };

            return p;
        }

        private void PlaceTreePopup()
        {
            int w = ClientSize.Width - S(16);
            int h = S(260);
            int y = _bottom.Top - h - S(6);
            _treePopup.SetBounds(S(8), Math.Max(H_Header + S(4), y), w, h);
        }

        private void ToggleTreePopup()
        {
            if (_treePopup.Visible)
            {
                _treePopup.Visible = false;
                UpdatePerceptionButton();
                return;
            }
            RebuildDocTree(false, _deepPerception);
            PlaceTreePopup();
            _treePopup.Visible = true;
            _treePopup.BringToFront();
            UpdatePerceptionButton();
        }

        // ═══════════════════════════════════════════════════════════════
        //  会话管理
        // ═══════════════════════════════════════════════════════════════

        private void BindSessionEvents()
        {
            _sessionList.SessionSelected += id => { LoadSession(id); ToggleSessions(false); };
            _sessionList.SessionDeleted += id =>
            {
                SessionManager.Instance.DeleteSession(id);
                if (_currentSession?.Id == id) StartNewSession();
                RefreshSessionList();
            };
            _sessionList.BackRequested += () => ToggleSessions(false);
        }

        private void StartNewSession()
        {
            if (_isRunning) return;
            SaveSession();
            _currentSession = SessionManager.Instance.CreateSession();
            _agentSession = CreateAgentSession(null);
            DebugLogger.Instance.LogSessionStart(_currentSession.Title);

            _chatPanel.ClearAll();
            _titleLabel.Text = _currentSession.Title;
            _sAll.Clear();
            lock (_sLock) _sPending.Clear();
            _greetDone = false;
            RefreshCtx();
            ToggleSessions(false);
            _ = InitPerceptionAndGreetAsync();
        }

        private async Task InitPerceptionAndGreetAsync()
        {
            // 打招呼先行：快速到达网络 await，释放 UI 线程渲染 thinking indicator
            var greetTask = GreetAsync();
            // 让 UI 线程完成一轮消息处理，确保 thinking indicator 已渲染
            await Task.Yield();
            // 感知并发执行，避免启动链路等待感知完成
            ObserveBackgroundTask(ExecutePerceptionAsync(false), "InitPerception");
            await greetTask;
        }

        private void LoadSession(string id)
        {
            if (_isRunning) return;
            SaveSession();
            var s = SessionManager.Instance.LoadSession(id);
            if (s == null) return;

            _currentSession = s;
            _agentSession = CreateAgentSession(s.AgentSessionStateJson);
            DebugLogger.Instance.LogSessionStart(_currentSession.Title);

            _chatPanel.ClearAll();
            var exportedMessages = _connect?.MainAgentInstance?.ExportSessionMessages(_agentSession)
                ?? new List<SessionMessage>();
            foreach (var m in exportedMessages)
            {
                if (string.IsNullOrWhiteSpace(m.Content)) continue;
                if (string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))
                    _chatPanel.AddMessage("用户", _userAvatar, UChatRole.User, ExtractUserInputForDisplay(m.Content));
                else if (string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                    _chatPanel.AddMessage("AI福星", _aiAvatar, UChatRole.AI, m.Content);
            }

            _titleLabel.Text = _currentSession.Title;
            _chatPanel.ScrollToEnd();
            RefreshCtx();
            RefreshSystemPrompt();
            RebuildDocTree(false, _deepPerception);
            _greetDone = true;
        }

        private void SaveSession()
        {
            if (_currentSession == null) return;
            var session = _currentSession;
            string serializedSession = null;

            try
            {
                var agent = _connect?.MainAgentInstance;
                if (agent != null && _agentSession != null)
                {
                    var element = agent.SerializeSessionAsync(
                        _agentSession,
                        null,
                        CancellationToken.None).GetAwaiter().GetResult();
                    serializedSession = element.GetRawText();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("TaskPaneHost.SaveSession.SerializeSession", ex);
            }

            Task.Run(() => SessionManager.Instance.SaveSession(session, serializedSession));
        }

        private void ToggleSessions(bool show)
        {
            _inSessionView = show;
            _sessionList.Visible = show;
            if (show)
            {
                SaveSession();
                RefreshSessionList();
                _sessionList.BringToFront();
                _sessionList.Bounds = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
            }
        }

        private void RefreshSessionList()
        {
            _sessionList.RefreshList(SessionManager.Instance.ListSessions(), _currentSession?.Id);
        }

        // ═══════════════════════════════════════════════════════════════
        //  消息发送 / 流式输出
        // ═══════════════════════════════════════════════════════════════

        private async Task SendAsync()
        {
            if (_connect == null)
                throw new InvalidOperationException("TaskPane 尚未初始化");
            if (_isRunning) { _agentCts?.Cancel(); return; }
            if (_sendGuard) return;
            _sendGuard = true;

            string text = (_inputBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(text)) { _sendGuard = false; return; }
            _inputBox.Text = "";

            string merged = text;
            if (!string.IsNullOrWhiteSpace(_selText))
                merged = "[附加选中文本]\n" + _selText + "\n\n[用户输入]\n" + text;

            _chatPanel.AddMessage("用户", _userAvatar, UChatRole.User, text);
            SaveSession();
            RefreshCtx();

            _sTarget = _chatPanel.AddMessage("AI福星", _aiAvatar, UChatRole.AI, "");
            _sTarget.ShowThinkingIndicator();
            _sAll.Clear();
            lock (_sLock) _sPending.Clear();
            _sTimer.Start();

            SubAgentBlock toolProgress = null;
            var toolQueue = new System.Collections.Generic.Queue<ToolCallCard>();
            _agentCts = new CancellationTokenSource();
            bool runningReset = false;
            SetRunning(true);

            try
            {
                var agent = _connect.MainAgentInstance
                    ?? throw new InvalidOperationException("MainAgent 未初始化");

                var outboundMessages = new List<ChatMessage>
                {
                    new ChatMessage(Microsoft.Extensions.AI.ChatRole.User, merged)
                };

                while (true)
                {
                    var pendingApprovals = new List<FunctionApprovalRequestContent>();
                    var runOptions = new FuXingRunOptions
                    {
                        InvokeOnSta = InvokeOnSta,
                        SystemPrompt = BuildPrompt()
                    };

                    int lastFlushTick = Environment.TickCount;
                    await foreach (var update in agent.RunStreamingAsync(
                        outboundMessages, _agentSession, runOptions, _agentCts.Token))
                    {
                        if (update.Text != null)
                        {
                            lock (_sLock) _sPending.Append(update.Text);
                            int now = Environment.TickCount;
                            if (now - lastFlushTick >= 40)
                            {
                                FlushStream();
                                lastFlushTick = now;
                            }
                        }

                        if (update.Contents == null) continue;

                        foreach (var content in update.Contents)
                        {
                            if (content is ToolExecutionStartContent start)
                            {
                                Ui(() =>
                                {
                                    if (string.Equals(start.ToolName, "run_sub_agent", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (toolProgress == null)
                                            toolProgress = _sTarget?.AddSubAgent("子智能体任务");
                                        toolProgress?.AddToolCallStep(start.ToolName);
                                    }
                                    else
                                    {
                                        var card = _sTarget?.AddToolCall(start.ToolName, "执行中...");
                                        if (card != null) toolQueue.Enqueue(card);
                                    }
                                });
                            }
                            else if (content is ToolExecutionEndContent end)
                            {
                                Ui(() =>
                                {
                                    toolProgress?.UpdateLastToolCallStep(end.Success);
                                    if (toolQueue.Count > 0)
                                    {
                                        var c = toolQueue.Dequeue();
                                        c.Update(end.Success ? ToolCallStatus.Success : ToolCallStatus.Error,
                                                 end.Success ? "完成" : "失败");
                                    }
                                });
                            }
                            else if (content is FunctionApprovalRequestContent approval)
                            {
                                pendingApprovals.Add(approval);
                            }
                            else if (content is AgentErrorContent error)
                            {
                                Ui(() =>
                                {
                                    if (toolProgress == null) _sTarget?.SetText("⚠ " + error.Message);
                                    else toolProgress.AddOutput(error.Message, false);
                                });
                                DebugLogger.Instance.LogError("TaskPaneHost.SendAsync.AgentErrorContent", error.Message);
                            }
                        }
                    }

                    if (pendingApprovals.Count == 0)
                        break;

                    outboundMessages = new List<ChatMessage>();
                    foreach (var approval in pendingApprovals)
                    {
                        string functionName = approval.FunctionCall?.Name ?? "unknown_function";
                        string displayName = _connect?.ToolRegistryInstance?.GetDisplayName(functionName) ?? functionName;
                        string summary = ToolRegistry.BuildApprovalSummary(approval.FunctionCall?.Arguments);

                        bool approved = await RequestApprovalOnUiAsync(displayName, functionName, summary).ConfigureAwait(false);
                        DebugLogger.Instance.LogApproval(functionName, approval.FunctionCall?.CallId ?? "N/A", approved, summary);

                        var approvalMessage = new ChatMessage { Role = Microsoft.Extensions.AI.ChatRole.User };
                        approvalMessage.Contents.Add(approval.CreateResponse(
                            approved,
                            approved ? "approved" : "rejected"));
                        outboundMessages.Add(approvalMessage);
                    }
                }

                FlushStream(true);
                if (_sAll.Length == 0 && toolProgress == null)
                {
                    Ui(() => _sTarget?.SetText("⚠ 未收到模型响应，请检查模型服务或重试"));
                    DebugLogger.Instance.LogError("TaskPaneHost.SendAsync", "流式结束但无任何文本输出");
                }
                SetRunning(false);
                runningReset = true;
                SaveSession();
                await TryGenTitle(text);
                _greetDone = true;
            }
            catch (OperationCanceledException)
            {
                DebugLogger.Instance.LogError("TaskPaneHost.SendAsync", "OperationCanceledException");
                Ui(() =>
                {
                    if (toolProgress == null) _sTarget?.SetText("已停止");
                    else toolProgress.AddOutput("已停止", false);
                });
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("TaskPaneHost.SendAsync", ex);
                Ui(() => _sTarget?.SetText("⚠ " + ex.Message));
            }
            finally
            {
                _sTimer.Stop();
                _agentCts?.Dispose();
                _agentCts = null;
                if (!runningReset) SetRunning(false);
                _sendGuard = false;
                _chatPanel.ScrollToEnd();
                RebuildDocTree(false, _deepPerception);
            }
        }

        private const string DefaultGreeting =
            "你好！我是 **AI福星**，你的 Word 智能助手。\n\n我可以帮你：\n- 📝 编辑和排版文档\n- 🔍 审查文档内容\n- 💡 回答文档相关问题\n\n有什么我可以帮你的吗？";

        private async Task GreetAsync()
        {
            if (_greetDone || _connect == null) return;
            _greetDone = true;

            var agent = _connect.MainAgentInstance;
            if (agent == null)
            {
                _chatPanel.AddMessage("AI福星", _aiAvatar, UChatRole.AI, DefaultGreeting);
                _chatPanel.ScrollToEnd();
                return;
            }

            _sTarget = _chatPanel.AddMessage("AI福星", _aiAvatar, UChatRole.AI, "");
            _sTarget.ShowThinkingIndicator();
            _sAll.Clear();
            lock (_sLock) _sPending.Clear();
            _sStripThink = true;
            _sTimer.Start();

            try
            {
                var greetMessages = new List<ChatMessage>
                {
                    new ChatMessage(Microsoft.Extensions.AI.ChatRole.User, GreetPrompt)
                };
                var greetOptions = new FuXingRunOptions
                {
                    SystemPrompt = BuildPrompt()
                };

                int lastFlushTick = Environment.TickCount;
                await foreach (var update in agent.RunStreamingAsync(
                    greetMessages, _agentSession, greetOptions, CancellationToken.None))
                {
                    if (update.Text != null)
                    {
                        lock (_sLock) _sPending.Append(update.Text);
                        int now = Environment.TickCount;
                        if (now - lastFlushTick >= 40)
                        {
                            FlushStream();
                            lastFlushTick = now;
                        }
                    }
                }

                FlushStream(true);
                string result = StripThink(_sAll.ToString());
                if (string.IsNullOrWhiteSpace(result)) result = DefaultGreeting;
                Ui(() => _sTarget?.SetText(result));
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("TaskPaneHost.GreetAsync", ex);
                Ui(() => _sTarget?.SetText(DefaultGreeting));
            }
            finally
            {
                _sTimer.Stop();
                _sStripThink = false;
                _chatPanel.ScrollToEnd();
            }
        }

        private void FlushStream(bool force = false)
        {
            if (_sTarget == null) return;
            string d;
            lock (_sLock)
            {
                if (_sPending.Length == 0 && !force) return;
                d = _sPending.ToString();
                _sPending.Clear();
            }
            Ui(() =>
            {
                if (!string.IsNullOrEmpty(d)) _sAll.Append(d);
                var display = _sStripThink ? StripThink(_sAll.ToString()) : _sAll.ToString();
                _sTarget.SetText(display);
            });
        }

        // ═══════════════════════════════════════════════════════════════
        //  辅助逻辑
        // ═══════════════════════════════════════════════════════════════

        private string BuildPrompt()
        {
            var sb = new StringBuilder();

            string soulPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".fuxing", "SOUL.md");

            string soulContent = null;
            try
            {
                if (File.Exists(soulPath))
                {
                    var modTime = File.GetLastWriteTimeUtc(soulPath);
                    if (_cachedSoulContent != null && modTime == _cachedSoulModTime)
                        soulContent = _cachedSoulContent;
                    else
                    {
                        soulContent = File.ReadAllText(soulPath, Encoding.UTF8);
                        _cachedSoulContent = soulContent;
                        _cachedSoulModTime = modTime;
                    }
                }
            }
            catch { soulContent = _cachedSoulContent; }

            if (!string.IsNullOrEmpty(soulContent))
            {
                sb.AppendLine(soulContent);
            }
            else
            {
                sb.AppendLine("你是 AI福星，一个集成在 Microsoft Word 中的智能助手。");
                sb.AppendLine("始终使用中文回答。简洁、准确、有帮助。");
            }

            // 注入文档感知结果
            var doc = _connect?.WordApplication?.ActiveDocument;
            if (doc != null)
            {
                var graph = DocumentGraphCache.Instance.GetCached(doc.FullName);
                if (graph != null)
                {
                    sb.AppendLine();
                    sb.AppendLine("## 当前文档结构");
                    sb.AppendLine(graph.ToGraphText());
                }
            }

            return sb.ToString();
        }

        private void RefreshSystemPrompt()
        {
            // 系统提示词在每次 Run 时通过 FuXingRunOptions.SystemPrompt 注入。
        }

        private async Task TryGenTitle(string first)
        {
            if (_currentSession == null || _currentSession.Title != "新对话") return;
            var agent = _connect.MainAgentInstance;
            if (agent == null) return;

            string summary = first.Length > 80 ? first.Substring(0, 80) : first;
            string t = await agent.GenerateTitleAsync(summary);
            if (string.IsNullOrWhiteSpace(t)) return;

            SessionManager.Instance.UpdateTitle(_currentSession.Id, t);
            _currentSession.Title = t;
            DebugLogger.Instance.UpdateSessionTitle(t);
            _titleLabel.Text = t;
        }

        private void SetRunning(bool on)
        {
            Ui(() =>
            {
                _isRunning = on;
                _ctxDot.ForeColor = on ? CRed : CGreen;
                _tip.SetToolTip(_ctxDot, on ? "运行中..." : CtxText());
                _sendBtn.Text = on ? "停止" : "发送";
                _sendBtn.Type = on ? AntdUI.TTypeMini.Error : AntdUI.TTypeMini.Primary;
                _inputBox.Enabled = !on;
                RefreshCtx();
            });
        }

        private void RefreshCtx()
        {
            int cur = _connect?.MainAgentInstance?.EstimateSessionTokens(_agentSession) ?? 0;
            int lim = 128000;

            if (_cachedConfig == null || (DateTime.UtcNow - _configCacheTime).TotalSeconds > 10)
            {
                _cachedConfig = _connect?.ConfigLoaderInstance?.LoadConfig();
                _configCacheTime = DateTime.UtcNow;
            }
            var cfg = _cachedConfig;
            if (cfg != null && cfg.ContextWindowLimit > 0)
            {
                lim = cfg.ContextWindowLimit;
            }

            double r = lim > 0 ? (double)cur / lim : 0;
            _ctxDot.ForeColor = _isRunning ? CRed : r > 0.8 ? CRed : r > 0.5 ? CYellow : CGreen;
            _tip.SetToolTip(_ctxDot, CtxText());
        }

        private string CtxText()
        {
            int cur = _connect?.MainAgentInstance?.EstimateSessionTokens(_agentSession) ?? 0;
            int lim = (_cachedConfig != null && _cachedConfig.ContextWindowLimit > 0)
                ? _cachedConfig.ContextWindowLimit
                : 128000;
            return $"上下文 {Fmt(cur)}/{Fmt(lim)}";
        }

        private static string Fmt(int n) => n >= 1000 ? $"{n / 1000.0:0.#}K" : n.ToString();

        // ── 选中文本监控 ──────────────────────────────────────────────

        private void StartSelectionMonitor()
        {
            _selTimer = new System.Windows.Forms.Timer { Interval = 400 };
            _selTimer.Tick += (s, e) => PollSel();
            _selTimer.Start();
        }

        private void PollSel()
        {
            if (_connect?.WordApplication == null || _isRunning) return;
            try
            {
                var sel = _connect.WordApplication.Selection;
                bool hasSelection = sel != null && sel.Start != sel.End;
                if (!hasSelection)
                {
                    if (_selSnapshot != "") _selSnapshot = "";
                    if (!string.IsNullOrEmpty(_selText)) ClearSel();
                    return;
                }

                string t = (sel?.Text ?? "").Trim();
                if (t.Length > 300) t = t.Substring(0, 300) + "...";
                if (t == _selSnapshot) return;
                _selSnapshot = t;

                if (string.IsNullOrWhiteSpace(t) || t == "\r")
                {
                    if (!string.IsNullOrEmpty(_selText)) ClearSel();
                    return;
                }

                _selText = t;
                _selChip.Text = BuildSelectionSummary(t);
                _selChip.Visible = true;
                LayoutChips();
            }
            catch { }
        }

        private static string BuildSelectionSummary(string text)
        {
            string summary = (text ?? "").Replace("\r", " ").Replace("\a", " ").Replace("\v", " ").Trim();
            if (summary.Length > 18) summary = summary.Substring(0, 18) + "...";
            return string.IsNullOrWhiteSpace(summary) ? "📎 已选中" : "📎 已选：" + summary;
        }

        private void ClearSel()
        {
            _selText = null;
            _selChip.Visible = false;
            LayoutChips();
        }

        // ── 文档感知 ──────────────────────────────────────────────────

        /// <summary>执行感知并刷新树</summary>
        private async Task ExecutePerceptionAsync(bool deep)
        {
            var doc = _connect?.WordApplication?.ActiveDocument;
            if (doc == null)
            {
                _treeLbl.Text = "未感知文档";
                _treeLbl.ForeColor = Fg2;
                UpdatePerceptionButton();
                return;
            }

            if (_isPerceiving)
            {
                _treeLbl.Text = "正在感知中…";
                _treeLbl.ForeColor = CYellow;
                UpdatePerceptionButton();
                return;
            }

            _isPerceiving = true;

            string modeLabel = deep ? "深度" : "快速";
            _treeLbl.Text = $"正在{modeLabel}感知…";
            _treeLbl.ForeColor = CYellow;
            _percProgress.Value = 0F;
            _percProgress.Loading = true;
            _percProgress.Visible = true;
            _treeEmpty.Text = $"🔍  正在{modeLabel}感知文档结构…";
            _treeEmpty.ForeColor = CYellow;
            ShowTreeEmpty(true);
            if (!_treePopup.Visible)
            {
                PlaceTreePopup();
                _treePopup.Visible = true;
                _treePopup.BringToFront();
            }
            UpdatePerceptionButton();

            var progress = new Progress<(float ratio, string message)>(p =>
                Ui(() =>
                {
                    _treeLbl.Text = p.message;
                    _treeLbl.ForeColor = CYellow;
                    _percProgress.Value = p.ratio;
                    _treeEmpty.Text = $"🔍  {p.message}";
                    if (p.ratio >= 1f) _percProgress.Loading = false;
                }));

            try
            {
                DocumentGraphCache.Instance.Invalidate(doc);
                await DocumentGraphCache.Instance.GetOrBuildAsync(
                    doc, _connect.SubAgentRunnerInstance, deep: deep,
                    progress: progress);

                _percProgress.Value = 1F;
                _percProgress.Loading = false;
                RebuildDocTree(true, deep);
            }
            catch (Exception ex)
            {
                _treeLbl.Text = $"{modeLabel}感知失败";
                _treeLbl.ForeColor = CRed;
                _percProgress.Fill = CRed;
                _percProgress.Loading = false;
                _treeEmpty.Text = $"❌  {modeLabel}感知失败\n点击上方按钮重试";
                _treeEmpty.ForeColor = CRed;
                ShowTreeEmpty(true);
                System.Diagnostics.Debug.WriteLine($"Perception error: {ex.Message}");
            }
            finally
            {
                _isPerceiving = false;
                _percProgress.Visible = false;
                _percProgress.Fill = CBlue;
                // 重置空状态占位文字和颜色
                if (_treeEmpty.Visible && _treeView.Nodes.Count == 0
                    && _treeEmpty.ForeColor != CRed)
                {
                    _treeEmpty.Text = "📄  点击上方按钮\n开始感知文档结构";
                    _treeEmpty.ForeColor = Color.FromArgb(156, 163, 175);
                }
                UpdatePerceptionButton();
                RefreshSystemPrompt();
            }
        }

        private void OnGraphCacheChanged(object sender, GraphCacheChangedEventArgs e)
        {
            Ui(() => RefreshSystemPrompt());
        }

        /// <summary>从缓存的文档图刷新树控件</summary>
        private void RebuildDocTree(bool force, bool deep)
        {
            try
            {
                var doc = _connect?.WordApplication?.ActiveDocument;
                if (doc == null)
                {
                    _treeLbl.Text = "未感知文档";
                    _treeLbl.ForeColor = Fg2;
                    ShowTreeEmpty(true);
                    UpdatePerceptionButton();
                    return;
                }

                var graph = DocumentGraphCache.Instance.GetCached(doc.FullName);

                if (graph == null)
                {
                    _treeLbl.Text = "未感知文档";
                    _treeLbl.ForeColor = Fg2;
                    ShowTreeEmpty(true);
                    UpdatePerceptionButton();
                    return;
                }

                var sig = (graph.IsDeepPerception ? "D|" : "Q|") + graph.Index.Count + "|" + graph.ContentHash;
                if (!force && sig == _percSig && _treeView.Nodes.Count > 0) return;
                _percSig = sig;

                _treeView.Nodes.Clear();
                _treeView.BeginUpdate();

                var root = new TreeNode(doc.Name ?? "当前文档");
                root.Tag = new DocTreeAnchor(0);

                if (graph.Root != null)
                {
                    foreach (var childId in graph.Root.ChildIds)
                        AddGraphNodeToTree(root.Nodes, graph, childId);
                }

                _treeView.Nodes.Add(root);
                _treeView.ExpandAll();
                _treeView.EndUpdate();

                ShowTreeEmpty(false);

                int hc = graph.FindByType(DocNodeType.Section).Count;
                int tc = graph.FindByType(DocNodeType.Table).Count;
                int ic = graph.FindByType(DocNodeType.Image).Count;

                _treeLbl.Text = $"已感知（{(graph.IsDeepPerception ? "深度" : "快速")}） 标题{hc} 表格{tc} 图片{ic}";
                _treeLbl.ForeColor = CGreen;
                _tip.SetToolTip(_percBtn, $"标题{hc} 表格{tc} 图片{ic}");
                UpdatePerceptionButton();
            }
            catch
            {
                _treeLbl.Text = "感知失败";
                _treeLbl.ForeColor = CRed;
                ShowTreeEmpty(true);
                UpdatePerceptionButton();
            }
        }

        /// <summary>切换空状态占位和树控件可见性</summary>
        private void ShowTreeEmpty(bool empty)
        {
            _treeEmpty.Visible = empty;
            _treeView.Visible = !empty;
        }

        /// <summary>递归添加文档图节点到 TreeView</summary>
        private void AddGraphNodeToTree(TreeNodeCollection parent, DocumentGraph graph, string nodeId)
        {
            var node = graph.GetById(nodeId);
            if (node == null) return;

            // 跳过 Heading 节点 — Section 已携带标题
            if (node.Type == DocNodeType.Heading) return;

            var treeNode = new TreeNode(node.Title) { Tag = GetAnchorFromNode(node) };

            switch (node.Type)
            {
                case DocNodeType.Section:
                    treeNode.ForeColor = Color.FromArgb(30, 64, 175);
                    break;
                case DocNodeType.Table:
                    treeNode.ForeColor = Color.FromArgb(180, 83, 9);
                    break;
                case DocNodeType.Image:
                    treeNode.ForeColor = Color.FromArgb(124, 58, 237);
                    break;
                case DocNodeType.List:
                    treeNode.ForeColor = Color.FromArgb(4, 120, 87);
                    break;
            }

            parent.Add(treeNode);

            if (node.Type == DocNodeType.Section || node.Type == DocNodeType.Document)
            {
                foreach (var childId in node.ChildIds)
                    AddGraphNodeToTree(treeNode.Nodes, graph, childId);
            }
        }

        private static DocTreeAnchor GetAnchorFromNode(DocNode node)
        {
            if (node.Meta != null)
            {
                if (node.Meta.TryGetValue("heading_start", out var hs))
                    return new DocTreeAnchor(int.Parse(hs));
                if (node.Meta.TryGetValue("range_start", out var rs))
                    return new DocTreeAnchor(int.Parse(rs));
            }
            return new DocTreeAnchor(0);
        }

        private void UpdatePerceptionButton()
        {
            if (_isPerceiving)
            {
                _percBtn.Text = _treePopup.Visible ? "感知中 ▾" : "感知中 ▴";
                _percBtn.Type = AntdUI.TTypeMini.Default;
                _percBtn.ForeColor = Color.FromArgb(180, 83, 9);
                _percBtn.BackColor = Color.FromArgb(255, 247, 237);
                if (_tip != null)
                    _tip.SetToolTip(_percBtn, _treePopup.Visible ? "正在感知，点击查看详情" : "正在感知，点击展开详情");
                return;
            }

            if (_treePopup.Visible)
            {
                _percBtn.Text = "文档感知 ▾";
                _percBtn.Type = AntdUI.TTypeMini.Primary;
                _percBtn.ForeColor = Color.White;
                _percBtn.BackColor = CBlue;
            }
            else
            {
                _percBtn.Text = "文档感知 ▴";
                _percBtn.Type = AntdUI.TTypeMini.Default;
                _percBtn.ForeColor = CBlue;
                _percBtn.BackColor = Color.FromArgb(239, 246, 255);
            }
            if (_tip != null)
                _tip.SetToolTip(_percBtn, _treePopup.Visible ? "收起文档感知" : "展开文档感知");
        }

        private void OnTreeNodeClicked(TreeNode node)
        {
            if (node?.Tag is not DocTreeAnchor anchor) return;
            try
            {
                var app = _connect?.WordApplication;
                var doc = app?.ActiveDocument;
                if (doc == null) throw new InvalidOperationException("没有活动文档");

                int pos = Math.Max(0, Math.Min(anchor.Start, doc.Content.End));
                var range = doc.Range(pos, pos);
                range.Select();
                app.ActiveWindow?.ScrollIntoView(range, true);
            }
            catch
            {
                _treeLbl.Text = "⚠ 跳转失败";
                _treeLbl.ForeColor = CRed;
            }
        }

        // ── 输入自适应 ────────────────────────────────────────────────

        private void ResizeBottom()
        {
            if (_bottom == null || _inputBox == null) return;
            int w = Math.Max(S(80), _inputBox.ClientSize.Width - S(12));
            int lines = Math.Max(1, TextRenderer.MeasureText(
                _inputBox.Text + "\n", _inputBox.Font,
                new Size(w, int.MaxValue), TextFormatFlags.WordBreak).Height / (_inputBox.Font.Height + S(2)));

            int inputH = H_InputBase + Math.Min(4, lines - 1) * S(18);
            inputH = Math.Max(H_InputBase, Math.Min(H_InputMax, inputH));
            int desired = H_ChipBar + inputH + H_BottomPad;

            if (Math.Abs(_bottom.Height - desired) > 2)
                _bottom.Height = desired;
        }

        // ═══════════════════════════════════════════════════════════════
        //  工具方法
        // ═══════════════════════════════════════════════════════════════

        private static string StripThink(string t)
        {
            if (string.IsNullOrEmpty(t)) return "";
            string r = Regex.Replace(t, @"<think>[\s\S]*?</think>", "", RegexOptions.IgnoreCase).Trim();
            return Regex.IsMatch(r, @"<think>", RegexOptions.IgnoreCase) ? "" : r;
        }

        private AgentSession CreateAgentSession(string serializedStateJson)
        {
            var agent = _connect?.MainAgentInstance;
            if (agent == null) return null;

            try
            {
                if (!string.IsNullOrWhiteSpace(serializedStateJson))
                {
                    using (var doc = System.Text.Json.JsonDocument.Parse(serializedStateJson))
                    {
                        return agent.DeserializeSessionAsync(
                            doc.RootElement.Clone(),
                            null,
                            CancellationToken.None).GetAwaiter().GetResult();
                    }
                }

                return agent.CreateSessionAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("TaskPaneHost.CreateAgentSession", ex);
                return null;
            }
        }

        private static string ExtractUserInputForDisplay(string storedContent)
        {
            if (string.IsNullOrWhiteSpace(storedContent)) return "";
            const string marker = "[用户输入]";
            int idx = storedContent.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0) return storedContent;
            return storedContent.Substring(idx + marker.Length).Trim();
        }

        private void Ui(Action a)
        {
            if (IsDisposed || Disposing || a == null) return;
            if (InvokeRequired) BeginInvoke((MethodInvoker)(() => a()));
            else a();
        }

        private void ObserveBackgroundTask(Task task, string name)
        {
            if (task == null) return;
            task.ContinueWith(t =>
            {
                if (t.Exception != null)
                    DebugLogger.Instance.LogError($"TaskPaneHost.{name}", t.Exception);
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>
        /// 审批回调 — 在 UI 线程上显示 ApprovalCard，异步等待用户点击结果。
        /// 供 MainAgent 的 ApprovalRequiredAIFunction 审批流程调用。
        /// </summary>
        private async Task<bool> RequestApprovalOnUiAsync(string displayName, string functionName, string summary)
        {
            ApprovalCard card = null;
            Invoke((MethodInvoker)(() =>
            {
                card = _sTarget?.AddApprovalCard(displayName, functionName, summary);
                _chatPanel.ScrollToEnd();
            }));
            if (card == null) return false;
            return await card.ResultTask.ConfigureAwait(false);
        }

        /// <summary>
        /// 将委托 marshal 到 UI 主线程（Word STA 线程）同步执行并返回结果。
        /// 在后台线程调用 Control.Invoke，阻塞后台线程直到主线程执行完毕，
        /// 不会阻塞 UI 消息泵。
        /// </summary>
        private object InvokeOnSta(Func<object> func)
        {
            if (!InvokeRequired)
                return func();
            return Invoke((Func<object>)func);
        }

        private static void DB(Panel p)
        {
            typeof(Panel).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(p, true, null);
        }

        private static Image MakeCircle(Color c)
        {
            var bmp = new Bitmap(36, 36);
            using (var g = Graphics.FromImage(bmp))
            using (var b = new SolidBrush(c))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                g.FillEllipse(b, 0, 0, 36, 36);
            }
            return bmp;
        }
    }

#pragma warning restore MEAI001
}
