using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>替换选中的文本</summary>
    public class ReplaceSelectedTextTool : ToolBase
    {
        public override string Name => "replace_selected_text";
        public override string DisplayName => "替换选中文本";
        public override ToolCategory Category => ToolCategory.Editing;

        public override string Description =>
            "Replace the currently selected text in the Word document with the specified content.";

        public override JObject Parameters => new JObject
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

        public override Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            string newText = RequireString(arguments, "new_text");
            RequireActiveDocument(connect);

            var selection = connect.WordApplication.Selection;
            if (string.IsNullOrEmpty(selection?.Text?.Trim()))
                return Task.FromResult(ToolExecutionResult.Fail("没有选中的文本"));

            string oldText = selection.Text;
            using (BeginTrackRevisions(connect))
            {
                selection.TypeText(newText);
            }

            return Task.FromResult(
                ToolExecutionResult.Ok($"已将选中文本（{oldText.Length}字符）替换为新文本（{newText.Length}字符）。"));
        }
    }
}
