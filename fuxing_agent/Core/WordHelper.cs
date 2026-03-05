using System;
using System.Runtime.InteropServices;
using Microsoft.Office.Interop.Word;
using System.Drawing;

namespace FuXingAgent.Core
{
    /// <summary>
    /// Word COM 操作辅助方法，封装常用的 Interop 调用
    /// </summary>
    public static class WordHelper
    {
        /// <summary>安全释放 COM 对象</summary>
        public static void ReleaseCom(object comObj)
        {
            if (comObj != null)
            {
                try { Marshal.ReleaseComObject(comObj); }
                catch { }
            }
        }

        /// <summary>安全获取窗口句柄</summary>
        public static int GetWindowHwnd(Window window)
        {
            if (window == null) return 0;
            try { return window.Hwnd; }
            catch
            {
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

        /// <summary>进入修订追踪模式，将作者设为"AI福星"</summary>
        public static TrackRevisionsScope BeginTrackRevisions(Application app)
        {
            return new TrackRevisionsScope(app);
        }

        /// <summary>给指定范围设置浅色底色</summary>
        public static void HighlightRange(Document doc, int start, int end)
        {
            try
            {
                var range = doc.Range(start, end);
                range.Shading.BackgroundPatternColor = (WdColor)(250 * 65536 + 240 * 256 + 228);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"设置高亮失败: {ex.Message}");
            }
        }

        /// <summary>清除指定范围的底色</summary>
        public static void ClearHighlight(Document doc, int start, int end)
        {
            try
            {
                var range = doc.Range(start, end);
                range.Shading.BackgroundPatternColor = WdColor.wdColorAutomatic;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"清除高亮失败: {ex.Message}");
            }
        }

        public static WdParagraphAlignment ParseAlignment(string alignment)
        {
            switch ((alignment ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "left": return WdParagraphAlignment.wdAlignParagraphLeft;
                case "center": return WdParagraphAlignment.wdAlignParagraphCenter;
                case "right": return WdParagraphAlignment.wdAlignParagraphRight;
                case "justify":
                default:
                    return WdParagraphAlignment.wdAlignParagraphJustify;
            }
        }

        public static WdColor ParseHexColor(string color)
        {
            if (string.IsNullOrWhiteSpace(color))
                return WdColor.wdColorAutomatic;

            string value = color.Trim();
            if (value.StartsWith("#"))
                value = value.Substring(1);

            if (value.Length != 6)
                return WdColor.wdColorAutomatic;

            try
            {
                int r = Convert.ToInt32(value.Substring(0, 2), 16);
                int g = Convert.ToInt32(value.Substring(2, 2), 16);
                int b = Convert.ToInt32(value.Substring(4, 2), 16);
                int ole = ColorTranslator.ToOle(Color.FromArgb(r, g, b));
                return (WdColor)ole;
            }
            catch
            {
                return WdColor.wdColorAutomatic;
            }
        }
    }

    /// <summary>修订追踪作用域 — 自动设置/恢复 TrackRevisions 和用户名</summary>
    public sealed class TrackRevisionsScope : IDisposable
    {
        private readonly Application _app;
        private readonly string _savedUserName;
        private readonly string _savedUserInitials;
        private readonly bool _wasTracking;

        public TrackRevisionsScope(Application app)
        {
            _app = app;
            _savedUserName = app.UserName;
            _savedUserInitials = app.UserInitials;
            _wasTracking = app.ActiveDocument.TrackRevisions;

            app.UserName = "AI福星";
            app.UserInitials = "AI";
            app.ActiveDocument.TrackRevisions = true;
        }

        public void Dispose()
        {
            try
            {
                _app.ActiveDocument.TrackRevisions = _wasTracking;
                _app.UserName = _savedUserName;
                _app.UserInitials = _savedUserInitials;
            }
            catch { }
        }
    }
}
