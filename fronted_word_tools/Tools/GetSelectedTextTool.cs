using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>获取用户选中的文本内容</summary>
    public class GetSelectedTextTool : ITool
    {
        public string Name => "get_selected_text";

        public string Description =>
            "获取用户在 Word 文档中当前选中的文本内容。";

        public JObject Parameters => null;

        public Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            var app = connect.WordApplication;
            var selection = app.Selection;
            string text = selection?.Text?.Trim();
            if (string.IsNullOrEmpty(text))
                return Task.FromResult(
                    ToolExecutionResult.Ok("当前没有选中任何文本。"));

            return Task.FromResult(
                ToolExecutionResult.Ok($"当前选中文本（{text.Length}字符）:\n{text}"));
        }
    }
}
