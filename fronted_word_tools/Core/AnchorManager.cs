using NetOffice.WordApi;
using NetOffice.WordApi.Enums;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace FuXing.Core
{
    // ═══════════════════════════════════════════════════════════════
    //  文档锚点管理器
    //
    //  使用 Word ContentControl（RichText 类型, 隐形外观）作为稳定的
    //  位置标记。锚点随文档编辑自动跟踪位置，不受字符偏移漂移影响。
    //
    //  核心设计：
    //  - Temporary=true  → 文档关闭时自动清除，零残留
    //  - Appearance=Hidden → 用户完全不可见
    //  - Tag 格式 "fxg:{label}" → 通过 SelectContentControlsByTag 快速查找
    //  - 同一 label 不允许重复 → Place 时如已存在则先 Remove
    // ═══════════════════════════════════════════════════════════════

    /// <summary>锚点信息</summary>
    public class AnchorInfo
    {
        /// <summary>锚点名称</summary>
        public string Label { get; set; }

        /// <summary>当前字符偏移起始</summary>
        public int CharStart { get; set; }

        /// <summary>当前字符偏移结束</summary>
        public int CharEnd { get; set; }

        /// <summary>内容前 100 字符预览</summary>
        public string TextPreview { get; set; }
    }

    /// <summary>
    /// 文档锚点管理器。
    /// 使用 Word ContentControl 作为稳定的位置标记，
    /// 锚点随文档编辑自动跟踪位置。
    /// </summary>
    public class AnchorManager
    {
        /// <summary>Tag 前缀，所有福星锚点的 CC Tag 以此开头</summary>
        public const string TagPrefix = "fxg:";

        /// <summary>文本预览最大长度</summary>
        private const int PreviewMaxLength = 100;

        /// <summary>
        /// 在指定 Range 上放置一个命名锚点。
        /// 创建一个隐形的 RichText ContentControl，用 Tag 标识。
        /// 如果同名锚点已存在，先移除再创建。
        /// </summary>
        /// <param name="doc">目标文档</param>
        /// <param name="range">要锚定的范围</param>
        /// <param name="label">锚点名称（如 "src", "ch3-table"）</param>
        /// <returns>锚点信息</returns>
        public AnchorInfo Place(Document doc, Range range, string label)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (range == null) throw new ArgumentNullException(nameof(range));
            if (string.IsNullOrWhiteSpace(label))
                throw new ArgumentException("锚点名称不能为空", nameof(label));

            string tag = TagPrefix + label;

            // 如果同名锚点已存在，先移除
            RemoveByTag(doc, tag);

            // 创建 RichText ContentControl
            var cc = doc.ContentControls.Add(
                WdContentControlType.wdContentControlRichText, range);

            cc.Tag = tag;
            cc.Title = $"⚓ {label}";
            cc.Temporary = true;
            cc.LockContentControl = false;
            cc.LockContents = false;

            // 隐藏外观（不影响文档显示）
            cc.Appearance = WdContentControlAppearance.wdContentControlHidden;

            Debug.WriteLine($"[AnchorManager] 已放置锚点: {label} (Tag={tag})");

            return BuildAnchorInfo(cc, label);
        }

        /// <summary>
        /// 尝试放置锚点。如果 Word 拒绝在此位置创建 RichText CC（如范围与现有控件冲突），
        /// 返回 null 而非抛出异常。
        /// 调用前后临时抑制 Word 弹框，避免"不能在此处应用RTF控件"对话框弹出。
        /// </summary>
        public AnchorInfo TryPlace(Document doc, Range range, string label)
        {
            if (!CanPlaceRichTextControl(doc, range))
            {
                Debug.WriteLine($"[AnchorManager] 跳过锚点 '{label}'：范围不适合创建 RichText ContentControl");
                return null;
            }

            var app = doc.Application;
            var prevAlerts = app.DisplayAlerts;
            app.DisplayAlerts = WdAlertLevel.wdAlertsNone;
            try
            {
                return Place(doc, range, label);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AnchorManager] 无法放置锚点 '{label}': {ex.Message}");
                return null;
            }
            finally
            {
                app.DisplayAlerts = prevAlerts;
            }
        }

        /// <summary>
        /// 预判范围是否适合创建 RichText ContentControl。
        /// 避免调用 Word API 后弹出"不能在此处应用RTF控件"对话框。
        /// Word 允许完全嵌套（子在父内）和完全包裹（父包子），但禁止部分交叉。
        /// </summary>
        private static bool CanPlaceRichTextControl(Document doc, Range range)
        {
            if (doc == null || range == null) return false;
            if (range.Start >= range.End) return false;

            foreach (ContentControl existing in doc.ContentControls)
            {
                // 跳过我们自己的锚点 CC（Place 会通过 RemoveByTag 处理同名的）
                string tag = existing.Tag;
                if (tag != null && tag.StartsWith(TagPrefix)) continue;

                var existingRange = existing.Range;
                int eStart = existingRange.Start;
                int eEnd = existingRange.End;

                // 无重叠  安全
                if (range.End <= eStart || range.Start >= eEnd) continue;

                // 新范围完全在现有 CC 内部（嵌套）  Word 允许
                if (range.Start >= eStart && range.End <= eEnd) continue;

                // 新范围完全包含现有 CC（包裹）  Word 允许
                if (range.Start <= eStart && range.End >= eEnd) continue;

                // 部分交叉  会触发 RTF 弹框
                Debug.WriteLine($"[AnchorManager] 检测到部分交叉：新范围[{range.Start},{range.End}) vs 现有CC[{eStart},{eEnd})");
                return false;
            }

            return true;
        }
        /// <summary>
        /// 按名称获取锚点的当前 Range。
        /// ContentControl 会自动跟踪文档编辑，返回的 Range 始终是正确位置。
        /// </summary>
        public Range GetRange(Document doc, string label)
        {
            var cc = FindByLabel(doc, label);
            if (cc == null)
                throw new InvalidOperationException($"锚点不存在: {label}");

            return cc.Range;
        }

        /// <summary>获取锚点信息</summary>
        public AnchorInfo Get(Document doc, string label)
        {
            var cc = FindByLabel(doc, label);
            if (cc == null) return null;

            return BuildAnchorInfo(cc, label);
        }

        /// <summary>
        /// 移除锚点但保留其包裹的内容。
        /// CC.Delete(false) = 删除容器，保留内容。
        /// </summary>
        public bool Remove(Document doc, string label)
        {
            string tag = TagPrefix + label;
            return RemoveByTag(doc, tag);
        }

        /// <summary>列出当前文档中所有福星锚点</summary>
        public List<AnchorInfo> List(Document doc)
        {
            var result = new List<AnchorInfo>();

            foreach (ContentControl cc in doc.ContentControls)
            {
                string tag = cc.Tag;
                if (tag != null && tag.StartsWith(TagPrefix))
                {
                    string label = tag.Substring(TagPrefix.Length);
                    result.Add(BuildAnchorInfo(cc, label));
                }
            }

            return result;
        }

        /// <summary>清除文档中所有福星锚点（保留内容）</summary>
        public int ClearAll(Document doc)
        {
            int count = 0;
            // 逆序遍历，因为删除会影响索引
            for (int i = doc.ContentControls.Count; i >= 1; i--)
            {
                var cc = doc.ContentControls[i];
                if (cc.Tag != null && cc.Tag.StartsWith(TagPrefix))
                {
                    cc.Delete(false);
                    count++;
                }
            }

            if (count > 0)
                Debug.WriteLine($"[AnchorManager] 已清除 {count} 个锚点");

            return count;
        }

        /// <summary>检查是否存在指定锚点</summary>
        public bool Exists(Document doc, string label)
        {
            return FindByLabel(doc, label) != null;
        }

        // ═══════════════════════════════════════════════════
        //  内部方法
        // ═══════════════════════════════════════════════════

        /// <summary>按 label 查找 ContentControl</summary>
        private ContentControl FindByLabel(Document doc, string label)
        {
            string tag = TagPrefix + label;
            var matches = doc.SelectContentControlsByTag(tag);
            if (matches.Count > 0)
                return matches[1]; // ContentControls 是 1-based

            return null;
        }

        /// <summary>按 Tag 移除 CC（保留内容）</summary>
        private bool RemoveByTag(Document doc, string tag)
        {
            var matches = doc.SelectContentControlsByTag(tag);
            if (matches.Count == 0) return false;

            // 可能有多个同 Tag 的 CC（理论上不应该，但防御性删除所有）
            for (int i = matches.Count; i >= 1; i--)
                matches[i].Delete(false);

            Debug.WriteLine($"[AnchorManager] 已移除锚点: {tag}");
            return true;
        }

        /// <summary>从 CC 构建 AnchorInfo</summary>
        private static AnchorInfo BuildAnchorInfo(ContentControl cc, string label)
        {
            var range = cc.Range;
            string text = range.Text ?? "";
            if (text.Length > PreviewMaxLength)
                text = text.Substring(0, PreviewMaxLength) + "…";

            return new AnchorInfo
            {
                Label = label,
                CharStart = range.Start,
                CharEnd = range.End,
                TextPreview = text
            };
        }
    }
}
