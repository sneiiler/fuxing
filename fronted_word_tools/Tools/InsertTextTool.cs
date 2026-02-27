using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>在光标位置插入文本</summary>
    public class InsertTextTool : ToolBase
    {
        public override string Name => "insert_text";
        public override string DisplayName => "插入文本";
        public override ToolCategory Category => ToolCategory.Editing;

        public override string Description =>
            "Insert text at the current cursor position in the Word document.";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["text"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "要插入的文本内容"
                }
            },
            ["required"] = new JArray("text")
        };

        public override Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            string text = RequireString(arguments, "text");
            RequireActiveDocument(connect);

            using (BeginTrackRevisions(connect))
            {
                connect.WordApplication.Selection.TypeText(text);
            }

            return Task.FromResult(
                ToolExecutionResult.Ok($"已在光标位置插入 {text.Length} 个字符。"));
        }
    }
}
