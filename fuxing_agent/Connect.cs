using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Net;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;
using Extensibility;
using Microsoft.Office.Core;
using Microsoft.Office.Interop.Word;
using FuXingAgent.Agents;
using FuXingAgent.Core;

namespace FuXingAgent
{
    [ComVisible(true)]
    [Guid("D0E1F2A3-B4C5-6789-ABCD-EF0123456789")]
    [ProgId("FuXingAgent.Connect")]
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public class Connect : IDTExtensibility2, IRibbonExtensibility, ICustomTaskPaneConsumer
    {
        // Word 是非托管宿主，不会读取 .dll.config 中的 bindingRedirect，
        // 必须在任何类型解析前通过 AssemblyResolve 手动加载依赖。
        static Connect()
        {
            var addinDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                var name = new AssemblyName(args.Name);
                var candidate = Path.Combine(addinDir, name.Name + ".dll");
                if (File.Exists(candidate))
                {
                    try { return Assembly.LoadFrom(candidate); }
                    catch { }
                }
                return null;
            };

            // Net462 + Office host may default to legacy protocols; ensure TLS1.2 is available.
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var proxy = WebRequest.DefaultWebProxy;
                if (proxy != null)
                    proxy.Credentials = CredentialCache.DefaultCredentials;
            }
            catch { }
        }

        public static Connect CurrentInstance { get; private set; }

        private Microsoft.Office.Interop.Word.Application _wordApplication;
        private ConfigLoader _configLoader;
        private AgentBootstrap _agentBootstrap;
        private ToolRegistry _toolRegistry;
        private MainAgent _mainAgent;
        private SubAgentRunner _subAgentRunner;
        private readonly HashSet<string> _fxStyleInitializedDocKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool _globalStartupWarningShown;

        private ICTPFactory _ctpFactory;
        private readonly Dictionary<int, WindowPaneContext> _windowPanes = new Dictionary<int, WindowPaneContext>();

        internal IRibbonUI RibbonUI { get; private set; }

        public Microsoft.Office.Interop.Word.Application WordApplication => _wordApplication;
        public ConfigLoader ConfigLoaderInstance => _configLoader;
        public AgentBootstrap AgentBootstrapInstance => _agentBootstrap;
        public ToolRegistry ToolRegistryInstance => _toolRegistry;
        public MainAgent MainAgentInstance => _mainAgent;
        public SubAgentRunner SubAgentRunnerInstance => _subAgentRunner;
        public CursorSnapshot SelectionSnapshot { get; set; }

        // ═══════════════════════════════════════════════════════════════
        //  IDTExtensibility2
        // ═══════════════════════════════════════════════════════════════

        public void OnConnection(object application, ext_ConnectMode connectMode,
            object addInInst, ref Array custom)
        {
            _wordApplication = (Microsoft.Office.Interop.Word.Application)application;
            CurrentInstance = this;
        }

        public void OnStartupComplete(ref Array custom)
        {
            try
            {
                AntdUI.SvgDb.Emoji = AntdUI.FluentFlat.Emoji;

                _configLoader = new ConfigLoader();
                var cfg = _configLoader.LoadConfig();
                DebugLogger.Instance.Enabled = true;
                DebugLogger.Instance.LogInfo($"Network bootstrap: SecurityProtocol={ServicePointManager.SecurityProtocol}");

                ResourceManager.CopyResourcesToOutput();
                Debug.WriteLine(ResourceManager.GetResourceStatus());

                _agentBootstrap = new AgentBootstrap(_configLoader);
                _agentBootstrap.Initialize();

                _toolRegistry = new ToolRegistry();
                _toolRegistry.Initialize(this);

                _mainAgent = new MainAgent(_agentBootstrap, _toolRegistry, cfg.MaxToolRounds);
                _subAgentRunner = new SubAgentRunner(_agentBootstrap, _toolRegistry);

                BindInitialTaskPane();
            }
            catch (Exception ex)
            {
                MessageBox.Show("福星初始化失败: " + ex.Message, "FuXingAgent",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void OnDisconnection(ext_DisconnectMode removeMode, ref Array custom)
        {
            try
            {
                foreach (var ctx in _windowPanes.Values)
                {
                    try { ctx.Dispose(); } catch { }
                }
                _windowPanes.Clear();

                if (_wordApplication != null)
                {
                    Marshal.ReleaseComObject(_wordApplication);
                    _wordApplication = null;
                }

                CurrentInstance = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Connect] OnDisconnection error: {ex.Message}");
            }
        }

        public void OnAddInsUpdate(ref Array custom) { }
        public void OnBeginShutdown(ref Array custom) { }

        // ═══════════════════════════════════════════════════════════════
        //  IRibbonExtensibility
        // ═══════════════════════════════════════════════════════════════

        public string GetCustomUI(string ribbonID)
        {
            string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string ribbonPath = Path.Combine(assemblyDir, "RibbonUI.xml");
            if (File.Exists(ribbonPath))
                return File.ReadAllText(ribbonPath);
            throw new FileNotFoundException("RibbonUI.xml 未找到", ribbonPath);
        }

        public void Ribbon_OnLoad(IRibbonUI ribbonUI)
        {
            RibbonUI = ribbonUI;
        }

        public object GetButtonImage(IRibbonControl control)
        {
            if (control == null || string.IsNullOrEmpty(control.Id))
                return null;
            string iconName = GetIconNameForControl(control.Id);
            if (string.IsNullOrEmpty(iconName))
                return null;
            return ResourceManager.GetIconAsPictureDisp(iconName);
        }

        private static string GetIconNameForControl(string controlId)
        {
            switch (controlId)
            {
                case "toggle_taskpane_btn": return "logo.png";
                case "CheckStandardValidityButton": return "icons8-deepseek-150.png";
                case "LearnFormatButton": return "icons8-learn_styles.png";
                case "LoadDefaultStylesButton": return "icons8-load_default_styles-all.png";
                case "setting_btn": return "icons8-setting-128.png";
                case "about_btn": return "icons8-clean-96.png";
                default: return null;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  ICustomTaskPaneConsumer
        // ═══════════════════════════════════════════════════════════════

        public void CTPFactoryAvailable(ICTPFactory CTPFactoryInst)
        {
            _ctpFactory = CTPFactoryInst;
            Debug.WriteLine("[CTP] ICTPFactory 已缓存");
        }

        // ═══════════════════════════════════════════════════════════════
        //  Ribbon 按钮事件
        // ═══════════════════════════════════════════════════════════════

        public void toggle_taskpane_btn_Click(IRibbonControl control)
        {
            try
            {
                var ctx = GetOrCreatePaneForActiveWindow();
                ApplyScaledTaskPaneWidth(ctx, _wordApplication?.ActiveWindow);
                ctx.Visible = !ctx.Visible;
                if (ctx.Visible)
                    ShowStartupWarningIfNeeded();
            }
            catch (Exception ex)
            {
                MessageBox.Show("切换面板失败: " + ex.Message, "FuXingAgent",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void CheckStandardValidityButton_Click(IRibbonControl control)
        {
            try
            {
                var sel = _wordApplication.Selection;
                string text = sel?.Text?.Trim();
                if (string.IsNullOrEmpty(text))
                {
                    MessageBox.Show("请先选择要校验的文本", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                var ctx = GetOrCreatePaneForActiveWindow();
                ctx.Visible = true;
                string message = $"[用户选中了以下文本，请校验其中引用的标准是否有效]\n\n{text}";
                ctx.Control.InjectUserMessage(message);
            }
            catch (Exception ex)
            {
                MessageBox.Show("校验标准失败: " + ex.Message, "FuXingAgent",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void LearnFormatButton_Click(IRibbonControl control)
        {
            try
            {
                var selection = _wordApplication?.Selection;
                if (selection == null || selection.Range == null)
                    throw new InvalidOperationException("无法获取当前选择");

                var paragraphs = selection.Range.Paragraphs;
                if (paragraphs == null || paragraphs.Count == 0)
                    throw new InvalidOperationException("请将光标置于段落内或选中段落文本");

                var sourceParagraph = paragraphs[1];
                var doc = _wordApplication.ActiveDocument;
                if (doc == null)
                    throw new InvalidOperationException("没有活动文档");

                string styleName = $"fx_学习_{DateTime.Now:yyyyMMdd_HHmmss}";
                CreateOrUpdateStyleFromParagraph(doc, sourceParagraph, styleName);
                sourceParagraph.Range.set_Style(styleName);

                var ctx = GetOrCreatePaneForActiveWindow();
                ctx.Visible = true;
                ctx.Control.InjectUserMessage($"[我刚从当前段落学习并创建了样式 {styleName}，请告诉我这个样式适合用于哪些内容，并给出 3 条使用建议。]");
            }
            catch (Exception ex)
            {
                MessageBox.Show("学习格式失败: " + ex.Message, "FuXingAgent",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void LoadDefaultStylesButton_Click(IRibbonControl control)
        {
            try
            {
                EnsureFxStylesInActiveDocument();
                MessageBox.Show("默认样式库加载完成", "FuXingAgent",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("载入默认样式失败: " + ex.Message, "FuXingAgent",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void setting_btn_Click(IRibbonControl control)
        {
            try
            {
                using (var form = new SettingForm())
                {
                    form.OnConfigUpdated += () =>
                    {
                        var cfg = _configLoader.LoadConfig();
                        DebugLogger.Instance.Enabled = true;
                    };
                    form.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开设置失败: " + ex.Message, "FuXingAgent",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void about_btn_Click(IRibbonControl control)
        {
            try
            {
                using (var dialog = new AboutDialog())
                    dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开关于界面失败: " + ex.Message, "FuXingAgent",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowStartupWarningIfNeeded()
        {
            if (_globalStartupWarningShown)
                return;

            var cfg = _configLoader?.LoadConfig() ?? new ConfigLoader.Config();
            if (!cfg.ShowStartupWarning)
            {
                _globalStartupWarningShown = true;
                return;
            }

            _globalStartupWarningShown = true;
            try
            {
                using (var dialog = new StartupWarningDialog())
                    dialog.ShowDialog();
            }
            catch { }
        }

        private void EnsureFxStylesInActiveDocument()
        {
            var doc = _wordApplication?.ActiveDocument;
            if (doc == null)
                throw new InvalidOperationException("没有活动文档");

            string docKey = BuildDocumentInitKey(doc);
            if (_fxStyleInitializedDocKeys.Contains(docKey))
                return;

            string addinDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string profilePath = Path.Combine(addinDir, "Skills", "load_default_style", "default_style_profile.json");
            if (!File.Exists(profilePath))
                throw new FileNotFoundException("默认样式配置文件不存在", profilePath);

            var profile = JObject.Parse(File.ReadAllText(profilePath));

            string bodyStyle = profile["body_style"]?.ToString();
            var bodyFontObj = profile["body_font"] as JObject;

            if (!string.IsNullOrWhiteSpace(bodyStyle) && bodyFontObj != null)
            {
                CreateOrUpdateParagraphStyle(
                    doc,
                    bodyStyle,
                    null,
                    bodyStyle,
                    bodyFontObj,
                    new JObject
                    {
                        ["alignment"] = bodyFontObj["alignment"]?.ToString() ?? "justify",
                        ["first_line_indent_pt"] = bodyFontObj["first_line_indent_pt"]?.Value<float>() ?? 0f,
                        ["space_before_pt"] = bodyFontObj["space_before_pt"]?.Value<float>() ?? 0f,
                        ["space_after_pt"] = bodyFontObj["space_after_pt"]?.Value<float>() ?? 0f,
                        ["line_spacing_rule"] = bodyFontObj["line_spacing_rule"]?.ToString() ?? "single",
                        ["line_spacing_pt"] = bodyFontObj["line_spacing_pt"]?.Value<float>() ?? 0f
                    },
                    null);
            }

            var headingStyles = profile["heading_styles"] as JObject;
            var headingFontObj = profile["heading_font"] as JObject;
            var headingLevels = headingFontObj?["levels"] as JObject;

            if (headingStyles != null && headingFontObj != null)
            {
                for (int i = 1; i <= 6; i++)
                {
                    string key = "h" + i;
                    string target = headingStyles[key]?.ToString();
                    if (string.IsNullOrWhiteSpace(target))
                        continue;

                    var levelObj = headingLevels?[key] as JObject;
                    var fontObj = new JObject
                    {
                        ["name"] = headingFontObj["name"]?.ToString(),
                        ["name_ascii"] = headingFontObj["name_ascii"]?.ToString(),
                        ["size_pt"] = levelObj?["size_pt"]?.Value<float>() ?? 12f,
                        ["bold"] = levelObj?["bold"]?.Value<bool>() ?? false
                    };

                    var paraObj = new JObject
                    {
                        ["alignment"] = headingFontObj["alignment"]?.ToString() ?? "left",
                        ["first_line_indent_pt"] = headingFontObj["first_line_indent_pt"]?.Value<float>() ?? 0f,
                        ["left_indent_pt"] = headingFontObj["left_indent_pt"]?.Value<float>() ?? 0f,
                        ["space_before_pt"] = headingFontObj["space_before_pt"]?.Value<float>() ?? 0f,
                        ["space_after_pt"] = headingFontObj["space_after_pt"]?.Value<float>() ?? 0f,
                        ["line_spacing_rule"] = headingFontObj["line_spacing_rule"]?.ToString() ?? "single",
                        ["line_spacing_pt"] = headingFontObj["line_spacing_pt"]?.Value<float>() ?? 0f
                    };

                    CreateOrUpdateParagraphStyle(doc, target, null, bodyStyle, fontObj, paraObj, i);
                }
            }

            string captionStyle = profile["caption"]?["style_name"]?.ToString();
            string captionSeed = profile["caption"]?["seed_from_style_name"]?.ToString();
            if (!string.IsNullOrWhiteSpace(captionStyle))
            {
                CreateOrUpdateParagraphStyle(doc, captionStyle, captionSeed, bodyStyle, null, null, null);
            }

            _fxStyleInitializedDocKeys.Add(docKey);
        }

        private static string BuildDocumentInitKey(Document doc)
        {
            string fullName = "";
            string name = "";
            try { fullName = doc.FullName ?? ""; } catch { }
            try { name = doc.Name ?? ""; } catch { }
            return string.IsNullOrWhiteSpace(fullName)
                ? $"unsaved::{name}::{doc.GetHashCode()}"
                : fullName;
        }

        private void CreateOrUpdateStyleFromParagraph(Document doc, Paragraph sourceParagraph, string styleName)
        {
            var style = TryGetStyle(doc, styleName) ?? doc.Styles.Add(styleName, WdStyleType.wdStyleTypeParagraph);

            var sourceFont = sourceParagraph.Range.Font;
            var sourcePara = sourceParagraph.Range.ParagraphFormat;

            style.Font.Name = sourceFont.Name;
            style.Font.NameFarEast = sourceFont.NameFarEast;
            style.Font.NameAscii = sourceFont.NameAscii;
            style.Font.Size = sourceFont.Size;
            style.Font.Bold = sourceFont.Bold;
            style.Font.Italic = sourceFont.Italic;

            style.ParagraphFormat.Alignment = sourcePara.Alignment;
            style.ParagraphFormat.FirstLineIndent = sourcePara.FirstLineIndent;
            style.ParagraphFormat.LeftIndent = sourcePara.LeftIndent;
            style.ParagraphFormat.RightIndent = sourcePara.RightIndent;
            style.ParagraphFormat.SpaceBefore = sourcePara.SpaceBefore;
            style.ParagraphFormat.SpaceAfter = sourcePara.SpaceAfter;
            style.ParagraphFormat.LineSpacingRule = sourcePara.LineSpacingRule;
            style.ParagraphFormat.LineSpacing = sourcePara.LineSpacing;
        }

        private void CreateOrUpdateParagraphStyle(
            Document doc,
            string styleName,
            string basedOn,
            string nextStyle,
            JObject font,
            JObject paragraph,
            int? outlineLevel)
        {
            if (string.IsNullOrWhiteSpace(styleName))
                return;

            var style = TryGetStyle(doc, styleName) ?? doc.Styles.Add(styleName, WdStyleType.wdStyleTypeParagraph);

            if (!string.IsNullOrWhiteSpace(basedOn))
            {
                object baseStyleObj = basedOn;
                style.set_BaseStyle(ref baseStyleObj);
            }
            if (!string.IsNullOrWhiteSpace(nextStyle))
            {
                object nextStyleObj = nextStyle;
                style.set_NextParagraphStyle(ref nextStyleObj);
            }

            if (font != null)
            {
                if (!string.IsNullOrWhiteSpace(font["name"]?.ToString()))
                    style.Font.NameFarEast = font["name"].ToString();
                if (!string.IsNullOrWhiteSpace(font["name_ascii"]?.ToString()))
                    style.Font.NameAscii = font["name_ascii"].ToString();
                if (font["size_pt"] != null)
                    style.Font.Size = font["size_pt"].Value<float>();
                if (font["bold"] != null)
                    style.Font.Bold = font["bold"].Value<bool>() ? 1 : 0;
            }

            if (paragraph != null)
            {
                style.ParagraphFormat.Alignment = ParseAlignment(paragraph["alignment"]?.ToString());
                style.ParagraphFormat.FirstLineIndent = paragraph["first_line_indent_pt"]?.Value<float>() ?? 0f;
                style.ParagraphFormat.LeftIndent = paragraph["left_indent_pt"]?.Value<float>() ?? 0f;
                style.ParagraphFormat.SpaceBefore = paragraph["space_before_pt"]?.Value<float>() ?? 0f;
                style.ParagraphFormat.SpaceAfter = paragraph["space_after_pt"]?.Value<float>() ?? 0f;

                string spacingRule = paragraph["line_spacing_rule"]?.ToString();
                if (string.Equals(spacingRule, "exactly", StringComparison.OrdinalIgnoreCase))
                {
                    style.ParagraphFormat.LineSpacingRule = WdLineSpacing.wdLineSpaceExactly;
                    style.ParagraphFormat.LineSpacing = paragraph["line_spacing_pt"]?.Value<float>() ?? 20f;
                }
            }

            if (outlineLevel.HasValue)
                style.ParagraphFormat.OutlineLevel = ParseOutlineLevel(outlineLevel.Value);
        }

        private static WdParagraphAlignment ParseAlignment(string alignment)
        {
            switch ((alignment ?? "").Trim().ToLowerInvariant())
            {
                case "left": return WdParagraphAlignment.wdAlignParagraphLeft;
                case "center": return WdParagraphAlignment.wdAlignParagraphCenter;
                case "right": return WdParagraphAlignment.wdAlignParagraphRight;
                case "justify":
                default:
                    return WdParagraphAlignment.wdAlignParagraphJustify;
            }
        }

        private static WdOutlineLevel ParseOutlineLevel(int level)
        {
            switch (level)
            {
                case 1: return WdOutlineLevel.wdOutlineLevel1;
                case 2: return WdOutlineLevel.wdOutlineLevel2;
                case 3: return WdOutlineLevel.wdOutlineLevel3;
                case 4: return WdOutlineLevel.wdOutlineLevel4;
                case 5: return WdOutlineLevel.wdOutlineLevel5;
                case 6: return WdOutlineLevel.wdOutlineLevel6;
                default: return WdOutlineLevel.wdOutlineLevelBodyText;
            }
        }

        private static Style TryGetStyle(Document doc, string styleName)
        {
            try { return doc.Styles[styleName] as Style; }
            catch { return null; }
        }

        // ═══════════════════════════════════════════════════════════════
        //  TaskPane 管理
        // ═══════════════════════════════════════════════════════════════

        private void BindInitialTaskPane()
        {
            // 启动时若 CTPFactory 已创建初始 TaskPane，进行窗口绑定
            // 实际绑定在第一次 toggle 时按需进行
        }

        internal WindowPaneContext GetActiveWindowContext()
        {
            try
            {
                var win = _wordApplication?.ActiveWindow;
                if (win == null) return null;
                int hwnd = WordHelper.GetWindowHwnd(win);
                _windowPanes.TryGetValue(hwnd, out var ctx);
                return ctx;
            }
            catch { return null; }
        }

        internal WindowPaneContext GetOrCreatePaneForActiveWindow()
        {
            var win = _wordApplication.ActiveWindow;
            if (win == null)
                throw new InvalidOperationException("没有活动窗口");

            int hwnd = WordHelper.GetWindowHwnd(win);

            if (_windowPanes.TryGetValue(hwnd, out var existing))
            {
                ApplyScaledTaskPaneWidth(existing, win);
                return existing;
            }

            if (_ctpFactory == null)
                throw new InvalidOperationException("CTPFactory 尚未初始化");

            string progId = typeof(TaskPaneHost).FullName;
            _CustomTaskPane ctp;

            try
            {
                ctp = _ctpFactory.CreateCTP(progId, "AI福星", win);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TaskPane] CreateCTP({progId}) 失败: {ex}");
                throw new InvalidOperationException($"创建 TaskPane 失败: {ex.Message}", ex);
            }

            ctp.DockPosition = MsoCTPDockPosition.msoCTPDockPositionRight;
            ctp.Width = ComputeScaledTaskPaneWidth(new IntPtr(hwnd));

            var control = (TaskPaneHost)ctp.ContentControl;
            control.Initialize(this);

            var ctx = new WindowPaneContext(ctp, control);
            _windowPanes[hwnd] = ctx;

            Debug.WriteLine($"[TaskPane] 窗口 0x{hwnd:X} 创建新 TaskPane");
            return ctx;
        }

        private void ApplyScaledTaskPaneWidth(WindowPaneContext ctx, Window win)
        {
            if (ctx == null || win == null) return;

            int hwnd = WordHelper.GetWindowHwnd(win);
            int desiredWidth = ComputeScaledTaskPaneWidth(new IntPtr(hwnd));
            if (ctx.Width != desiredWidth)
                ctx.Width = desiredWidth;
        }

        private static int ComputeScaledTaskPaneWidth(IntPtr hwnd)
        {
            float scale = UiScale.GetScaleForHwnd(hwnd);
            int scaledWidth = UiScale.Scale(420, scale);
            return Math.Max(360, Math.Min(960, scaledWidth));
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  每个 Word 窗口的 TaskPane 上下文
    // ═══════════════════════════════════════════════════════════════

    internal sealed class WindowPaneContext : IDisposable
    {
        public Microsoft.Office.Core._CustomTaskPane CTP { get; }
        public TaskPaneHost Control { get; }

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

        public WindowPaneContext(Microsoft.Office.Core._CustomTaskPane ctp, TaskPaneHost control)
        {
            CTP = ctp;
            Control = control;
        }

        public void Dispose()
        {
            try { Control?.Dispose(); } catch { }
            try { Marshal.ReleaseComObject(CTP); } catch { }
        }
    }
}
