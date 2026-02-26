using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>载入默认 AI 样式库到当前文档</summary>
    public class LoadDefaultStylesTool : ITool
    {
        public string Name => "load_default_styles";

        public string Description =>
            "载入默认 AI 样式库到当前文档，包含 AI 正文、一级至六级标题、图片、表格样式。";

        public JObject Parameters => null;

        public Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            connect.LoadDefaultStylesPublic();
            return Task.FromResult(
                ToolExecutionResult.Ok("已载入默认 AI 样式库（AI正文、一级~六级标题、图片、表格样式）。"));
        }
    }
}
