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
        private AntdUI.Button _structBtn;     // "文档结构" 标签按钮
        private AntdUI.Button _graphBtn;      // "知识图谱" 标签按钮
        private Panel _inputRow;
        private AntdUI.Input _inputBox;
        private AntdUI.Button _sendBtn;

        // ═══════════════════════════════════════════════════════════════
        //  UI — 内联感知面板（输入框上方）
        // ═══════════════════════════════════════════════════════════════

        private Panel _inlinePanel;           // 内联内容容器
        private Label _inlineTitleLbl;        // 内联面板标题
        private AntdUI.Button _inlineCloseBtn;// 内联面板关闭按钮
        private TreeView _treeView;           // 文档结构树
        private Label _treeEmpty;             // 文档结构的空状态提示
        private Panel _percRow;               // 进度行容器
        private AntdUI.Progress _percProgress;
        private Label _treeLbl;               // 进度行右侧短文字
        private Panel _factPanel;             // 知识图谱内容面板
        private TreeView _factTreeView;       // 知识图谱树
        private Label _factEmpty;             // 知识图谱空状态提示
        private SessionListPanel _sessionList;
        private WorkflowBlock _activeWorkflowBlock;
        private string _activeWorkflowName;

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
        private bool _isPerceiving;
        private string _activeTab;            // "struct" | "graph" | null
        private const int B_InlinePanel = 200;
        private string _cachedSoulContent;
        private DateTime _cachedSoulModTime;
        private ConfigLoader.Config _cachedConfig;
        private DateTime _configCacheTime;

        private const string GreetPrompt =
            "对话开始，这是一条预置消息。先说说你的能力，然后问问用户想做什么。控制在100字以内。";

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
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Microsoft YaHei UI", 9F);

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

            _tip = new ToolTip();

            Resize += (s, e) =>
            {
                if (_sessionList.Visible)
                    _sessionList.Bounds = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
            };

            _bottom.SizeChanged += (s, e) => { };

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
                Font = new Font("Microsoft YaHei UI", 14F),
                Radius = 2
            };
            _newChatBtn.Click += (s, e) => StartNewSession();

            _titleLabel = new AntdUI.Label
            {
                Text = "新对话",
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                ForeColor = Fg1,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            };

            _ctxDot = new Label
            {
                Text = "●",
                Font = new Font("Microsoft YaHei UI", 9F),
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
                Font = new Font("Microsoft YaHei UI", 14F),
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

            // -- Chip 栏 --
            _chipBar = new Panel { Height = H_ChipBar, Dock = DockStyle.Top, BackColor = Color.Transparent };

            _selChip = new AntdUI.Tag
            {
                Visible = false,
                Text = "已选中",
                Font = new Font("Microsoft YaHei UI", 8F),
                ForeColor = Color.FromArgb(194, 65, 12),
                BackColor = Color.FromArgb(255, 247, 237),
                BorderWidth = 1,
                Radius = 2,
                CloseIcon = true,
                AutoSize = true,
                AutoSizeMode = AntdUI.TAutoSize.Auto
            };
            _selChip.CloseChanged += (s, e) => { ClearSel(); return true; };

            _structBtn = new AntdUI.Button
            {
                Text = "文档结构",
                Size = new Size(S(88), S(30)),
                Font = new Font("Microsoft YaHei UI", 8.5F),
                Type = AntdUI.TTypeMini.Default,
                Radius = 2,
                WaveSize = 0,
                ForeColor = CBlue,
                BackColor = Color.FromArgb(239, 246, 255),
                BorderWidth = 1
            };
            _structBtn.Click += (s, e) => ToggleInlineTab("struct");

            _graphBtn = new AntdUI.Button
            {
                Text = "知识图谱",
                Size = new Size(S(88), S(30)),
                Font = new Font("Microsoft YaHei UI", 8.5F),
                Type = AntdUI.TTypeMini.Default,
                Radius = 2,
                WaveSize = 0,
                ForeColor = CBlue,
                BackColor = Color.FromArgb(239, 246, 255),
                BorderWidth = 1
            };
            _graphBtn.Click += (s, e) => ToggleInlineTab("graph");

            _chipBar.Resize += (s, e) => LayoutChips();
            _chipBar.Paint += (s, e) =>
            {
                using (var pen = new Pen(Border))
                    e.Graphics.DrawLine(pen, 0, _chipBar.Height - 1, _chipBar.Width, _chipBar.Height - 1);
            };
            _chipBar.Controls.AddRange(new Control[] { _selChip, _structBtn, _graphBtn });

            // -- 内联感知面板 --
            _inlinePanel = BuildInlinePanel();

            // -- 输入行 --
            _inputRow = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };

            _inputBox = new AntdUI.Input
            {
                Font = new Font("Microsoft YaHei UI", 9.5F),
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
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
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
            sec.Controls.Add(_inlinePanel); // Top (below chipBar)
            sec.Controls.Add(_chipBar);  // Top (topmost)
            return sec;
        }

        private void ResetWorkflowUi()
        {
            _activeWorkflowName = null;
            _activeWorkflowBlock = null;
        }

        private void HandleWorkflowContent(AIContent content)
        {
            if (content is WorkflowExecutionStartContent started)
            {
                _activeWorkflowName = started.WorkflowName;
                _activeWorkflowBlock = _sTarget?.AddWorkflowCard(
                    started.WorkflowName,
                    started.WorkflowDisplayName,
                    started.TotalSteps);
                return;
            }

            if (content is WorkflowStepUpdateContent step
                && _activeWorkflowBlock != null
                && string.Equals(step.WorkflowName, _activeWorkflowName, StringComparison.OrdinalIgnoreCase))
            {
                _activeWorkflowBlock.UpdateStep(step.StepIndex, step.StepName, step.Description, step.IsCompleted, step.Success);
                return;
            }

            if (content is WorkflowExecutionEndContent ended
                && _activeWorkflowBlock != null
                && string.Equals(ended.WorkflowName, _activeWorkflowName, StringComparison.OrdinalIgnoreCase))
            {
                _activeWorkflowBlock.SetFinished(ended.Success, ended.Summary);
            }
        }

        private void LayoutChips()
        {
            int x = S(10);
            int cy = (H_ChipBar - _structBtn.Height) / 2;
            _structBtn.Location = new Point(x, cy);
            x += _structBtn.Width + S(4);
            _graphBtn.Location = new Point(x, cy);
            x += _graphBtn.Width + S(8);
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

        // -- 内联感知面板（输入框上方，标签页切换） --------------------

        private Panel BuildInlinePanel()
        {
            var p = new Panel
            {
                Visible = false,
                Dock = DockStyle.Top,
                Height = S(B_InlinePanel),
                BackColor = Bg,
                BorderStyle = BorderStyle.None
            };
            DB(p);

            var smallFont = new Font("Microsoft YaHei UI", 8F);
            var bodyFont = new Font("Microsoft YaHei UI", 9F);

            // -- 标题栏：标签名 + 关闭按钮 --
            var headerRow = new Panel
            {
                Dock = DockStyle.Top,
                Height = S(28),
                BackColor = Color.FromArgb(249, 250, 251)
            };
            DB(headerRow);

            _inlineTitleLbl = new Label
            {
                Text = "文档结构",
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold),
                ForeColor = Fg1,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill
            };
            _inlineTitleLbl.Padding = new Padding(S(8), 0, 0, 0);

            _inlineCloseBtn = new AntdUI.Button
            {
                Text = "收起",
                Size = new Size(S(48), S(22)),
                Font = smallFont,
                Type = AntdUI.TTypeMini.Default,
                Radius = 2,
                WaveSize = 0,
                ForeColor = Fg2,
                BackColor = Color.Transparent,
                BorderWidth = 0,
                Dock = DockStyle.Right
            };
            _inlineCloseBtn.Click += (s, e) =>
            {
                _activeTab = null;
                _inlinePanel.Visible = false;
                UpdateTabButtons();
                ResizeBottom();
            };

            headerRow.Controls.Add(_inlineTitleLbl);
            headerRow.Controls.Add(_inlineCloseBtn);

            // -- 进度行：进度条 + 右侧短状态文字 --
            _percRow = new Panel
            {
                Dock = DockStyle.Top,
                Height = S(22),
                BackColor = Color.FromArgb(249, 250, 251),
                Visible = false,
                Padding = new Padding(S(8), S(2), S(8), S(2))
            };
            DB(_percRow);

            _percProgress = new AntdUI.Progress
            {
                Dock = DockStyle.Fill,
                Shape = AntdUI.TShapeProgress.Round,
                Radius = S(2),
                Fill = CBlue,
                Back = Color.FromArgb(229, 231, 235),
                Loading = false,
                LoadingFull = true,
                Value = 0F,
                UseSystemText = false
            };

            _treeLbl = new Label
            {
                Dock = DockStyle.Right,
                AutoSize = false,
                Width = S(80),
                Font = smallFont,
                ForeColor = Fg2,
                TextAlign = ContentAlignment.MiddleRight,
                Text = ""
            };

            _percRow.Controls.Add(_percProgress);
            _percRow.Controls.Add(_treeLbl);

            // -- 文档结构：空状态提示 --
            _treeEmpty = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "正在解析文档结构...",
                Font = bodyFont,
                ForeColor = Color.FromArgb(156, 163, 175),
                BackColor = Bg,
                Visible = true
            };

            // -- 文档结构：树控件 --
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

            // -- 知识图谱：空状态提示 --
            _factEmpty = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "使用 Ribbon \"文档感知\" 按钮提取文档事实",
                Font = bodyFont,
                ForeColor = Color.FromArgb(156, 163, 175),
                BackColor = Bg,
                Visible = false
            };

            // -- 知识图谱：事实树 --
            _factTreeView = new TreeView
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

            // Dock 添加顺序：Fill 先加，Top 后加
            p.Controls.Add(_treeView);
            p.Controls.Add(_treeEmpty);
            p.Controls.Add(_factTreeView);
            p.Controls.Add(_factEmpty);
            p.Controls.Add(_percRow);
            p.Controls.Add(headerRow);

            p.Paint += (s, e) =>
            {
                using (var pen = new Pen(Border))
                {
                    e.Graphics.DrawLine(pen, 0, 0, p.Width, 0);
                    e.Graphics.DrawLine(pen, 0, p.Height - 1, p.Width, p.Height - 1);
                }
            };

            return p;
        }

        /// <summary>切换内联标签页（struct=文档结构, graph=知识图谱）</summary>
        private void ToggleInlineTab(string tab)
        {
            if (_activeTab == tab)
            {
                // 再次点击相同标签 -> 收起
                _activeTab = null;
                _inlinePanel.Visible = false;
                UpdateTabButtons();
                ResizeBottom();
                return;
            }

            _activeTab = tab;
            _inlinePanel.Visible = true;
            _inlineTitleLbl.Text = tab == "struct" ? "文档结构" : "知识图谱";

            bool isStruct = tab == "struct";
            // 文档结构控件
            _treeView.Visible = isStruct && _treeView.Nodes.Count > 0;
            _treeEmpty.Visible = isStruct && _treeView.Nodes.Count == 0;
            // 知识图谱控件
            _factTreeView.Visible = !isStruct && _factTreeView.Nodes.Count > 0;
            _factEmpty.Visible = !isStruct && _factTreeView.Nodes.Count == 0;

            if (isStruct)
            {
                RebuildDocTree(false);
            }
            else
            {
                RebuildFactTree();
            }

            UpdateTabButtons();
            ResizeBottom();
        }

        /// <summary>更新标签按钮样式（选中/未选中）</summary>
        private void UpdateTabButtons()
        {
            bool structActive = _activeTab == "struct";
            bool graphActive = _activeTab == "graph";

            _structBtn.Type = structActive ? AntdUI.TTypeMini.Primary : AntdUI.TTypeMini.Default;
            _structBtn.ForeColor = structActive ? Color.White : CBlue;
            _structBtn.BackColor = structActive ? CBlue : Color.FromArgb(239, 246, 255);

            _graphBtn.Type = graphActive ? AntdUI.TTypeMini.Primary : AntdUI.TTypeMini.Default;
            _graphBtn.ForeColor = graphActive ? Color.White : CBlue;
            _graphBtn.BackColor = graphActive ? CBlue : Color.FromArgb(239, 246, 255);
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
            ResetWorkflowUi();
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
            RebuildDocTree(false);
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
            ResetWorkflowUi();
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
                        InnerChatOptions = new Microsoft.Extensions.AI.ChatOptions
                        {
                            Instructions = BuildPrompt()
                        },
                        InvokeOnSta = InvokeOnSta,
                        RequestUserInputAsync = RequestUserInputOnUiAsync
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
                            else if (content is WorkflowExecutionStartContent
                                  || content is WorkflowStepUpdateContent
                                  || content is WorkflowExecutionEndContent)
                            {
                                Ui(() =>
                                {
                                    // 工作流自带详细卡片，移除之前为同一工具创建的 ToolCallCard
                                    if (content is WorkflowExecutionStartContent && toolQueue.Count > 0)
                                    {
                                        var redundant = toolQueue.Dequeue();
                                        _sTarget?.RemoveBlock(redundant);
                                    }
                                    HandleWorkflowContent(content);
                                });
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
                RebuildDocTree(false);
                RebuildFactTree();
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
                    InnerChatOptions = new Microsoft.Extensions.AI.ChatOptions
                    {
                        Instructions = BuildPrompt()
                    }
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
                // 流式阶段：strip 后为空说明仅有 think 内容，保持 thinking 指示器
                if (string.IsNullOrEmpty(display) && !force) return;
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
            // 系统提示词在每次 Run 时通过 FuXingRunOptions ChatOptions.Instructions 注入。
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

        // -- 文档结构感知 ------------------------------------------------

        /// <summary>执行文档结构感知并刷新树</summary>
        private async Task ExecutePerceptionAsync(bool deep)
        {
            var doc = _connect?.WordApplication?.ActiveDocument;
            if (doc == null)
            {
                _treeEmpty.Text = "未检测到活动文档";
                _treeEmpty.ForeColor = Color.FromArgb(156, 163, 175);
                ShowTreeEmpty(true);
                return;
            }

            if (_isPerceiving) return;
            _isPerceiving = true;

            _treeLbl.Text = "解析中";
            _treeLbl.ForeColor = CYellow;
            _percProgress.Value = 0F;
            _percProgress.Loading = true;
            _percRow.Visible = true;
            _treeEmpty.Text = "正在解析文档结构...";
            _treeEmpty.ForeColor = Color.FromArgb(107, 114, 128);
            ShowTreeEmpty(true);

            // 自动展开文档结构标签页
            if (_activeTab != "struct")
            {
                _activeTab = "struct";
                _inlineTitleLbl.Text = "文档结构";
                _inlinePanel.Visible = true;
                UpdateTabButtons();
                ResizeBottom();
            }

            var progress = new Progress<(float ratio, string message)>(p =>
                Ui(() =>
                {
                    int pct = (int)(p.ratio * 100);
                    _treeLbl.Text = $"{pct}%";
                    _treeLbl.ForeColor = Fg2;
                    _percProgress.Value = p.ratio;
                    _treeEmpty.Text = p.message;
                    _treeEmpty.ForeColor = Color.FromArgb(107, 114, 128);
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
                RebuildDocTree(true);
            }
            catch (Exception ex)
            {
                _percProgress.Fill = CRed;
                _percProgress.Loading = false;
                _treeLbl.Text = "失败";
                _treeLbl.ForeColor = CRed;
                _treeEmpty.Text = "文档结构解析失败";
                _treeEmpty.ForeColor = CRed;
                ShowTreeEmpty(true);
                System.Diagnostics.Debug.WriteLine($"Perception error: {ex.Message}");
            }
            finally
            {
                _isPerceiving = false;
                _percRow.Visible = false;
                _percProgress.Fill = CBlue;
                if (_treeEmpty.Visible && _treeView.Nodes.Count == 0
                    && _treeEmpty.ForeColor != CRed)
                {
                    _treeEmpty.Text = "正在解析文档结构...";
                    _treeEmpty.ForeColor = Color.FromArgb(156, 163, 175);
                }
                RefreshSystemPrompt();
            }
        }

        private void OnGraphCacheChanged(object sender, GraphCacheChangedEventArgs e)
        {
            Ui(() => RefreshSystemPrompt());
        }

        /// <summary>从缓存的文档图刷新树控件</summary>
        private void RebuildDocTree(bool force)
        {
            try
            {
                var doc = _connect?.WordApplication?.ActiveDocument;
                if (doc == null)
                {
                    _treeEmpty.Text = "未检测到文档";
                    _treeEmpty.ForeColor = Color.FromArgb(156, 163, 175);
                    ShowTreeEmpty(true);
                    return;
                }

                var graph = DocumentGraphCache.Instance.GetCached(doc.FullName);

                if (graph == null)
                {
                    _treeEmpty.Text = "正在解析文档结构...";
                    _treeEmpty.ForeColor = Color.FromArgb(156, 163, 175);
                    ShowTreeEmpty(true);
                    return;
                }

                var sig = graph.Index.Count + "|" + graph.ContentHash;
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

                _tip.SetToolTip(_structBtn,
                    $"标题{hc} 表格{tc} 图片{ic}");
            }
            catch
            {
                _treeEmpty.Text = "解析失败，请重试";
                _treeEmpty.ForeColor = CRed;
                ShowTreeEmpty(true);
            }
        }

        /// <summary>切换文档结构空状态占位和树控件可见性</summary>
        private void ShowTreeEmpty(bool empty)
        {
            bool isStructTab = _activeTab == "struct";
            _treeEmpty.Visible = empty && isStructTab;
            _treeView.Visible = !empty && isStructTab;
        }

        /// <summary>从事实缓存刷新知识图谱树</summary>
        private void RebuildFactTree()
        {
            _factTreeView.Nodes.Clear();

            var doc = _connect?.WordApplication?.ActiveDocument;
            if (doc == null)
            {
                ShowFactEmpty(true, "未检测到文档");
                return;
            }

            var snapshot = DocumentFactCache.Instance.GetFreshSnapshot(
                doc.FullName, doc.Content.Text.GetHashCode(), "all", 0);
            if (snapshot == null || snapshot.Facts.Count == 0)
            {
                ShowFactEmpty(true, "使用 Ribbon \"文档感知\" 按钮提取文档事实");
                return;
            }

            // 按 Type 分组展示
            var groups = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);
            var typeLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "data", "数据" },
                { "event", "事件" },
                { "activity", "活动" },
                { "metric", "技术指标" }
            };

            _factTreeView.BeginUpdate();
            foreach (var fact in snapshot.Facts)
            {
                string typeKey = (fact.Type ?? "other").ToLowerInvariant();
                if (!groups.TryGetValue(typeKey, out var groupNode))
                {
                    string label = typeLabels.TryGetValue(typeKey, out var l) ? l : typeKey;
                    groupNode = new TreeNode(label) { ForeColor = Color.FromArgb(30, 64, 175) };
                    groups[typeKey] = groupNode;
                    _factTreeView.Nodes.Add(groupNode);
                }
                var factNode = new TreeNode(fact.Summary ?? fact.Value ?? "(无摘要)")
                {
                    ForeColor = Color.FromArgb(55, 65, 81),
                    ToolTipText = fact.Evidence
                };
                groupNode.Nodes.Add(factNode);
            }
            _factTreeView.ExpandAll();
            _factTreeView.EndUpdate();

            ShowFactEmpty(false, null);
        }

        /// <summary>切换知识图谱空状态占位和树控件可见性</summary>
        private void ShowFactEmpty(bool empty, string message)
        {
            bool isGraphTab = _activeTab == "graph";
            if (message != null)
            {
                _factEmpty.Text = message;
                _factEmpty.ForeColor = Color.FromArgb(156, 163, 175);
            }
            _factEmpty.Visible = empty && isGraphTab;
            _factTreeView.Visible = !empty && isGraphTab;
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
                _treeLbl.Text = "跳转失败";
                _treeLbl.ForeColor = CRed;
            }
        }

        // -- 输入自适应 ------------------------------------------------

        private void ResizeBottom()
        {
            if (_bottom == null || _inputBox == null) return;
            int w = Math.Max(S(80), _inputBox.ClientSize.Width - S(12));
            int lines = Math.Max(1, TextRenderer.MeasureText(
                _inputBox.Text + "\n", _inputBox.Font,
                new Size(w, int.MaxValue), TextFormatFlags.WordBreak).Height / (_inputBox.Font.Height + S(2)));

            int inputH = H_InputBase + Math.Min(4, lines - 1) * S(18);
            inputH = Math.Max(H_InputBase, Math.Min(H_InputMax, inputH));
            int inlineH = (_inlinePanel != null && _inlinePanel.Visible) ? S(B_InlinePanel) : 0;
            int desired = H_ChipBar + inlineH + inputH + H_BottomPad;

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

        private async Task<string> RequestUserInputOnUiAsync(string question, List<FuXingAgent.Tools.AskUserOption> options, bool allowFreeInput)
        {
            AskUserCard card = null;

            void BuildCard()
            {
                var uiOptions = new List<UI.AskUserOption>();
                if (options != null)
                {
                    foreach (var opt in options)
                    {
                        if (opt == null || string.IsNullOrWhiteSpace(opt.label)) continue;
                        uiOptions.Add(new UI.AskUserOption
                        {
                            Label = opt.label,
                            Description = opt.description
                        });
                    }
                }

                card = _sTarget?.AddAskUserCard(question ?? "", uiOptions, allowFreeInput);
                _chatPanel.ScrollToEnd();
            }

            if (InvokeRequired)
                Invoke((MethodInvoker)BuildCard);
            else
                BuildCard();

            if (card == null) return string.Empty;
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
