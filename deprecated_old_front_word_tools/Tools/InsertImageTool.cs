using Newtonsoft.Json.Linq;
using System.IO;
using NetOffice.WordApi;
using NetOffice.WordApi.Enums;

namespace FuXing
{
    /// <summary>在文档中插入图片</summary>
    public class InsertImageTool : ToolBase
    {
        public override string Name => "insert_image";
        public override string DisplayName => "插入图片";
        public override ToolCategory Category => ToolCategory.Structure;

        public override string Description =>
            "Insert image at cursor with optional proportional scaling. " +
            "Use width_cm/height_cm for easy sizing (set ONE for proportional scale). " +
            "Or use width/height in points. A4 usable width≈16.5cm≈467pt.";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["file_path"] = new JObject { ["type"] = "string", ["description"] = "图片文件绝对路径" },
                ["width_cm"] = new JObject { ["type"] = "number", ["description"] = "宽度（厘米），只设宽则高按比例缩放（优先使用此参数）" },
                ["height_cm"] = new JObject { ["type"] = "number", ["description"] = "高度（厘米），只设高则宽按比例缩放" },
                ["width"] = new JObject { ["type"] = "number", ["description"] = "宽度（磅），只设宽则高按比例缩放" },
                ["height"] = new JObject { ["type"] = "number", ["description"] = "高度（磅），只设高则宽按比例缩放" },
                ["alignment"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray("left", "center", "right"),
                    ["description"] = "段落对齐方式（默认 center）"
                }
            },
            ["required"] = new JArray("file_path")
        };

        public override System.Threading.Tasks.Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            string filePath = RequireString(arguments, "file_path");

            if (!File.Exists(filePath))
                return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Fail($"文件不存在: {filePath}"));

            var app = connect.WordApplication;
            var doc = RequireActiveDocument(connect);
            var sel = app.Selection;

            EnsureNewParagraphIfNeeded(app);

            var insertRange = sel.Range;
            var shape = doc.InlineShapes.AddPicture(filePath, false, true, insertRange);

            float origWidth = shape.Width;
            float origHeight = shape.Height;

            // cm 参数优先于 pt 参数
            const float CmToPoints = 28.3465f;
            float? widthCm = OptionalNullableFloat(arguments, "width_cm");
            float? heightCm = OptionalNullableFloat(arguments, "height_cm");
            float? widthPt = OptionalNullableFloat(arguments, "width");
            float? heightPt = OptionalNullableFloat(arguments, "height");

            float? targetWidth = widthCm.HasValue ? widthCm.Value * CmToPoints
                               : widthPt;
            float? targetHeight = heightCm.HasValue ? heightCm.Value * CmToPoints
                                : heightPt;

            if (targetWidth.HasValue && targetHeight.HasValue)
            {
                shape.Width = targetWidth.Value;
                shape.Height = targetHeight.Value;
            }
            else if (targetWidth.HasValue)
            {
                float newWidth = targetWidth.Value;
                shape.Height = origHeight * (newWidth / origWidth);
                shape.Width = newWidth;
            }
            else if (targetHeight.HasValue)
            {
                float newHeight = targetHeight.Value;
                shape.Width = origWidth * (newHeight / origHeight);
                shape.Height = newHeight;
            }

            string sizeInfo = $"{shape.Width:F0}×{shape.Height:F0} 磅（{shape.Width / CmToPoints:F1}×{shape.Height / CmToPoints:F1} cm）";

            // 清理图片所在段落格式：单倍行距、居中、无缩进，避免当前文本样式污染
            var paraFmt = shape.Range.ParagraphFormat;
            string alignment = OptionalString(arguments, "alignment", "center");
            paraFmt.Alignment = WordHelper.ParseAlignment(alignment);
            paraFmt.LineSpacingRule = NetOffice.WordApi.Enums.WdLineSpacing.wdLineSpaceSingle;
            paraFmt.LeftIndent = 0f;
            paraFmt.RightIndent = 0f;
            paraFmt.FirstLineIndent = 0f;
            paraFmt.SpaceBefore = 0f;
            paraFmt.SpaceAfter = 0f;

            return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Ok(
                $"已插入图片 {Path.GetFileName(filePath)}（{sizeInfo}，{alignment}对齐）"));
        }
    }
}
