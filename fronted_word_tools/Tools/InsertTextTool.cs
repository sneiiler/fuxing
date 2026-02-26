using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>在光标位置插入文本</summary>
    public class InsertTextTool : ITool
    {
        public string Name => "insert_text";

        public string Description =>
            "在 Word 文档当前光标位置插入文本。";

        public JObject Parameters => new JObject
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

        public Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            string text = arguments?["text"]?.ToString();
            if (string.IsNullOrEmpty(text))
                return Task.FromResult(ToolExecutionResult.Fail("缺少 text 参数"));

            var app = connect.WordApplication;
            app.Selection.TypeText(text);
            return Task.FromResult(
                ToolExecutionResult.Ok($"已在光标位置插入 {text.Length} 个字符。"));
        }
    }
}
