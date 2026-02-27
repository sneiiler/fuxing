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
            "Insert an image at the cursor position.\n" +
            "- file_path: absolute path to the image file (required)\n" +
            "- width / height: dimensions in points; set one for proportional scaling, omit both for original size\n" +
            "- alignment: paragraph alignment (left/center/right), default: center\n" +
            "- Note: 1cm ≈ 28.35pt, A4 usable width ≈ 467pt";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["file_path"] = new JObject { ["type"] = "string", ["description"] = "图片文件绝对路径" },
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
            string filePath = arguments["file_path"]?.ToString();
            if (string.IsNullOrWhiteSpace(filePath))
                return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Fail("缺少 file_path 参数"));

            if (!File.Exists(filePath))
                return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Fail($"文件不存在: {filePath}"));

            var app = connect.WordApplication;
            var doc = app.ActiveDocument;
            var range = app.Selection.Range;

            var shape = doc.InlineShapes.AddPicture(filePath, false, true, range);

            float origWidth = shape.Width;
            float origHeight = shape.Height;

            bool hasWidth = arguments["width"] != null;
            bool hasHeight = arguments["height"] != null;

            if (hasWidth && hasHeight)
            {
                shape.Width = (float)arguments["width"];
                shape.Height = (float)arguments["height"];
            }
            else if (hasWidth)
            {
                float newWidth = (float)arguments["width"];
                shape.Height = origHeight * (newWidth / origWidth);
                shape.Width = newWidth;
            }
            else if (hasHeight)
            {
                float newHeight = (float)arguments["height"];
                shape.Width = origWidth * (newHeight / origHeight);
                shape.Height = newHeight;
            }

            string alignment = arguments["alignment"]?.ToString() ?? "center";
            shape.Range.ParagraphFormat.Alignment = WordHelper.ParseAlignment(alignment);

            return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Ok(
                $"已插入图片 {Path.GetFileName(filePath)}（{shape.Width:F0}×{shape.Height:F0} 磅，{alignment}对齐）"));
        }
    }
}
