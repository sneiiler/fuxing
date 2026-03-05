using Newtonsoft.Json.Linq;
using NetOffice.WordApi;
using NetOffice.WordApi.Enums;

namespace FuXing
{
    /// <summary>在当前位置插入题注（图、表、公式等，自动编号）</summary>
    public class InsertCaptionTool : ToolBase
    {
        public override string Name => "insert_caption";
        public override string DisplayName => "插入题注";
        public override ToolCategory Category => ToolCategory.Structure;

        public override string Description =>
            "Insert auto-numbered caption for the nearest image or table. " +
            "Automatically finds the image/table near cursor. " +
            "label: category (图/表/公式 etc.). title: caption text. " +
            "position: above/below (default: below for images, above for tables).";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["label"] = new JObject { ["type"] = "string", ["description"] = "题注标签（如 图、表、公式）" },
                ["title"] = new JObject { ["type"] = "string", ["description"] = "题注标题文本" },
                ["position"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray("above", "below"),
                    ["description"] = "题注位置（默认：图片 below，表格 above）"
                },
                ["exclude_label"] = new JObject { ["type"] = "boolean", ["description"] = "是否省略标签只保留编号（默认 false）" }
            },
            ["required"] = new JArray("label", "title")
        };

        public override System.Threading.Tasks.Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            string label = RequireString(arguments, "label");
            string title = RequireString(arguments, "title");

            string positionArg = OptionalString(arguments, "position");
            bool excludeLabel = OptionalBool(arguments, "exclude_label", false);

            var app = connect.WordApplication;
            var doc = RequireActiveDocument(connect);
            var sel = app.Selection;

            EnsureCaptionLabel(app, label);

            // ── 检测选区附近的可题注对象 ──
            InlineShape targetImage = FindNearestInlineShape(sel);
            Table targetTable = (targetImage == null) ? FindNearestTable(sel) : null;

            // ── 智能默认位置：图片 below，表格 above ──
            string position;
            if (positionArg != null)
                position = positionArg;
            else if (targetTable != null)
                position = "above";
            else
                position = "below";

            int excludeLabelInt = excludeLabel ? 1 : 0;

            // ── 根据目标对象类型和位置，用可靠方式插入题注 ──
            if (targetImage != null)
            {
                InsertCaptionForImage(app, doc, targetImage, label, title, position, excludeLabelInt);
            }
            else if (targetTable != null)
            {
                InsertCaptionForTable(app, doc, targetTable, label, title, position, excludeLabelInt);
            }
            else
            {
                InsertCaptionAtCursor(app, label, title, position, excludeLabelInt);
            }

            // ── 确保题注段落前后干净隔离 ──
            EnsureCaptionParagraphIsolation(app, doc);

            string targetDesc = targetImage != null ? "图片" : targetTable != null ? "表格" : "光标";
            return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Ok(
                $"已插入题注：{label} X {title}（位置: {position}，目标: {targetDesc}）"));
        }

        /// <summary>
        /// 为图片插入题注。
        /// Word 的 InsertCaption(wdCaptionPositionBelow) 对 InlineShape 不可靠，
        /// 改用：定位到图片所在段落之后，用 wdCaptionPositionAbove 在下一段前面插入。
        /// </summary>
        private static void InsertCaptionForImage(Application app, Document doc,
            InlineShape image, string label, string title, string position, int excludeLabel)
        {
            var imgPara = image.Range.Paragraphs[1];

            if (position == "below")
            {
                // 定位到图片段落末尾之后（即下一段的开头）
                int afterImgPos = imgPara.Range.End;
                var nextRange = doc.Range(afterImgPos, afterImgPos);
                nextRange.Select();
                // "above" = 在当前位置前插入 = 图片段落之后
                app.Selection.InsertCaption(label, " " + title, "",
                    WdCaptionPosition.wdCaptionPositionAbove, excludeLabel);
            }
            else
            {
                // above：直接在图片上方插入，选中图片后用标准 API
                image.Select();
                app.Selection.InsertCaption(label, " " + title, "",
                    WdCaptionPosition.wdCaptionPositionAbove, excludeLabel);
            }
        }

        /// <summary>
        /// 为表格插入题注。
        /// 表格 above：定位到表格第一个段落前面。
        /// 表格 below：定位到表格最后一个段落之后。
        /// </summary>
        private static void InsertCaptionForTable(Application app, Document doc,
            Table table, string label, string title, string position, int excludeLabel)
        {
            if (position == "above")
            {
                // 定位到表格起始位置
                var tableStart = table.Range.Start;
                var beforeRange = doc.Range(tableStart, tableStart);
                beforeRange.Select();
                app.Selection.InsertCaption(label, " " + title, "",
                    WdCaptionPosition.wdCaptionPositionAbove, excludeLabel);
            }
            else
            {
                // below：定位到表格末尾之后
                var tableEnd = table.Range.End;
                var afterRange = doc.Range(tableEnd, tableEnd);
                afterRange.Select();
                app.Selection.InsertCaption(label, " " + title, "",
                    WdCaptionPosition.wdCaptionPositionAbove, excludeLabel);
            }
        }

        /// <summary>光标处插入题注（无明确目标对象时的回退）</summary>
        private static void InsertCaptionAtCursor(Application app,
            string label, string title, string position, int excludeLabel)
        {
            EnsureNewParagraphIfNeeded(app);
            var captionPos = position == "above"
                ? WdCaptionPosition.wdCaptionPositionAbove
                : WdCaptionPosition.wdCaptionPositionBelow;
            app.Selection.InsertCaption(label, " " + title, "", captionPos, excludeLabel);
        }

        /// <summary>
        /// 确保题注段落是独立的，前后不会与相邻内容粘连。
        /// 检查题注段落的文本是否包含非题注内容（粘连），若有则插入段落分隔。
        /// </summary>
        private static void EnsureCaptionParagraphIsolation(Application app, Document doc)
        {
            var sel = app.Selection;
            var captionPara = sel.Range.Paragraphs[1];
            string rawText = captionPara.Range.Text ?? "";
            string captionText = rawText.TrimEnd('\r', '\n', '\a');

            // 如果题注段落文本过长（正常题注不应超过 200 字符），说明可能有粘连
            if (captionText.Length > 200)
            {
                // 在题注域标记（SEQ 字段）之后查找第一个合理的分割点
                // 题注格式一般是 "标签 编号 标题"，之后如果还有大量文本就是粘连了
            }

            // 更可靠的方式：确保题注段落后面有段落分隔符
            try
            {
                int paraEnd = captionPara.Range.End;
                // 如果段落结尾在文档范围内，检查下一个字符
                if (paraEnd < doc.Content.End)
                {
                    var checkRange = doc.Range(paraEnd - 1, paraEnd);
                    string endChar = checkRange.Text ?? "";
                    // 段落末尾应该是 \r（段落标记）
                    if (endChar != "\r" && endChar != "\n")
                    {
                        // 段落没有正确终止，在末尾插入段落标记
                        var insertRange = doc.Range(paraEnd, paraEnd);
                        insertRange.InsertParagraphAfter();
                    }
                }
            }
            catch { /* 忽略边界情况 */ }
        }

        /// <summary>在选区所在段落或相邻段落中查找 InlineShape</summary>
        private static InlineShape FindNearestInlineShape(Selection sel)
        {
            if (sel.InlineShapes.Count > 0)
                return sel.InlineShapes[1];

            var para = sel.Range.Paragraphs[1];
            if (para.Range.InlineShapes.Count > 0)
                return para.Range.InlineShapes[1];

            // 检查上一段（光标可能在图片下方空行）
            try
            {
                var prevPara = para.Previous();
                if (prevPara != null && prevPara.Range.InlineShapes.Count > 0)
                    return prevPara.Range.InlineShapes[1];
            }
            catch { /* 如果 Previous() 不可用则忽略 */ }

            return null;
        }

        /// <summary>检测选区是否在表格内</summary>
        private static Table FindNearestTable(Selection sel)
        {
            if (sel.Tables.Count > 0)
                return sel.Tables[1];
            return null;
        }

        private void EnsureCaptionLabel(Application app, string label)
        {
            try
            {
                foreach (CaptionLabel cl in app.CaptionLabels)
                {
                    if (cl.Name == label) return;
                }
                app.CaptionLabels.Add(label);
            }
            catch
            {
                // 某些内置标签无法通过名称匹配，忽略
            }
        }
    }
}
