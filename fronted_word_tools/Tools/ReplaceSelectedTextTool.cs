using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>替换选中的文本</summary>
    public class ReplaceSelectedTextTool : ITool
    {
        public string Name => "replace_selected_text";

        public string Description =>
            "将 Word 文档中当前选中的文本替换为指定内容。";

        public JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["new_text"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "替换后的新文本内容"
                }
            },
            ["required"] = new JArray("new_text")
        };

        public Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            string newText = arguments?["new_text"]?.ToString();
            if (newText == null)
                return Task.FromResult(ToolExecutionResult.Fail("缺少 new_text 参数"));

            var app = connect.WordApplication;
            var selection = app.Selection;
            if (string.IsNullOrEmpty(selection?.Text?.Trim()))
                return Task.FromResult(ToolExecutionResult.Fail("没有选中的文本"));

            string oldText = selection.Text;
            selection.TypeText(newText);
            return Task.FromResult(
                ToolExecutionResult.Ok($"已将选中文本（{oldText.Length}字符）替换为新文本（{newText.Length}字符）。"));
        }
    }
}
