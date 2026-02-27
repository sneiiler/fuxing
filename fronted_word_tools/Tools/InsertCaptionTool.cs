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
            "Insert an auto-numbered caption at the cursor or below the selected object.\n" +
            "- label: caption label, e.g. \"图\", \"表\", \"公式\" (Word auto-creates labels that don't exist)\n" +
            "- title: caption title text (appended after auto-number with a space)\n" +
            "- position: above / below (default: below)\n" +
            "- exclude_label: omit label and keep only the number (default: false)";

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
                    ["description"] = "题注位置（默认 below）"
                },
                ["exclude_label"] = new JObject { ["type"] = "boolean", ["description"] = "是否省略标签只保留编号（默认 false）" }
            },
            ["required"] = new JArray("label", "title")
        };

        public override System.Threading.Tasks.Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            string label = arguments["label"]?.ToString();
            string title = arguments["title"]?.ToString();

            if (string.IsNullOrWhiteSpace(label))
                return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Fail("缺少 label 参数"));
            if (title == null)
                return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Fail("缺少 title 参数"));

            string position = arguments["position"]?.ToString() ?? "below";
            bool excludeLabel = arguments["exclude_label"] != null && (bool)arguments["exclude_label"];

            var app = connect.WordApplication;
            var selection = app.Selection;

            EnsureCaptionLabel(app, label);

            var captionPos = position == "above"
                ? WdCaptionPosition.wdCaptionPositionAbove
                : WdCaptionPosition.wdCaptionPositionBelow;

            // 使用位置参数调用 InsertCaption(label, title, titleAutoText, position, excludeLabel)
            selection.InsertCaption(label, " " + title, "", captionPos, excludeLabel ? 1 : 0);

            return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Ok(
                $"已插入题注：{label} X {title}（位置: {position}）"));
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
