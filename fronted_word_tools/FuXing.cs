using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using System.Text;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Threading.Tasks;
using FuXing.UI;
using NetOffice;
using NetOffice.Tools;
using NetOffice.WordApi;
using NetOffice.WordApi.Tools;
using NetOffice.OfficeApi.Enums;


namespace FuXing
{
    [ComVisible(true)]
    [Guid("C9F68F90-E8C4-4A8B-9A8B-5E6F7D8E9F0A")]
    [ProgId("FuXing.Connect")]
    [RegistryLocation(RegistrySaveLocation.CurrentUser), CustomPane(typeof(TaskPaneControl), "AI福星", false, PaneDockPosition.msoCTPDockPositionRight)]
	
    // ═══════════════════════════════════════════════════════════════
    //  每个 Word 窗口的 TaskPane 上下文
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 每个 Word 窗口拥有独立的 TaskPane、控件实例和聊天记忆。
    /// </summary>
    internal sealed class WindowPaneContext
    {
        public NetOffice.OfficeApi._CustomTaskPane CTP { get; }
        public TaskPaneControl Control { get; }
        public ChatMemory Memory { get; }

        public bool Visible
        {
            get => CTP.Visible;
            set => CTP.Visible = value;
        }

        public int Width
        {
            get => CTP.Width;
            set => CTP.Width = value;
        }

        public WindowPaneContext(NetOffice.OfficeApi._CustomTaskPane ctp, TaskPaneControl control)
        {
            CTP = ctp;
            Control = control;
            Memory = control.Memory;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  主插件入口
    // ═══════════════════════════════════════════════════════════════

    public class Connect : NetOffice.WordApi.Tools.COMAddin
    {
        private NetOffice.WordApi.Application _wordApplication;
        private NetWorkHelper _networkHelper;
        private string _savedUserName;
        private string _savedUserInitials;
        private ConfigLoader _configLoader;

        // 静态实例引用，供TaskPane控件调用
        public static Connect CurrentInstance { get; private set; }

        /// <summary>缓存的 CTP 工厂，供后续为新窗口创建 TaskPane</summary>
        private NetOffice.OfficeApi.ICTPFactory _ctpFactory;

        /// <summary>窗口句柄 → WindowPaneContext 映射，每个窗口独立的 TaskPane + ChatMemory</summary>
        private readonly Dictionary<int, WindowPaneContext> _windowPanes
            = new Dictionary<int, WindowPaneContext>();

        // RibbonUI 属性
        internal new NetOffice.OfficeApi.IRibbonUI RibbonUI { get; private set; }

        /// <summary>当前纠错模式</summary>
        public CorrectionMode CurrentCorrectionMode => _correctionMode;
        private CorrectionMode _correctionMode = CorrectionMode.Typo;

        /// <summary>
        /// 会话记忆管理器 —— 动态返回当前活动窗口的 ChatMemory。
        /// 若当前无活动窗口，返回一个后备实例（避免 NullRef）。
        /// </summary>
        public ChatMemory Memory
        {
            get
            {
                var ctx = GetActiveWindowContext();
                if (ctx == null) 
                    throw new InvalidOperationException("没有活动的窗口上下文，无法获取 ChatMemory");
                return ctx.Memory;
            }
        }

        /// <summary>工具注册表（定义大模型可调用的插件功能）</summary>
        public ToolRegistry ToolRegistry { get; } = new ToolRegistry();

        /// <summary>Skill 管理器（发现、加载、激活技能）</summary>
        public SkillManager SkillManager { get; } = new SkillManager();

        /// <summary>Word 应用程序引用（供 ToolRegistry 调用）</summary>
        public NetOffice.WordApi.Application WordApplication => _wordApplication;

        /// <summary>
        /// 用户发送消息时的光标快照。
        /// 工具执行期间用户可能移动光标，因此所有需要"当前光标位置"的工具
        /// 应读取此快照而非实时 Selection。
        /// 由 TaskPaneControl 在发送消息时设置，助手回合结束后清除。
        /// </summary>
        public CursorSnapshot SelectionSnapshot { get; set; }

        public Connect()
        {
            CurrentInstance = this;
            // 初始化 AntdUI Emoji 资源（Microsoft Fluent Flat 风格 SVG emoji）
            AntdUI.SvgDb.Emoji = AntdUI.FluentFlat.Emoji;
            OnStartupComplete += Connect_OnStartupComplete;
            OnDisconnection += Connect_OnDisconnection;
        }

        // ── CTP 工厂缓存（必须在 base 完成后保存，供新窗口动态创建 TaskPane） ──

        public override void CTPFactoryAvailable(object CTPFactoryInst)
        {
            base.CTPFactoryAvailable(CTPFactoryInst);
            _ctpFactory = new NetOffice.OfficeApi.ICTPFactory(Factory, null, CTPFactoryInst);
            System.Diagnostics.Debug.WriteLine("[CTP] ICTPFactory 已缓存，可为新窗口创建 TaskPane");
        }

        private void Connect_OnStartupComplete(ref Array custom)
        {
            try
            {
                _wordApplication = (NetOffice.WordApi.Application)Application;
                _networkHelper = new NetWorkHelper();
                _configLoader = new ConfigLoader();

                // 根据配置启用调试日志
                var startupConfig = _configLoader.LoadConfig();
                DebugLogger.Instance.Enabled = startupConfig.DeveloperMode;
                DebugLogger.Instance.LogInfo("插件启动，DeveloperMode=" + startupConfig.DeveloperMode);

                // 确保资源文件复制到输出目录
                ResourceManager.CopyResourcesToOutput();

                // 测试：输出资源状态
                System.Diagnostics.Debug.WriteLine("=== 福星 资源状态检查 ===");
                System.Diagnostics.Debug.WriteLine(ResourceManager.GetResourceStatus());
                System.Diagnostics.Debug.WriteLine("================================");

                System.Diagnostics.Debug.WriteLine($"Word加载插件版本 {Application.Version}");
                System.Diagnostics.Debug.WriteLine($"TaskPanes 数量: {TaskPanes.Count}");

                // [CustomPane] 属性创建的第一个 TaskPane 绑定到当前活动窗口
                if (TaskPanes.Count > 0 && TaskPanes[0].IsLoaded)
                {
                    try
                    {
                        var activeWin = _wordApplication.ActiveWindow;
                        if (activeWin != null)
                        {
                            int hwnd = GetWindowHwnd(activeWin);
                            var ctp = TaskPanes[0].Pane;
                            var control = TaskPaneInstances.Count > 0
                                ? (TaskPaneControl)TaskPaneInstances[0]
                                : null;
                            if (ctp != null && control != null && hwnd != 0)
                            {
                                _windowPanes[hwnd] = new WindowPaneContext(ctp, control);
                                System.Diagnostics.Debug.WriteLine(
                                    $"[TaskPane] 初始窗口 0x{hwnd:X} 已关联 TaskPane (CTP)");
                            }
                        }
                    }
                    catch
                    {
                        // Word 启动时可能尚无活动窗口，ActiveWindow 访问会抛异常，忽略即可
                        System.Diagnostics.Debug.WriteLine("[TaskPane] 初始化时无活动窗口，跳过绑定");
                    }
                }

                CreateUserInterface();
            }
            catch (Exception ex)
            {
                MessageBox.Show("初始化时错误: " + ex.Message, "福星错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Connect_OnDisconnection(ext_DisconnectMode RemoveMode, ref Array custom)
        {
            try
            {
                RemoveUserInterface();

                // 释放所有窗口的 TaskPane 资源
                foreach (var ctx in _windowPanes.Values)
                {
                    try { ctx.CTP.Dispose(); } catch { }
                    try { ctx.Control.Dispose(); } catch { }
                }
                _windowPanes.Clear();

                if (null != _wordApplication)
                    _wordApplication.Dispose();

                CurrentInstance = null;
            }
            catch (Exception ex)
            {
                MessageBox.Show("卸载插件时错误: " + ex.Message, "福星错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CreateUserInterface()
        {
            // 使用CustomPane特性时，任务窗格会自动创建
            // 这里可以添加其他UI初始化代码
        }

        private void RemoveUserInterface()
        {
            // 清理用户界面资源
        }

        public override string GetCustomUI(string RibbonID)
        {
            return GetRibbonXml();
        }

        private string GetRibbonXml()
        {
            try
            {
                string assemblyPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string ribbonPath = Path.Combine(assemblyPath, "RibbonUI.xml");
                
                if (File.Exists(ribbonPath))
                {
                    return File.ReadAllText(ribbonPath);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("RibbonUI.xml文件不存在，请确保文件已正确部署到输出目录");
                    throw new FileNotFoundException("RibbonUI.xml文件未找到", ribbonPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"读取Ribbon XML失败: {ex.Message}");
                throw;
            }
        }

        // 根据控件ID从 Resources 文件中加载图标
        public object GetButtonImage(NetOffice.OfficeApi.IRibbonControl control)
        {
            try
            {
                if (control == null || string.IsNullOrEmpty(control.Id)) 
                {
                    System.Diagnostics.Debug.WriteLine("[GetButtonImage] 控件或控件ID为空");
                    return null;
                }
                
                var iconName = GetIconNameForControl(control.Id);
                if (string.IsNullOrEmpty(iconName)) 
                {
                    System.Diagnostics.Debug.WriteLine($"[GetButtonImage] 未找到控件 {control.Id} 对应的图标");
                    return null;
                }

                System.Diagnostics.Debug.WriteLine($"[GetButtonImage] 为控件 {control.Id} 加载图标: {iconName}");
                
                // 使用改进的ResourceManager加载
                return ResourceManager.GetIconAsPictureDisp(iconName);
            }
            catch (Exception ex)
            {
                // 记录错误但不中断
                System.Diagnostics.Debug.WriteLine($"[GetButtonImage] 错误: {ex.Message}");
                return null;
            }
        }

        private string GetIconNameForControl(string controlId)
        {
            switch (controlId)
            {
                case "HelloWorldButton": return "icons8-better-150.png";
                case "icon_test_btn": return "icons8-sparkling-100.png"; // 使用sparkling图标作为测试图标
                case "LearnFormatButton": return "icons8-learn_styles.png"; // 学习格式功能图标
                case "LoadDefaultStylesButton": return "icons8-load_default_styles-all.png"; // 载入默认样式库图标
                case "CheckStandardValidityButton": return "icons8-deepseek-150.png";
                case "ai_text_correction_btn": return "icons8-spellcheck-70.png";
                case "toggle_taskpane_btn": return "logo.png"; // AI福星图标
                case "setting_btn": return "icons8-setting-128.png";
                case "about_btn": return "icons8-clean-96.png";
                default: 
                    System.Diagnostics.Debug.WriteLine($"[GetIconNameForControl] 未知控件ID: {controlId}");
                    return null;
            }
        }

        #region Ribbon 按钮事件

        /// <summary>
        /// 学习当前光标或选中文本所在段落的格式并保存为新样式
        /// </summary>
        /// <param name="control"></param>
        public void LearnFormatButton_Click(NetOffice.OfficeApi.IRibbonControl control)
        {
            try
            {
                _wordApplication.StatusBar = "正在学习段落格式...";
                
                var selection = _wordApplication.Selection;
                if (selection == null)
                {
                    MessageBox.Show("无法获取当前选择", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // 获取当前光标或选中文本所在的完整段落
                NetOffice.WordApi.Paragraph targetParagraph;
                NetOffice.WordApi.Range paragraphRange;
                
                if (selection.Type == NetOffice.WordApi.Enums.WdSelectionType.wdSelectionIP)
                {
                    // 光标位置，获取光标所在段落
                    targetParagraph = selection.Paragraphs[1];
                    paragraphRange = targetParagraph.Range;
                }
                else if (selection.Paragraphs.Count > 0)
                {
                    // 有选中文本，确定要分析的目标段落
                    NetOffice.WordApi.Paragraph selectedParagraph = null;
                    
                    // 如果选中了多个段落，询问用户要使用哪个段落的格式
                    if (selection.Paragraphs.Count > 1)
                    {
                        var result = MessageBox.Show(
                            $"检测到选中了 {selection.Paragraphs.Count} 个段落。\n\n是否使用第一个段落的格式？\n\n点击「是」使用第一个段落，点击「否」使用光标所在段落。",
                            "选择段落格式",
                            MessageBoxButtons.YesNoCancel,
                            MessageBoxIcon.Question);
                            
                        if (result == DialogResult.Cancel)
                        {
                            _wordApplication.StatusBar = "操作已取消";
                            return;
                        }
                        else if (result == DialogResult.No)
                        {
                            // 使用光标所在的段落
                            var cursorPos = selection.Start;
                            foreach (NetOffice.WordApi.Paragraph para in selection.Paragraphs)
                            {
                                if (cursorPos >= para.Range.Start && cursorPos <= para.Range.End)
                                {
                                    selectedParagraph = para;
                                    break;
                                }
                            }
                        }
                        else
                        {
                            // 使用第一个段落
                            selectedParagraph = selection.Paragraphs[1];
                        }
                    }
                    else
                    {
                        // 只有一个段落被选中
                        selectedParagraph = selection.Paragraphs[1];
                    }
                    
                    // 确保我们有有效的段落
                    if (selectedParagraph != null)
                    {
                        targetParagraph = selectedParagraph;
                        
                        // 检查是否是部分选择段落
                        var isPartialSelection = selection.Start > targetParagraph.Range.Start || 
                                               selection.End < targetParagraph.Range.End;
                        
                        if (isPartialSelection)
                        {
                            // 询问用户是否要扩展到完整段落
                            var expandResult = MessageBox.Show(
                                "检测到您只选择了段落的一部分。\n\n是否要分析整个段落的格式？\n\n点击「是」分析完整段落，点击「否」仅分析选中部分。",
                                "段落格式分析范围",
                                MessageBoxButtons.YesNoCancel,
                                MessageBoxIcon.Question);
                                
                            if (expandResult == DialogResult.Cancel)
                            {
                                _wordApplication.StatusBar = "操作已取消";
                                return;
                            }
                            else if (expandResult == DialogResult.Yes)
                            {
                                // 扩展到完整段落
                                paragraphRange = targetParagraph.Range;
                                _wordApplication.StatusBar = "已扩展到完整段落进行格式分析";
                            }
                            else
                            {
                                // 仅使用选中部分
                                paragraphRange = selection.Range;
                                _wordApplication.StatusBar = "分析选中部分的格式";
                            }
                        }
                        else
                        {
                            // 完整段落选择
                            paragraphRange = targetParagraph.Range;
                        }
                    }
                    else
                    {
                        MessageBox.Show("无法确定要分析的段落", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }
                else
                {
                    MessageBox.Show("无法确定要学习格式的段落", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // 创建格式信息字符串
                var formatInfo = ExtractParagraphFormat(targetParagraph, paragraphRange);
                
                // 显示段落预览信息
                var paragraphText = paragraphRange.Text?.Trim();
                if (!string.IsNullOrEmpty(paragraphText))
                {
                    // 限制预览文本长度
                    if (paragraphText.Length > 100)
                    {
                        paragraphText = paragraphText.Substring(0, 100) + "...";
                    }
                    formatInfo = $"段落预览: {paragraphText}\n\n{formatInfo}";
                }
                
                // 显示样式命名对话框
                ShowStyleNamingDialog(formatInfo);
                
                _wordApplication.StatusBar = "格式学习完成";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LearnFormatButton_Click error: {ex.Message}");
                MessageBox.Show($"学习格式失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _wordApplication.StatusBar = "格式学习失败";
            }
        }

        /// <summary>
        /// 载入默认AI样式库
        /// </summary>
        /// <param name="control"></param>
        public void LoadDefaultStylesButton_Click(NetOffice.OfficeApi.IRibbonControl control)
        {
            try
            {
                _wordApplication.StatusBar = "正在载入默认样式库...";
                
                LoadDefaultAIStyles();
                
                MessageBox.Show("默认样式库载入成功！\n包含：AI正文、AI一级~六级标题、AI图片、AI表格样式", 
                    "样式库载入完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                
                _wordApplication.StatusBar = "默认样式库载入完成";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadDefaultStylesButton_Click error: {ex.Message}");
                MessageBox.Show($"载入样式库失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _wordApplication.StatusBar = "样式库载入失败";
            }
        }

        public void CheckStandardValidityButton_Click(NetOffice.OfficeApi.IRibbonControl control)
        {
            try
            {
                var selection = _wordApplication.Selection;
                string selectedText = selection?.Text?.Trim();
                if (string.IsNullOrEmpty(selectedText))
                {
                    MessageBox.Show("请先选择要校验的文本", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // 打开 AI 面板并注入校验指令
                var paneControl = EnsureTaskPaneVisible();
                string message = $"[用户选中了以下文本，请校验其中引用的标准是否有效]\n\n{selectedText}";
                paneControl.InjectUserMessage(message);
            }
            catch (Exception ex)
            {
                MessageBox.Show("校验引用标准失败: " + ex.Message, "福星错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public async void ai_text_correction_btn_Click(NetOffice.OfficeApi.IRibbonControl control)
        {
            try
            {
                var selection = _wordApplication.Selection;
                bool hasSelection = selection != null &&
                                    selection.Start < selection.End &&
                                    !string.IsNullOrEmpty(selection.Text?.Trim());

                if (hasSelection)
                {
                    await CorrectSelectedTextAsync();
                }
                else
                {
                    if (_wordApplication.Documents.Count == 0)
                    {
                        MessageBox.Show("没有打开的文档", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    var confirm = MessageBox.Show(
                        "未选中文本，是否对全文进行纠错？\n\n注意：全文纠错可能需要较长时间。",
                        "全文纠错确认",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (confirm == DialogResult.Yes)
                        await CorrectAllTextAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("AI文本纠错时错误: " + ex.Message, "福星错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                _wordApplication.StatusBar = "纠错失败！";
            }
        }

        private static string GetCorrectionModeName(CorrectionMode mode)
        {
            switch (mode)
            {
                case CorrectionMode.Typo: return "错别字";
                case CorrectionMode.Semantic: return "语义";
                case CorrectionMode.Consistency: return "一致性";
                default: throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }

        private async System.Threading.Tasks.Task CorrectSelectedTextAsync()
        {
            var selection = _wordApplication.Selection;
            string originalText = selection.Text;
            int selStart = selection.Start;
            int selEnd = selection.End;

            var chat = EnsureTaskPaneVisible();
            if (chat == null)
            {
                MessageBox.Show("无法打开AI福星面板", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string modeName = GetCorrectionModeName(_correctionMode);
            var toolCard = chat.AddToolCallMessage($"选中文本纠错（{modeName}）", "正在初始化...");

            var activeDoc = _wordApplication.ActiveDocument;
            HighlightRange(activeDoc, selStart, selEnd);

            try
            {
                var service = TextCorrectionService.FromConfig();

                chat.UpdateToolCall(UI.ToolCallStatus.Running, "AI 正在分析选中文本...");
                _wordApplication.StatusBar = "AI 正在分析选中文本...";

                var result = await service.CorrectTextAsync(originalText, _correctionMode, msg =>
                {
                    chat.UpdateToolCall(UI.ToolCallStatus.Running, msg);
                    _wordApplication.StatusBar = msg;
                });

                if (!result.Success)
                {
                    chat.UpdateToolCall(UI.ToolCallStatus.Error, $"纠错失败：{result.ErrorMessage}");
                    _wordApplication.StatusBar = "纠错失败";
                    return;
                }

                if (!result.HasCorrections)
                {
                    chat.UpdateToolCall(UI.ToolCallStatus.Success, "未发现需要修改的内容，文本质量良好！");
                    _wordApplication.StatusBar = "纠错完成，未发现问题";
                    return;
                }

                chat.UpdateToolCall(UI.ToolCallStatus.Running, "正在应用修改...");
                int applied = ApplyCorrections(activeDoc, selStart, selEnd, result.Corrections);

                var details = new List<string>();
                foreach (var c in result.Corrections)
                    details.Add($"\"{c.Original}\" → \"{c.Replacement}\"");
                chat.UpdateToolCall(UI.ToolCallStatus.Success,
                    $"共发现 {result.Corrections.Count} 处问题，已修改 {applied} 处（审阅模式）",
                    details);

                if (!string.IsNullOrEmpty(result.Summary))
                    chat.AppendResultText($"**总结：**{result.Summary}");

                var correctionDetails = string.Join("\n", result.Corrections.Select(c => $"「{c.Original}」→「{c.Replacement}」"));
                Memory.RecordPluginAction("选中文本纠错",
                    $"对选中的 {originalText.Length} 字符文本进行纠错（{modeName}）",
                    $"发现 {result.Corrections.Count} 处问题，已修改 {applied} 处。\n{correctionDetails}");

                _wordApplication.StatusBar = $"纠错完成，已修改 {applied} 处";
            }
            finally
            {
                ClearHighlight(activeDoc, selStart, selEnd);
            }
        }

        private async System.Threading.Tasks.Task CorrectAllTextAsync()
        {
            var activeDoc = _wordApplication.ActiveDocument;
            string fullText = activeDoc.Content.Text;

            if (string.IsNullOrEmpty(fullText?.Trim()))
            {
                MessageBox.Show("文档内容为空", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var chat = EnsureTaskPaneVisible();
            if (chat == null)
            {
                MessageBox.Show("无法打开AI福星面板", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 将全文按段落分块
            var paragraphs = new List<ParagraphChunk>();
            foreach (NetOffice.WordApi.Paragraph para in activeDoc.Paragraphs)
            {
                var range = para.Range;
                string paraText = range.Text;
                if (!string.IsNullOrWhiteSpace(paraText) && paraText.Trim().Length > 1)
                {
                    paragraphs.Add(new ParagraphChunk
                    {
                        Text = paraText,
                        Start = range.Start,
                        End = range.End
                    });
                }
            }

            if (paragraphs.Count == 0)
            {
                MessageBox.Show("文档中无有效文本段落", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var chunks = MergeIntoChunks(paragraphs, 5000);

            string modeName = GetCorrectionModeName(_correctionMode);
            var toolCard = chat.AddToolCallMessage($"全文纠错（{modeName}）", $"共 {chunks.Count} 个文本段，准备开始...");

            int totalApplied = 0;
            var service = TextCorrectionService.FromConfig();

            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];

                HighlightRange(activeDoc, chunk.Start, chunk.End);

                string statusMsg = $"正在分析第 {i + 1}/{chunks.Count} 段（约 {chunk.Text.Length} 字）...";
                chat.UpdateToolCall(UI.ToolCallStatus.Running, statusMsg, new List<string>
                {
                    $"进度：第 {i + 1}/{chunks.Count} 段",
                    $"已发现问题：{totalApplied} 处"
                });
                _wordApplication.StatusBar = statusMsg;

                var result = await service.CorrectTextAsync(chunk.Text, _correctionMode, msg =>
                {
                    chat.UpdateToolCall(UI.ToolCallStatus.Running, msg, new List<string>
                    {
                        $"进度：第 {i + 1}/{chunks.Count} 段",
                        $"已发现问题：{totalApplied} 处"
                    });
                });

                ClearHighlight(activeDoc, chunk.Start, chunk.End);

                if (result.Success && result.HasCorrections)
                {
                    chat.UpdateToolCall(UI.ToolCallStatus.Running, $"正在应用第 {i + 1} 段的修改...", new List<string>
                    {
                        $"进度：第 {i + 1}/{chunks.Count} 段",
                        $"已发现问题：{totalApplied} 处"
                    });
                    int applied = ApplyCorrections(activeDoc, chunk.Start, chunk.End, result.Corrections);
                    totalApplied += applied;
                }
            }

            if (totalApplied > 0)
            {
                chat.UpdateToolCall(UI.ToolCallStatus.Success,
                    $"共分析 {chunks.Count} 段文本，发现并修改 {totalApplied} 处问题",
                    new List<string> { "已开启审阅模式，请查看修订内容和批注" });
            }
            else
            {
                chat.UpdateToolCall(UI.ToolCallStatus.Success,
                    $"共分析 {chunks.Count} 段文本",
                    new List<string> { "全文质量良好，未发现需要修改的内容！" });
            }

            Memory.RecordPluginAction("全文纠错",
                $"对全文 {fullText.Length} 字符进行纠错（{modeName}）",
                $"共分析 {chunks.Count} 段文本，修改 {totalApplied} 处");

            _wordApplication.StatusBar = $"全文纠错完成，已修改 {totalApplied} 处";
        }

        // ── TaskPane 辅助方法 ──

        /// <summary>
        /// 安全获取 Word 窗口句柄，兼容 Office 2010。
        /// Office 2010 的 COM 对象模型可能不支持 Hwnd 属性。
        /// </summary>
        private int GetWindowHwnd(NetOffice.WordApi.Window window)
        {
            if (window == null) return 0;
            try
            {
                return (int)window.Hwnd;
            }
            catch
            {
                // Office 2010 不支持 Hwnd 属性，尝试通过进程获取
                try
                {
                    var processes = System.Diagnostics.Process.GetProcessesByName("WINWORD");
                    if (processes.Length > 0)
                        return (int)processes[0].MainWindowHandle;
                }
                catch { }
                return 0;
            }
        }

        /// <summary>
        /// 获取当前活动窗口的 WindowPaneContext（不创建）。
        /// 如未找到返回 null。
        /// </summary>
        private WindowPaneContext GetActiveWindowContext()
        {
            try
            {
                var activeWindow = _wordApplication?.ActiveWindow;
                if (activeWindow == null) return null;
                int hwnd = GetWindowHwnd(activeWindow);
                _windowPanes.TryGetValue(hwnd, out var ctx);
                return ctx;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 获取当前活动窗口对应的 WindowPaneContext；若不存在则创建一个新的。
        /// Word SDI 模式下每个文档窗口拥有独立的 TaskPane + ChatMemory。
        /// </summary>
        private WindowPaneContext GetOrCreatePaneForActiveWindow()
        {
            var activeWindow = _wordApplication.ActiveWindow;
            if (activeWindow == null)
                throw new InvalidOperationException("没有活动窗口");

            int hwnd = GetWindowHwnd(activeWindow);

            // 已有映射 → 直接返回
            if (_windowPanes.TryGetValue(hwnd, out var existing))
                return existing;

            // 兜底：初始化时 [CustomPane] 创建的第一个 CTP 可能尚未绑定到 _windowPanes
            if (TaskPanes.Count > 0 && TaskPanes[0].IsLoaded)
            {
                var firstCtp = TaskPanes[0].Pane;
                // 检查是否还有未分配的初始 CTP
                bool firstClaimed = false;
                foreach (var v in _windowPanes.Values)
                {
                    if (object.ReferenceEquals(v.CTP, firstCtp)) { firstClaimed = true; break; }
                }
                if (!firstClaimed && firstCtp != null)
                {
                    var firstControl = TaskPaneInstances.Count > 0
                        ? (TaskPaneControl)TaskPaneInstances[0]
                        : null;
                    if (firstControl != null)
                    {
                        var ctx0 = new WindowPaneContext(firstCtp, firstControl);
                        _windowPanes[hwnd] = ctx0;
                        System.Diagnostics.Debug.WriteLine(
                            $"[TaskPane] 窗口 0x{hwnd:X} 复用初始 CTP");
                        return ctx0;
                    }
                }
            }

            // ── 新窗口：通过 ICTPFactory 创建真实的 Office CustomTaskPane ──
            if (_ctpFactory == null)
                throw new InvalidOperationException("ICTPFactory 尚未初始化，无法创建新 TaskPane");

            var ctp = _ctpFactory.CreateCTP(
                typeof(TaskPaneControl).FullName,   // COM ProgId（与类的完全限定名一致）
                "AI福星",
                activeWindow);                      // 绑定到当前窗口

            ctp.DockPosition = NetOffice.OfficeApi.Enums.MsoCTPDockPosition.msoCTPDockPositionRight;
            ctp.Width = 500;
            ctp.Visible = false;

            // 获取 Office 为我们实例化的 TaskPaneControl
            var control = (TaskPaneControl)ctp.ContentControl;
            // 手动触发 ITaskPane.OnConnection（NetOffice 只对 [CustomPane] 自动调用）
            control.OnConnection(_wordApplication, ctp, new object[0]);

            var newCtx = new WindowPaneContext(ctp, control);
            _windowPanes[hwnd] = newCtx;

            System.Diagnostics.Debug.WriteLine(
                $"[TaskPane] 为窗口 0x{hwnd:X} 创建新 CTP（总窗口数: {_windowPanes.Count}）");

            return newCtx;
        }

        /// <summary>确保 TaskPane 可见，返回当前窗口的 TaskPaneControl 实例</summary>
        private TaskPaneControl EnsureTaskPaneVisible()
        {
            var ctx = GetOrCreatePaneForActiveWindow();
            if (!ctx.Visible)
            {
                ctx.Width = 500;
                ctx.Visible = true;
            }
            // 确保启动安全提示和 LLM 问候健康检查已触发
            ctx.Control.ShowStartupWarningIfNeeded();
            ctx.Control.CheckAndRequestGreeting();
            return ctx.Control;
        }

        // ── Word 文本高亮 ──

        // 浅蓝色底色（BGR 格式: B=250, G=240, R=228）
        private static readonly int HIGHLIGHT_BGR = 250 * 65536 + 240 * 256 + 228;

        /// <summary>给指定范围设置浅色底色以示正在处理</summary>
        private void HighlightRange(NetOffice.WordApi.Document doc, int start, int end)
        {
            try
            {
                var range = doc.Range(start, end);
                range.Shading.BackgroundPatternColor =
                    (NetOffice.WordApi.Enums.WdColor)HIGHLIGHT_BGR;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"设置高亮失败: {ex.Message}");
            }
        }

        /// <summary>清除指定范围的底色</summary>
        private void ClearHighlight(NetOffice.WordApi.Document doc, int start, int end)
        {
            try
            {
                var range = doc.Range(start, end);
                range.Shading.BackgroundPatternColor =
                    NetOffice.WordApi.Enums.WdColor.wdColorAutomatic;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"清除高亮失败: {ex.Message}");
            }
        }

        // ── 在审阅模式下直接修改文本，并以批注标注修改原因 ──

        private int ApplyCorrections(NetOffice.WordApi.Document doc, int rangeStart, int rangeEnd, List<CorrectionItem> corrections)
        {
            int applied = 0;

            // 开启修订追踪，让用户能审阅每一处修改
            bool wasTracking = doc.TrackRevisions;
            doc.TrackRevisions = true;

            // 临时修改用户名，让修订显示为"AI福星"
            var savedName = _wordApplication.UserName;
            var savedInitials = _wordApplication.UserInitials;
            _wordApplication.UserName = "AI福星";
            _wordApplication.UserInitials = "AI";

            try
            {
                foreach (var correction in corrections)
                {
                    try
                    {
                        if (string.IsNullOrEmpty(correction.Original))
                            continue;

                        // Word Find 有 255 字符限制
                        if (correction.Original.Length > 255)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"纠错跳过（文本超过255字符）: '{correction.Original.Substring(0, 50)}...'");
                            continue;
                        }

                        // 在指定范围内查找原文
                        var searchRange = doc.Range(rangeStart, rangeEnd);
                        searchRange.Find.ClearFormatting();
                        searchRange.Find.Text = correction.Original;
                        searchRange.Find.Forward = true;
                        searchRange.Find.Wrap = NetOffice.WordApi.Enums.WdFindWrap.wdFindStop;
                        searchRange.Find.MatchCase = false;
                        searchRange.Find.MatchWholeWord = false;

                        bool found = searchRange.Find.Execute();

                        if (found)
                        {
                            // 记住找到的位置，用于添加批注
                            int foundStart = searchRange.Start;

                            // 直接替换文本（修订追踪会自动记录删除线+新增内容）
                            searchRange.Text = correction.Replacement;

                            // 替换后 searchRange 已缩放到新文本范围，在此处添加修改原因批注
                            if (!string.IsNullOrEmpty(correction.Reason))
                            {
                                var commentRange = doc.Range(foundStart, foundStart + correction.Replacement.Length);
                                doc.Comments.Add(commentRange, correction.Reason);
                            }

                            applied++;

                            // 替换可能导致 rangeEnd 偏移，重新计算
                            rangeEnd += correction.Replacement.Length - correction.Original.Length;
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"纠错跳过（Word中未找到匹配文本）: '{correction.Original}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"应用纠错失败: {ex.Message}");
                    }
                }
            }
            finally
            {
                // 恢复用户名
                _wordApplication.UserName = savedName;
                _wordApplication.UserInitials = savedInitials;

                // 退出修订追踪模式（已有修订仍可见，避免后续操作被误记为修订）
                doc.TrackRevisions = false;
            }

            return applied;
        }

        // ── 段落分块辅助 ──

        private class ParagraphChunk
        {
            public string Text { get; set; }
            public int Start { get; set; }
            public int End { get; set; }
        }

        private List<ParagraphChunk> MergeIntoChunks(List<ParagraphChunk> paragraphs, int maxChars)
        {
            var chunks = new List<ParagraphChunk>();
            var current = new ParagraphChunk { Text = "", Start = paragraphs[0].Start, End = paragraphs[0].End };

            foreach (var para in paragraphs)
            {
                if (current.Text.Length + para.Text.Length > maxChars && current.Text.Length > 0)
                {
                    chunks.Add(current);
                    current = new ParagraphChunk { Text = "", Start = para.Start, End = para.End };
                }

                current.Text += para.Text;
                current.End = para.End;
            }

            if (current.Text.Length > 0)
                chunks.Add(current);

            return chunks;
        }



        /// <summary>防重入标志，防止 Ribbon 回调在同一次点击中被多次触发</summary>
        private bool _toggleInProgress;

        public void toggle_taskpane_btn_Click(NetOffice.OfficeApi.IRibbonControl control)
        {
            if (_toggleInProgress) return;
            _toggleInProgress = true;
            try
            {
                var ctx = GetOrCreatePaneForActiveWindow();
                bool newVisible = !ctx.Visible;

                System.Diagnostics.Debug.WriteLine(
                    $"[toggle] Windows={_windowPanes.Count} Before={ctx.Visible} Target={newVisible}");

                if (newVisible)
                    ctx.Width = 500;

                ctx.Visible = newVisible;

                // 面板变为可见时确保安全提示 + LLM 问候健康检查
                if (newVisible)
                {
                    ctx.Control.ShowStartupWarningIfNeeded();
                    ctx.Control.CheckAndRequestGreeting();
                }

                System.Diagnostics.Debug.WriteLine($"[toggle] After={ctx.Visible}");
            }
            catch (Exception ex)
            {
                MessageBox.Show("TaskPane操作失败: " + ex.Message, "福星错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                System.Diagnostics.Debug.WriteLine($"TaskPane操作失败: {ex}");
            }
            finally
            {
                _toggleInProgress = false;
            }
        }

        public void setting_btn_Click(NetOffice.OfficeApi.IRibbonControl control)
        {
            try
            {
                SettingForm settingForm = new SettingForm();
                settingForm.OnConfigUpdated += () =>
                {
                    _networkHelper = new NetWorkHelper();
                    var updatedConfig = new ConfigLoader().LoadConfig();
                    DebugLogger.Instance.Enabled = updatedConfig.DeveloperMode;
                    DebugLogger.Instance.LogInfo("配置更新，DeveloperMode=" + updatedConfig.DeveloperMode);
                };
                settingForm.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show("卸载插件时错误: " + ex.Message, "福星错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void about_btn_Click(NetOffice.OfficeApi.IRibbonControl control)
        {
            try
            {
                AboutDialog aboutDialog = new AboutDialog();
                aboutDialog.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show("显示设置信息时错误: " + ex.Message, "福星错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }



        #endregion

        #region 自动添加注释

        private void AddCommentWithCustomUserName(Range range, string commentContent)
        {
            var tempUserName = _wordApplication.UserName;
            var tempUserNameInitials = _wordApplication.UserInitials;

            try
            {
                _wordApplication.UserName = "AI福星";
                _wordApplication.UserInitials = "AI福星";
                range.Comments.Add(range, commentContent);
            }
            finally
            {
                _wordApplication.UserName = tempUserName;
                _wordApplication.UserInitials = tempUserNameInitials;
            }
        }

        private void FormatTable(NetOffice.WordApi.Table table)
        {
            // ── 1. 应用 Word 内置的“网格型”样式（用于稳定边框结构） ──
            object style = NetOffice.WordApi.Enums.WdBuiltinStyle.wdStyleTableLightGrid;
            table.Style = style;

            // ── 2. 统一设置表格边框（柔和、清爽） ──
            table.Borders.InsideLineStyle = NetOffice.WordApi.Enums.WdLineStyle.wdLineStyleSingle;
            table.Borders.InsideLineWidth = NetOffice.WordApi.Enums.WdLineWidth.wdLineWidth025pt;
            table.Borders.InsideColor = NetOffice.WordApi.Enums.WdColor.wdColorAutomatic;

            table.Borders.OutsideLineStyle = NetOffice.WordApi.Enums.WdLineStyle.wdLineStyleSingle;
            table.Borders.OutsideLineWidth = NetOffice.WordApi.Enums.WdLineWidth.wdLineWidth050pt;
            table.Borders.OutsideColor = NetOffice.WordApi.Enums.WdColor.wdColorAutomatic;

            // ── 3. 清除表格底纹 ──
            // 直接在表格级别清除底纹，无需遍历所有单元格
            table.Shading.BackgroundPatternColor = NetOffice.WordApi.Enums.WdColor.wdColorAutomatic;
            table.Shading.Texture = NetOffice.WordApi.Enums.WdTextureIndex.wdTextureNone;

            // ── 4. 文本与排版格式：宋体 12pt 居中，清除粗体和颜色 ──
            table.Range.ParagraphFormat.Alignment = NetOffice.WordApi.Enums.WdParagraphAlignment.wdAlignParagraphCenter;
            table.Range.Font.Name = "宋体";
            table.Range.Font.NameFarEast = "宋体";
            table.Range.Font.Size = 12;
            table.Range.Font.Bold = 0;
            table.Range.Font.Color = NetOffice.WordApi.Enums.WdColor.wdColorAutomatic;

            table.Rows.HeightRule = NetOffice.WordApi.Enums.WdRowHeightRule.wdRowHeightAtLeast;
            table.Rows.Height = 20;

            // ── 5. 表头：上下左右居中 + 15% 灰底 + 细分割线 ──
            if (table.Rows.Count > 0)
            {
                var headerRow = table.Rows[1];
                headerRow.Range.ParagraphFormat.Alignment = NetOffice.WordApi.Enums.WdParagraphAlignment.wdAlignParagraphCenter;
                headerRow.Cells.VerticalAlignment = NetOffice.WordApi.Enums.WdCellVerticalAlignment.wdCellAlignVerticalCenter;
                headerRow.Range.Font.Bold = 1;
                headerRow.Shading.BackgroundPatternColor = NetOffice.WordApi.Enums.WdColor.wdColorGray15;
                headerRow.Shading.Texture = NetOffice.WordApi.Enums.WdTextureIndex.wdTextureNone;

                var headerBorderTypes = new[]
                {
                    NetOffice.WordApi.Enums.WdBorderType.wdBorderTop,
                    NetOffice.WordApi.Enums.WdBorderType.wdBorderBottom,
                    NetOffice.WordApi.Enums.WdBorderType.wdBorderLeft,
                    NetOffice.WordApi.Enums.WdBorderType.wdBorderRight,
                    NetOffice.WordApi.Enums.WdBorderType.wdBorderVertical,
                };
                foreach (var borderType in headerBorderTypes)
                {
                    var border = headerRow.Borders[borderType];
                    border.LineStyle = NetOffice.WordApi.Enums.WdLineStyle.wdLineStyleSingle;
                    border.LineWidth = NetOffice.WordApi.Enums.WdLineWidth.wdLineWidth025pt;
                    border.Color = NetOffice.WordApi.Enums.WdColor.wdColorAutomatic;
                }
            }
        }

        // ── 封装公开方法 — 供 ToolRegistry 在工具调用时访问 Word COM 操作 ──

        /// <summary>格式化表格（公开，供 ToolRegistry 调用）</summary>
        public void FormatTablePublic(NetOffice.WordApi.Table table) => FormatTable(table);

        /// <summary>高亮区域（公开）</summary>
        public void HighlightRangePublic(NetOffice.WordApi.Document doc, int start, int end) => HighlightRange(doc, start, end);

        /// <summary>清除高亮（公开）</summary>
        public void ClearHighlightPublic(NetOffice.WordApi.Document doc, int start, int end) => ClearHighlight(doc, start, end);

        /// <summary>应用纠错（审阅模式直接修改+批注原因，公开）</summary>
        public int ApplyCorrectionsPublic(NetOffice.WordApi.Document doc, int rangeStart, int rangeEnd, List<CorrectionItem> corrections)
            => ApplyCorrections(doc, rangeStart, rangeEnd, corrections);

        /// <summary>添加自定义用户名批注（公开）</summary>
        public void AddCommentPublic(NetOffice.WordApi.Range range, string content) => AddCommentWithCustomUserName(range, content);

        /// <summary>载入默认AI样式库（公开）</summary>
        public void LoadDefaultStylesPublic() => LoadDefaultAIStyles();

        /// <summary>进入修订追踪模式，并将作者设为"AI福星"，使文本修改可被用户可视化审阅</summary>
        public void EnsureTrackRevisions()
        {
            _savedUserName = _wordApplication.UserName;
            _savedUserInitials = _wordApplication.UserInitials;
            _wordApplication.UserName = "AI福星";
            _wordApplication.UserInitials = "AI";
            _wordApplication.ActiveDocument.TrackRevisions = true;
        }

        /// <summary>退出修订追踪模式，恢复用户名（已有修订仍可见）</summary>
        public void StopTrackRevisions()
        {
            _wordApplication.ActiveDocument.TrackRevisions = false;
            _wordApplication.UserName = _savedUserName;
            _wordApplication.UserInitials = _savedUserInitials;
        }

        #endregion

        #region Ribbon UI 回调方法

        public void OnLoadRibbonUI(NetOffice.OfficeApi.IRibbonUI ribbonUI)
        {
            RibbonUI = ribbonUI;
        }

        public void CorrectionMode_Click(NetOffice.OfficeApi.IRibbonControl control, bool pressed)
        {
            switch (control.Id)
            {
                case "mode_typo_btn": _correctionMode = CorrectionMode.Typo; break;
                case "mode_semantic_btn": _correctionMode = CorrectionMode.Semantic; break;
                case "mode_consistency_btn": _correctionMode = CorrectionMode.Consistency; break;
            }
            // 刷新所有 toggleButton 的选中状态（实现单选效果）
            if (RibbonUI != null)
            {
                RibbonUI.InvalidateControl("mode_typo_btn");
                RibbonUI.InvalidateControl("mode_semantic_btn");
                RibbonUI.InvalidateControl("mode_consistency_btn");
            }
        }

        public bool CorrectionMode_GetPressed(NetOffice.OfficeApi.IRibbonControl control)
        {
            switch (control.Id)
            {
                case "mode_typo_btn": return _correctionMode == CorrectionMode.Typo;
                case "mode_semantic_btn": return _correctionMode == CorrectionMode.Semantic;
                case "mode_consistency_btn": return _correctionMode == CorrectionMode.Consistency;
                default: return false;
            }
        }

        #endregion

        #region 文本格式处理辅助方法

        /// <summary>
        /// 提取段落格式信息
        /// </summary>
        private string ExtractParagraphFormat(NetOffice.WordApi.Paragraph paragraph, NetOffice.WordApi.Range range)
        {
            var formatInfo = new System.Text.StringBuilder();
            
            try
            {
                // 段落格式
                formatInfo.AppendLine("段落格式:");
                formatInfo.AppendLine($"  对齐方式: {paragraph.Range.ParagraphFormat.Alignment}");
                formatInfo.AppendLine($"  左缩进: {paragraph.Range.ParagraphFormat.LeftIndent}pt");
                formatInfo.AppendLine($"  右缩进: {paragraph.Range.ParagraphFormat.RightIndent}pt");
                formatInfo.AppendLine($"  首行缩进: {paragraph.Range.ParagraphFormat.FirstLineIndent}pt");
                formatInfo.AppendLine($"  段前间距: {paragraph.Range.ParagraphFormat.SpaceBefore}pt");
                formatInfo.AppendLine($"  段后间距: {paragraph.Range.ParagraphFormat.SpaceAfter}pt");
                formatInfo.AppendLine($"  行距: {paragraph.Range.ParagraphFormat.LineSpacing}pt");
                formatInfo.AppendLine($"  行距规则: {paragraph.Range.ParagraphFormat.LineSpacingRule}");
                
                // 字体格式
                formatInfo.AppendLine("\n字体格式:");
                formatInfo.AppendLine($"  中文字体: {range.Font.NameFarEast}");
                formatInfo.AppendLine($"  西文字体: {range.Font.Name}");
                formatInfo.AppendLine($"  字号: {range.Font.Size}pt");
                formatInfo.AppendLine($"  字体颜色: {range.Font.Color}");
                formatInfo.AppendLine($"  粗体: {range.Font.Bold}");
                formatInfo.AppendLine($"  斜体: {range.Font.Italic}");
                formatInfo.AppendLine($"  下划线: {range.Font.Underline}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"提取格式信息失败: {ex.Message}");
                formatInfo.AppendLine($"格式信息提取失败: {ex.Message}");
            }
            
            return formatInfo.ToString();
        }

        /// <summary>
        /// 显示样式命名对话框
        /// </summary>
        private void ShowStyleNamingDialog(string formatInfo)
        {
            // 使用AntdUI创建输入对话框
            var styleName = ShowStyleNameInputDialog(
                "创建新样式",
                $"检测到以下格式信息:\n\n{formatInfo}\n\n请输入新样式的名称:",
                "自定义样式");
                
            if (!string.IsNullOrEmpty(styleName))
            {
                CreateCustomStyle(styleName);
            }
        }

        /// <summary>
        /// 创建自定义样式
        /// </summary>
        private void CreateCustomStyle(string styleName)
        {
            try
            {
                var selection = _wordApplication.Selection;
                var document = _wordApplication.ActiveDocument;
                
                // 检查样式是否已存在
                foreach (NetOffice.WordApi.Style style in document.Styles)
                {
                    if (style.NameLocal == styleName)
                    {
                        MessageBox.Show($"样式 '{styleName}' 已存在，请选择其他名称。", "样式名称冲突", 
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
                
                // 创建新样式
                var newStyle = document.Styles.Add(styleName, NetOffice.WordApi.Enums.WdStyleType.wdStyleTypeParagraph);
                
                // 复制当前选中内容的格式到新样式
                var paragraph = selection.Paragraphs[1];
                var range = selection.Range;
                
                // 复制段落格式
                newStyle.ParagraphFormat.Alignment = paragraph.Range.ParagraphFormat.Alignment;
                newStyle.ParagraphFormat.LeftIndent = paragraph.Range.ParagraphFormat.LeftIndent;
                newStyle.ParagraphFormat.RightIndent = paragraph.Range.ParagraphFormat.RightIndent;
                newStyle.ParagraphFormat.FirstLineIndent = paragraph.Range.ParagraphFormat.FirstLineIndent;
                newStyle.ParagraphFormat.SpaceBefore = paragraph.Range.ParagraphFormat.SpaceBefore;
                newStyle.ParagraphFormat.SpaceAfter = paragraph.Range.ParagraphFormat.SpaceAfter;
                newStyle.ParagraphFormat.LineSpacing = paragraph.Range.ParagraphFormat.LineSpacing;
                newStyle.ParagraphFormat.LineSpacingRule = paragraph.Range.ParagraphFormat.LineSpacingRule;
                
                // 复制字体格式
                newStyle.Font.NameFarEast = range.Font.NameFarEast;
                newStyle.Font.Name = range.Font.Name;
                newStyle.Font.Size = range.Font.Size;
                newStyle.Font.Color = range.Font.Color;
                newStyle.Font.Bold = range.Font.Bold;
                newStyle.Font.Italic = range.Font.Italic;
                newStyle.Font.Underline = range.Font.Underline;
                
                MessageBox.Show($"样式 '{styleName}' 创建成功！", "样式创建完成", 
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"创建样式失败: {ex.Message}");
                MessageBox.Show($"创建样式失败: {ex.Message}", "错误", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 载入默认AI样式库
        /// </summary>
        private void LoadDefaultAIStyles()
        {
            try
            {
                var document = _wordApplication.ActiveDocument;
                
                // AI正文样式
                CreateDefaultStyle(document, "AI正文", new DefaultStyleConfig
                {
                    FontName = "宋体",
                    FontNameFarEast = "宋体",
                    FontSize = 12,
                    Alignment = NetOffice.WordApi.Enums.WdParagraphAlignment.wdAlignParagraphJustify,
                    FirstLineIndent = 24, // 首行缩进2字符
                    LineSpacing = 20,
                    LineSpacingRule = NetOffice.WordApi.Enums.WdLineSpacing.wdLineSpaceExactly
                });

                // AI一级标题
                CreateDefaultStyle(document, "AI一级", new DefaultStyleConfig
                {
                    FontName = "黑体",
                    FontNameFarEast = "黑体",
                    FontSize = 16,
                    Bold = true,
                    Alignment = NetOffice.WordApi.Enums.WdParagraphAlignment.wdAlignParagraphCenter,
                    SpaceBefore = 24,
                    SpaceAfter = 18,
                    LineSpacing = 20,
                    LineSpacingRule = NetOffice.WordApi.Enums.WdLineSpacing.wdLineSpaceExactly
                });

                // AI二级标题
                CreateDefaultStyle(document, "AI二级", new DefaultStyleConfig
                {
                    FontName = "黑体",
                    FontNameFarEast = "黑体",
                    FontSize = 14,
                    Bold = true,
                    Alignment = NetOffice.WordApi.Enums.WdParagraphAlignment.wdAlignParagraphLeft,
                    SpaceBefore = 18,
                    SpaceAfter = 12,
                    LineSpacing = 20,
                    LineSpacingRule = NetOffice.WordApi.Enums.WdLineSpacing.wdLineSpaceExactly
                });

                // AI三级到六级标题
                for (int i = 3; i <= 6; i++)
                {
                    CreateDefaultStyle(document, $"AI{GetChineseNumber(i)}级", new DefaultStyleConfig
                    {
                        FontName = "黑体",
                        FontNameFarEast = "黑体",
                        FontSize = 12 + (6 - i), // 随级别递减
                        Bold = true,
                        Alignment = NetOffice.WordApi.Enums.WdParagraphAlignment.wdAlignParagraphLeft,
                        SpaceBefore = 12f,
                        SpaceAfter = 6f,
                        LineSpacing = 20,
                        LineSpacingRule = NetOffice.WordApi.Enums.WdLineSpacing.wdLineSpaceExactly
                    });
                }

                // AI图片样式
                CreateDefaultStyle(document, "AI图片", new DefaultStyleConfig
                {
                    FontName = "宋体",
                    FontNameFarEast = "宋体",
                    FontSize = 10.5f,
                    Alignment = NetOffice.WordApi.Enums.WdParagraphAlignment.wdAlignParagraphCenter,
                    SpaceBefore = 6,
                    SpaceAfter = 12,
                    LineSpacing = 16,
                    LineSpacingRule = NetOffice.WordApi.Enums.WdLineSpacing.wdLineSpaceExactly
                });

                // AI表格样式
                CreateDefaultStyle(document, "AI表格", new DefaultStyleConfig
                {
                    FontName = "宋体",
                    FontNameFarEast = "宋体",
                    FontSize = 10.5f,
                    Alignment = NetOffice.WordApi.Enums.WdParagraphAlignment.wdAlignParagraphCenter,
                    SpaceBefore = 6f,
                    SpaceAfter = 6f,
                    LineSpacing = 16,
                    LineSpacingRule = NetOffice.WordApi.Enums.WdLineSpacing.wdLineSpaceExactly
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"载入默认样式失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 创建默认样式
        /// </summary>
        private void CreateDefaultStyle(NetOffice.WordApi.Document document, string styleName, DefaultStyleConfig config)
        {
            try
            {
                // 检查样式是否已存在，存在则删除重建
                foreach (NetOffice.WordApi.Style existingStyle in document.Styles)
                {
                    if (existingStyle.NameLocal == styleName)
                    {
                        existingStyle.Delete();
                        break;
                    }
                }

                // 创建新样式
                var newStyle = document.Styles.Add(styleName, NetOffice.WordApi.Enums.WdStyleType.wdStyleTypeParagraph);
                
                // 设置字体
                newStyle.Font.Name = config.FontName;
                newStyle.Font.NameFarEast = config.FontNameFarEast;
                newStyle.Font.Size = config.FontSize;
                if (config.Bold) newStyle.Font.Bold = 1;
                if (config.Italic) newStyle.Font.Italic = 1;
                
                // 设置段落格式
                newStyle.ParagraphFormat.Alignment = config.Alignment;
                newStyle.ParagraphFormat.FirstLineIndent = config.FirstLineIndent;
                newStyle.ParagraphFormat.LeftIndent = config.LeftIndent;
                newStyle.ParagraphFormat.RightIndent = config.RightIndent;
                newStyle.ParagraphFormat.SpaceBefore = config.SpaceBefore;
                newStyle.ParagraphFormat.SpaceAfter = config.SpaceAfter;
                newStyle.ParagraphFormat.LineSpacing = config.LineSpacing;
                newStyle.ParagraphFormat.LineSpacingRule = config.LineSpacingRule;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"创建样式 '{styleName}' 失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 获取中文数字
        /// </summary>
        private string GetChineseNumber(int number)
        {
            string[] chineseNumbers = { "", "一", "二", "三", "四", "五", "六", "七", "八", "九" };
            return number <= chineseNumbers.Length - 1 ? chineseNumbers[number] : number.ToString();
        }

        /// <summary>
        /// 默认样式配置类
        /// </summary>
        private class DefaultStyleConfig
        {
            public string FontName { get; set; } = "宋体";
            public string FontNameFarEast { get; set; } = "宋体";
            public float FontSize { get; set; } = 12;
            public bool Bold { get; set; } = false;
            public bool Italic { get; set; } = false;
            public NetOffice.WordApi.Enums.WdParagraphAlignment Alignment { get; set; } = NetOffice.WordApi.Enums.WdParagraphAlignment.wdAlignParagraphLeft;
            public float FirstLineIndent { get; set; } = 0;
            public float LeftIndent { get; set; } = 0;
            public float RightIndent { get; set; } = 0;
            public float SpaceBefore { get; set; } = 0;
            public float SpaceAfter { get; set; } = 0;
            public float LineSpacing { get; set; } = 12;
            public NetOffice.WordApi.Enums.WdLineSpacing LineSpacingRule { get; set; } = NetOffice.WordApi.Enums.WdLineSpacing.wdLineSpaceSingle;
        }

        /// <summary>
        /// 使用AntdUI显示样式名称输入对话框
        /// </summary>
        private string ShowStyleNameInputDialog(string title, string message, string defaultValue = "")
        {
            string result = null;
            
            // 创建对话框窗体
            var dialog = new AntdUI.Window
            {
                Text = title,
                Size = new Size(480, 320),
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                BackColor = Color.White,
                TopMost = true
            };

            // 主容器
            var mainPanel = new AntdUI.Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(20),
                BackColor = Color.White
            };

            // 消息标签
            var messageLabel = new AntdUI.Label
            {
                Text = message,
                Font = new System.Drawing.Font("Microsoft YaHei UI", 10F),
                ForeColor = Color.FromArgb(102, 102, 102),
                Dock = DockStyle.Top,
                Height = 120,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.TopLeft,
                AutoSize = false
            };

            // 输入框标签
            var inputLabel = new AntdUI.Label
            {
                Text = "样式名称:",
                Font = new System.Drawing.Font("Microsoft YaHei UI", 10F),
                ForeColor = Color.FromArgb(51, 51, 51),
                Dock = DockStyle.Top,
                Height = 25,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.BottomLeft
            };

            // 输入框
            var inputBox = new AntdUI.Input
            {
                Text = defaultValue,
                Font = new System.Drawing.Font("Microsoft YaHei UI", 10F),
                Dock = DockStyle.Top,
                Height = 36,
                BorderWidth = 1,
                BorderColor = Color.FromArgb(217, 217, 217),
                Radius = 6,
                Margin = new Padding(0, 5, 0, 20)
            };

            // 按钮容器
            var buttonPanel = new AntdUI.Panel
            {
                Dock = DockStyle.Top,
                Height = 40,
                BackColor = Color.Transparent
            };

            // 确定按钮
            var okButton = new AntdUI.Button
            {
                Text = "确定",
                Size = new Size(80, 36),
                Location = new System.Drawing.Point(280, 2),
                Type = AntdUI.TTypeMini.Primary,
                Font = new System.Drawing.Font("Microsoft YaHei UI", 10F),
                Radius = 6
            };

            // 取消按钮
            var cancelButton = new AntdUI.Button
            {
                Text = "取消",
                Size = new Size(80, 36),
                Location = new System.Drawing.Point(370, 2),
                Type = AntdUI.TTypeMini.Default,
                Font = new System.Drawing.Font("Microsoft YaHei UI", 10F),
                Radius = 6
            };

            // 事件处理
            okButton.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(inputBox.Text))
                {
                    AntdUI.Message.error(dialog, "样式名称不能为空");
                    return;
                }
                result = inputBox.Text.Trim();
                dialog.DialogResult = DialogResult.OK;
                dialog.Close();
            };

            cancelButton.Click += (s, e) =>
            {
                dialog.DialogResult = DialogResult.Cancel;
                dialog.Close();
            };

            // 回车键处理
            inputBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    okButton.PerformClick();
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    cancelButton.PerformClick();
                }
            };

            // 添加控件
            buttonPanel.Controls.AddRange(new Control[] { cancelButton, okButton });
            mainPanel.Controls.AddRange(new Control[] { buttonPanel, inputBox, inputLabel, messageLabel });
            dialog.Controls.Add(mainPanel);

            // 显示对话框
            inputBox.Focus();
            inputBox.SelectAll();
            
            return dialog.ShowDialog() == DialogResult.OK ? result : null;
        }

        #endregion
    }
}