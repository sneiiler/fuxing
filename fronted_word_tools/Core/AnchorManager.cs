using NetOffice.WordApi;
using NetOffice.WordApi.Enums;
using System;
using System.Collections.Generic;
using System.Text;

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

            Log($"Place 开始: label={label}, range=[{range.Start},{range.End})");

            // 如果同名锚点已存在，先移除
            RemoveByTag(doc, tag);

            // 创建 RichText ContentControl
            try
            {
                var cc = doc.ContentControls.Add(
                    WdContentControlType.wdContentControlRichText, range);

                cc.Tag = tag;
                cc.Title = $"⚓ {label}";
                cc.Temporary = true;
                cc.LockContentControl = false;
                cc.LockContents = false;

            // 隐藏外观（不影响文档显示）
            cc.Appearance = WdContentControlAppearance.wdContentControlHidden;

            Log($"Place 成功: label={label}, CC range=[{cc.Range.Start},{cc.Range.End})");

            return BuildAnchorInfo(cc, label);
            }
            catch (Exception ex)
            {
                LogError($"Place CC 创建失败: label={label}, range=[{range.Start},{range.End})", ex);
                DumpAllContentControls(doc, "Place 失败现场");
                throw;
            }
        }

        /// <summary>
        /// 尝试放置锚点。如果 Word 拒绝在此位置创建 RichText CC（如范围与现有控件冲突），
        /// 返回 null 而非抛出异常。
        /// 调用前后临时抑制 Word 弹框，避免"不能在此处应用RTF控件"对话框弹出。
        /// </summary>
        public AnchorInfo TryPlace(Document doc, Range range, string label)
        {
            string selfTag = TagPrefix + label;

            Log($"TryPlace 开始: label={label}, range=[{range.Start},{range.End}), selfTag={selfTag}");

            if (!CanPlaceRichTextControl(doc, range, selfTag))
            {
                Log($"TryPlace 跳过: label={label} — CanPlaceRichTextControl 返回 false");
                DumpAllContentControls(doc, $"TryPlace 跳过 '{label}'");
                return null;
            }

            var app = doc.Application;
            var prevAlerts = app.DisplayAlerts;
            app.DisplayAlerts = WdAlertLevel.wdAlertsNone;
            try
            {
                var result = Place(doc, range, label);
                Log($"TryPlace 成功: label={label}");
                return result;
            }
            catch (Exception ex)
            {
                LogError($"TryPlace CC 创建异常: label={label}, range=[{range.Start},{range.End})", ex);
                DumpAllContentControls(doc, $"TryPlace 异常 '{label}'");
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
        /// 规则：① 嵌套只允许在 RichText/BuildingBlockGallery/Group CC 内部
        /// ② 完全包裹（父包子）允许 ③ 部分交叉禁止
        /// </summary>
        private static bool CanPlaceRichTextControl(Document doc, Range range, string selfTag = null)
        {
            if (doc == null || range == null) return false;
            if (range.Start >= range.End)
            {
                Log($"CanPlace 拒绝: range.Start({range.Start}) >= range.End({range.End})");
                return false;
            }

            int ccCount = doc.ContentControls.Count;
            Log($"CanPlace 检查: 新范围[{range.Start},{range.End}), selfTag={selfTag ?? "(null)"}, 文档CC数={ccCount}");

            foreach (ContentControl existing in doc.ContentControls)
            {
                // 只跳过即将被 RemoveByTag 移除的同名 CC
                string tag = existing.Tag ?? "(null)";
                if (selfTag != null && tag == selfTag)
                {
                    Log($"  跳过同名CC: Tag={tag}");
                    continue;
                }

                var existingRange = existing.Range;
                int eStart = existingRange.Start;
                int eEnd = existingRange.End;
                var ccType = existing.Type;

                // 无重叠 → 安全
                if (range.End <= eStart || range.Start >= eEnd) continue;

                // 新范围完全在现有 CC 内部（嵌套）
                // Word 只允许在 RichText / BuildingBlockGallery / Group CC 内部嵌套
                if (range.Start >= eStart && range.End <= eEnd)
                {
                    if (ccType == WdContentControlType.wdContentControlRichText
                        || ccType == WdContentControlType.wdContentControlBuildingBlockGallery
                        || ccType == WdContentControlType.wdContentControlGroup)
                    {
                        Log($"  允许嵌套: 新范围[{range.Start},{range.End}) ⊂ CC[{eStart},{eEnd}) Type={ccType} Tag={tag}");
                        continue; // 允许嵌套
                    }
                    Log($"  ❌ 拒绝嵌套: 不能在 {ccType} 类型的 CC 内部创建 RichText CC: [{eStart},{eEnd}) Tag={tag}");
                    return false;
                }

                // 新范围完全包含现有 CC（包裹） → Word 允许
                if (range.Start <= eStart && range.End >= eEnd)
                {
                    Log($"  允许包裹: 新范围[{range.Start},{range.End}) ⊃ CC[{eStart},{eEnd}) Type={ccType} Tag={tag}");
                    continue;
                }

                // 部分交叉 → 会触发 RTF 弹框
                Log($"  ❌ 拒绝部分交叉: 新范围[{range.Start},{range.End}) ∩ CC[{eStart},{eEnd}) Type={ccType} Tag={tag}");
                return false;
            }

            Log($"CanPlace 通过: 新范围[{range.Start},{range.End})");
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
                Log($"ClearAll: 已清除 {count} 个锚点");

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

            Log($"RemoveByTag: 已移除 {matches.Count} 个 CC, tag={tag}");
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

        // ═══════════════════════════════════════════════════
        //  日志辅助
        // ═══════════════════════════════════════════════════

        private static void Log(string message)
        {
            DebugLogger.Instance.LogDebug("Anchor", message);
        }

        private static void LogError(string context, Exception ex)
        {
            DebugLogger.Instance.LogError($"[Anchor] {context}", ex);
        }

        /// <summary>
        /// 将文档中所有 ContentControl 的信息转储到日志，用于排查 RTF 冲突。
        /// </summary>
        private static void DumpAllContentControls(Document doc, string reason)
        {
            try
            {
                int count = doc.ContentControls.Count;
                var sb = new StringBuilder();
                sb.AppendLine($"CC 转储（{reason}）: 共 {count} 个 ContentControl");
                for (int i = 1; i <= count; i++)
                {
                    var cc = doc.ContentControls[i];
                    string tag = cc.Tag ?? "(null)";
                    var r = cc.Range;
                    sb.AppendLine($"  [{i}] Type={cc.Type}, Tag={tag}, Range=[{r.Start},{r.End}), " +
                                  $"Title={cc.Title ?? "(null)"}");
                }
                Log(sb.ToString());
            }
            catch (Exception ex)
            {
                Log($"CC 转储失败: {ex.Message}");
            }
        }
    }
}
