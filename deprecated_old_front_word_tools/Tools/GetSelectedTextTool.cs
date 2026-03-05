using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>获取用户选中的文本内容</summary>
    public class GetSelectedTextTool : ToolBase
    {
        public override string Name => "get_selected_text";
        public override string DisplayName => "获取选中文本";
        public override ToolCategory Category => ToolCategory.Query;

        public override string Description =>
            "Get the currently selected text content in the Word document.";

        public override JObject Parameters => null;

        public override Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
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
