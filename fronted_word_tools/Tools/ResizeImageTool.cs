using Newtonsoft.Json.Linq;
using NetOffice.WordApi;
using NetOffice.WordApi.Enums;
using System;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>调整文档中已有图片的尺寸（等比或自由缩放）</summary>
    public class ResizeImageTool : ToolBase
    {
        public override string Name => "resize_image";
        public override string DisplayName => "调整图片尺寸";
        public override ToolCategory Category => ToolCategory.Structure;

        public override string Description =>
            "Resize an existing image. Target: 'selected'(at cursor), 'last', or 1-based index. " +
            "Set ONE of width_cm/height_cm for proportional scaling; set BOTH for free scaling. " +
            "Or use scale_percent for uniform scaling (e.g. 50 = half size). A4 usable width≈16.5cm.";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["target"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "目标图片：'selected'（光标处）、'last'（最后一张）、或 1-based 序号（如 '3'）。默认 'selected'"
                },
                ["width_cm"] = new JObject { ["type"] = "number", ["description"] = "宽度（厘米），只设宽则高按比例缩放" },
                ["height_cm"] = new JObject { ["type"] = "number", ["description"] = "高度（厘米），只设高则宽按比例缩放" },
                ["scale_percent"] = new JObject { ["type"] = "number", ["description"] = "等比缩放百分比（如 50 = 缩小到一半）" }
            },
            ["required"] = new JArray()
        };

        public override Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            var app = connect.WordApplication;
            var doc = RequireActiveDocument(connect);

            // ── 定位目标图片 ──
            string target = OptionalString(arguments, "target", "selected");
            InlineShape shape = FindTargetShape(app, doc, target);
            if (shape == null)
                return Task.FromResult(ToolExecutionResult.Fail($"未找到目标图片: {target}"));

            float origWidth = shape.Width;
            float origHeight = shape.Height;

            if (origWidth <= 0 || origHeight <= 0)
                return Task.FromResult(ToolExecutionResult.Fail(
                    $"图片尺寸异常: {origWidth}×{origHeight} 磅"));

            const float CmToPoints = 28.3465f;

            // ── 参数解析 ──
            float? widthCm = OptionalNullableFloat(arguments, "width_cm");
            float? heightCm = OptionalNullableFloat(arguments, "height_cm");
            float? scalePct = OptionalNullableFloat(arguments, "scale_percent");

            if (!widthCm.HasValue && !heightCm.HasValue && !scalePct.HasValue)
                return Task.FromResult(ToolExecutionResult.Fail(
                    "需要至少指定 width_cm、height_cm 或 scale_percent 之一"));

            float newWidth, newHeight;

            if (scalePct.HasValue)
            {
                // 等比缩放百分比
                if (scalePct.Value <= 0 || scalePct.Value > 1000)
                    return Task.FromResult(ToolExecutionResult.Fail("scale_percent 应在 1~1000 之间"));

                float factor = scalePct.Value / 100f;
                newWidth = origWidth * factor;
                newHeight = origHeight * factor;
            }
            else if (widthCm.HasValue && heightCm.HasValue)
            {
                // 自由缩放（同时指定宽高）
                newWidth = widthCm.Value * CmToPoints;
                newHeight = heightCm.Value * CmToPoints;
            }
            else if (widthCm.HasValue)
            {
                // 按宽度等比缩放
                newWidth = widthCm.Value * CmToPoints;
                newHeight = origHeight * (newWidth / origWidth);
            }
            else
            {
                // 按高度等比缩放
                newHeight = heightCm.Value * CmToPoints;
                newWidth = origWidth * (newHeight / origHeight);
            }

            // ── 先设高度再设宽度（避免 Word 中间态自动调整） ──
            shape.Height = newHeight;
            shape.Width = newWidth;

            string sizeInfo = $"{shape.Width / CmToPoints:F1}×{shape.Height / CmToPoints:F1} cm " +
                              $"({shape.Width:F0}×{shape.Height:F0} 磅)";
            string origInfo = $"{origWidth / CmToPoints:F1}×{origHeight / CmToPoints:F1} cm";

            return Task.FromResult(ToolExecutionResult.Ok(
                $"图片已从 {origInfo} 调整为 {sizeInfo}"));
        }

        /// <summary>根据 target 参数定位 InlineShape</summary>
        private static InlineShape FindTargetShape(Application app, Document doc, string target)
        {
            if (doc.InlineShapes.Count == 0) return null;

            target = (target ?? "selected").Trim().ToLowerInvariant();

            if (target == "last")
                return doc.InlineShapes[doc.InlineShapes.Count];

            if (int.TryParse(target, out int index))
            {
                if (index < 1 || index > doc.InlineShapes.Count) return null;
                return doc.InlineShapes[index];
            }

            // "selected" — 在选区范围内查找
            var sel = app.Selection;
            foreach (InlineShape s in sel.Range.InlineShapes)
            {
                if (s.Type == WdInlineShapeType.wdInlineShapePicture ||
                    s.Type == WdInlineShapeType.wdInlineShapeLinkedPicture)
                    return s;
            }

            // 选区没有图片时，查找选区所在段落的图片
            var para = sel.Range.Paragraphs[1];
            foreach (InlineShape s in para.Range.InlineShapes)
            {
                if (s.Type == WdInlineShapeType.wdInlineShapePicture ||
                    s.Type == WdInlineShapeType.wdInlineShapeLinkedPicture)
                    return s;
            }

            return null;
        }
    }
}
