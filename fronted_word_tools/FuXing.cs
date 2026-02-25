using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using System.Text;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Linq;
using NetOffice;
using NetOffice.Tools;
using NetOffice.WordApi;
using NetOffice.WordApi.Tools;
using NetOffice.OfficeApi.Enums;


namespace FuXing
{
    // MyTaskPane 类定义
    public partial class MyTaskPane : UserControl, NetOffice.WordApi.Tools.ITaskPane
    {
        private Connect ParentAddin { get; set; }

        public MyTaskPane()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Name = "MyTaskPane";
            this.Size = new System.Drawing.Size(400, 607);
            this.BackColor = System.Drawing.Color.White;
            this.ResumeLayout(false);
        }

        #region ITaskPane Implementation

        public void OnConnection(NetOffice.WordApi.Application application, NetOffice.OfficeApi._CustomTaskPane definition, object[] customArguments)
        {
            if (customArguments.Length > 0)
                ParentAddin = customArguments[0] as Connect;
        }

        public void OnDisconnection()
        {
        }

        public void OnDockPositionChanged(NetOffice.OfficeApi.Enums.MsoCTPDockPosition position)
        {
        }

        public void OnVisibleStateChanged(bool visible)
        {
            if (null != ParentAddin && null != ParentAddin.RibbonUI)
                ParentAddin.RibbonUI.InvalidateControl("togglePaneVisibleButton");
        }

        #endregion
    }

    [ComVisible(true)]
    [Guid("C9F68F90-E8C4-4A8B-9A8B-5E6F7D8E9F0A")]
    [ProgId("FuXing.Connect")]
    [RegistryLocation(RegistrySaveLocation.CurrentUser), CustomUI("FuXing.RibbonUI.xml"), CustomPane(typeof(TaskPaneControl), "AI助手", false, PaneDockPosition.msoCTPDockPositionRight)]
	
    public class Connect : NetOffice.WordApi.Tools.COMAddin
    {
        private NetOffice.WordApi.Application _wordApplication;
        private NetWorkHelper _networkHelper;
        private ConfigLoader _configLoader;
        private NetOffice.OfficeApi._CustomTaskPane _taskPane; // 恢复原生TaskPane
        private TaskPaneControl _taskPaneControl;

        // 静态实例引用，供TaskPane控件调用
        public static Connect CurrentInstance { get; private set; }

        // RibbonUI 属性
        internal new NetOffice.OfficeApi.IRibbonUI RibbonUI { get; private set; }

        public Connect()
        {
            CurrentInstance = this;
            OnStartupComplete += Connect_OnStartupComplete;
            OnDisconnection += Connect_OnDisconnection;
        }

        private void Connect_OnStartupComplete(ref Array custom)
        {
            try
            {
                _wordApplication = (NetOffice.WordApi.Application)Application;
                _networkHelper = new NetWorkHelper();
                _configLoader = new ConfigLoader();

                // 确保资源文件复制到输出目录
                ResourceManager.CopyResourcesToOutput();

                // 测试：输出资源状态
                System.Diagnostics.Debug.WriteLine("=== 福星 资源状态检查 ===");
                System.Diagnostics.Debug.WriteLine(ResourceManager.GetResourceStatus());
                System.Diagnostics.Debug.WriteLine("================================");

                System.Diagnostics.Debug.WriteLine($"Word加载插件版本 {Application.Version}");
                System.Diagnostics.Debug.WriteLine($"TaskPanes 数量: {TaskPanes.Count}");
                
                // 使用CustomPane特性时，任务窗格会自动创建，不需要手动创建
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
                
                // 释放TaskPane资源
                if (_taskPane != null)
                {
                    _taskPane.Dispose();
                    _taskPane = null;
                }

                if (_taskPaneControl != null)
                {
                    _taskPaneControl.Dispose();
                    _taskPaneControl = null;
                }

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
                case "ai_text_correction_all_btn": return "icons8-spellcheck-all-100.png";
                case "FormatTableStyleButton": return "icons8-table_single-96.png";
                case "table_all_style_format_btn": return "icons8-table_all-100.png";
                case "toggle_taskpane_btn": return "icons8-deepseek-150.png"; // 使用deepseek图标表示AI助手
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
                _wordApplication.StatusBar = "正在校验符合标准...";

                var selection = _wordApplication.Selection;
                if (string.IsNullOrEmpty(selection.Text?.Trim()))
                {
                    MessageBox.Show("请先选择要校验的文本", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string searchInfo = "";
                string check_result = "";

                var sb = new StringBuilder();
                string[] lines = selection.Text.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    var cleanedLine = Regex.Replace(line, @"[^\w\s\u4e00-\u9fa5]", "").Trim();
                    sb.AppendLine(cleanedLine);
                }
                searchInfo = sb.ToString();

                if (!string.IsNullOrEmpty(searchInfo))
                {
                    check_result = _networkHelper.SendStandardCheckRequest(searchInfo);
                }

                var paragraphs = selection.Paragraphs;
                if (paragraphs.Count > 0)
                {
                    var firstParagraph = paragraphs[1];
                    var lastWordRange = firstParagraph.Range.Duplicate;
                    lastWordRange.Start = firstParagraph.Range.End - 2;
                    lastWordRange.End = firstParagraph.Range.End;

                    AddCommentWithCustomUserName(lastWordRange, check_result);
                }

                _wordApplication.StatusBar = "标准校验完成！";
            }
            catch (Exception ex)
            {
                MessageBox.Show("校验符合标准时错误: " + ex.Message, "福星错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                _wordApplication.StatusBar = "校验失败！";
            }
        }

        public void ai_text_correction_btn_Click(NetOffice.OfficeApi.IRibbonControl control)
        {
            try
            {
                _wordApplication.StatusBar = "正在进行AI文本纠错...";

                var selection = _wordApplication.Selection;
                if (string.IsNullOrEmpty(selection.Text?.Trim()))
                {
                    MessageBox.Show("请先选择要纠错的文本", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string originalText = selection.Text;
                string correctedText = _networkHelper.SendTextCorrectionRequest(originalText);

                if (correctedText != originalText && !correctedText.StartsWith("文本纠错失败"))
                {
                    selection.Text = correctedText;
                    MessageBox.Show("文本纠错完成！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(correctedText, "纠错结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                _wordApplication.StatusBar = "文本纠错完成！";
            }
            catch (Exception ex)
            {
                MessageBox.Show("AI文本纠错时错误: " + ex.Message, "福星错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                _wordApplication.StatusBar = "纠错失败！";
            }
        }

        public void ai_text_correction_all_btn_Click(NetOffice.OfficeApi.IRibbonControl control)
        {
            try
            {
                _wordApplication.StatusBar = "正在进行全文AI纠错...";

                if (_wordApplication.Documents.Count == 0)
                {
                    MessageBox.Show("没有打开的文档", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var activeDoc = _wordApplication.ActiveDocument;
                string originalText = activeDoc.Content.Text;

                if (string.IsNullOrEmpty(originalText.Trim()))
                {
                    MessageBox.Show("文档内容为空", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string correctedText = _networkHelper.SendTextCorrectionRequest(originalText);

                if (correctedText != originalText && !correctedText.StartsWith("文本纠错失败"))
                {
                    activeDoc.Content.Text = correctedText;
                    MessageBox.Show("全文纠错完成！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(correctedText, "纠错结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                _wordApplication.StatusBar = "全文纠错完成！";
            }
            catch (Exception ex)
            {
                MessageBox.Show("全文AI纠错时错误: " + ex.Message, "福星错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                _wordApplication.StatusBar = "全文纠错失败！";
            }
        }

        public void FormatTableStyleButton_Click(NetOffice.OfficeApi.IRibbonControl control)
        {
            try
            {
                _wordApplication.StatusBar = "正在格式化表格...";

                var selection = _wordApplication.Selection;
                if (selection.Tables.Count == 0)
                {
                    MessageBox.Show("请将光标放在表格内", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var table = selection.Tables[1];
                FormatTable(table);

                _wordApplication.StatusBar = "表格格式化完成！";
                MessageBox.Show("表格格式化完成！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("格式化表格时错误: " + ex.Message, "福星错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                _wordApplication.StatusBar = "表格格式化失败！";
            }
        }

        public void table_all_style_format_btn_Click(NetOffice.OfficeApi.IRibbonControl control)
        {
            try
            {
                _wordApplication.StatusBar = "正在格式化所有表格...";

                if (_wordApplication.Documents.Count == 0)
                {
                    MessageBox.Show("没有打开的文档", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var activeDoc = _wordApplication.ActiveDocument;
                int tableCount = activeDoc.Tables.Count;

                if (tableCount == 0)
                {
                    MessageBox.Show("文档中没有表格", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                for (int i = 1; i <= tableCount; i++)
                {
                    FormatTable(activeDoc.Tables[i]);
                }

                _wordApplication.StatusBar = "所有表格格式化完成！";
                MessageBox.Show($"已格式化 {tableCount} 个表格", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("格式化所有表格时错误: " + ex.Message, "福星错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                _wordApplication.StatusBar = "表格格式化失败！";
            }
        }

        public void toggle_taskpane_btn_Click(NetOffice.OfficeApi.IRibbonControl control)
        {
            try
            {
                // 使用CustomPane特性自动创建的TaskPane
                if (TaskPanes.Count > 0)
                {
                    var taskPane = TaskPanes[0];
                    
                    // 设置任务面板宽度
                    if (taskPane.Visible == false)  // 如果即将显示面板，先设置宽度
                    {
                        taskPane.Width = 500;
                        System.Diagnostics.Debug.WriteLine($"TaskPane width set to: {taskPane.Width}");
                    }
                    
                    taskPane.Visible = !taskPane.Visible;
                    System.Diagnostics.Debug.WriteLine($"TaskPane可见性切换为: {taskPane.Visible}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("TaskPane未找到，可能初始化失败");
                    throw new InvalidOperationException("TaskPane未正确初始化");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("原生TaskPane操作失败: " + ex.Message, "福星错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                System.Diagnostics.Debug.WriteLine($"原生TaskPane操作失败: {ex.Message}");
            }
        }

        public void setting_btn_Click(NetOffice.OfficeApi.IRibbonControl control)
        {
            try
            {
                SettingForm settingForm = new SettingForm();
                settingForm.OnConfigUpdated += () => { _networkHelper = new NetWorkHelper(); };
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
                // 按住Ctrl键可显示图标资源信息
                if (Control.ModifierKeys == Keys.Control)
                {
                    IconTestForm.ShowTest();
                    return;
                }

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
                _wordApplication.UserName = "AI助手";
                _wordApplication.UserInitials = "AI助手";
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
            table.Borders.Enable = 1;
            table.Borders.OutsideLineStyle = NetOffice.WordApi.Enums.WdLineStyle.wdLineStyleSingle;
            table.Borders.InsideLineStyle = NetOffice.WordApi.Enums.WdLineStyle.wdLineStyleSingle;
            table.Borders.OutsideLineWidth = NetOffice.WordApi.Enums.WdLineWidth.wdLineWidth075pt;
            table.Borders.InsideLineWidth = NetOffice.WordApi.Enums.WdLineWidth.wdLineWidth050pt;

            table.Range.ParagraphFormat.Alignment = NetOffice.WordApi.Enums.WdParagraphAlignment.wdAlignParagraphCenter;

            table.Range.Font.Name = "宋体";
            table.Range.Font.Size = 12;

            table.Rows.HeightRule = NetOffice.WordApi.Enums.WdRowHeightRule.wdRowHeightAtLeast;
            table.Rows.Height = 20;

            if (table.Rows.Count > 0)
            {
                var headerRow = table.Rows[1];
                headerRow.Range.Font.Bold = 1;
                headerRow.Shading.BackgroundPatternColor = NetOffice.WordApi.Enums.WdColor.wdColorGray15;
            }
        }

        #endregion

        #region COM 注册方法

        [ComRegisterFunction]
        public new static void RegisterFunction(Type type)
        {
            try
            {
                Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Office\Word\Addins\" + type.FullName);
                key.SetValue("Description", "福星插件 - AI文本纠错、标准校验、表格格式化");
                key.SetValue("FriendlyName", "福星");
                key.SetValue("LoadBehavior", 3);
                key.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("注册时错误: " + ex.Message, "福星注册错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        [ComUnregisterFunction]
        public new static void UnregisterFunction(Type type)
        {
            try
            {
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKey(
                    @"Software\Microsoft\Office\Word\Addins\" + type.FullName, false);
            }
            catch (Exception ex)
            {
                MessageBox.Show("卸载插件时错误: " + ex.Message, "福星卸载错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #endregion

        #region Ribbon UI 回调方法

        public void OnLoadRibbonUI(NetOffice.OfficeApi.IRibbonUI ribbonUI)
        {
            RibbonUI = ribbonUI;
        }

        public bool TogglePaneVisibleButton_GetPressed(NetOffice.OfficeApi.IRibbonControl control)
        {
            return TaskPanes.Count > 0 ? TaskPanes[0].Visible : false;
        }

        public void TogglePaneVisibleButton_Click(NetOffice.OfficeApi.IRibbonControl control, bool pressed)
        {
            if (TaskPanes.Count > 0)
                TaskPanes[0].Visible = pressed;
        }

        public void AboutButton_Click(NetOffice.OfficeApi.IRibbonControl control)
        {
            MessageBox.Show(String.Format("福星 Version {0}", this.GetType().Assembly.GetName().Version),
                "About 福星", MessageBoxButtons.OK, MessageBoxIcon.Information);
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