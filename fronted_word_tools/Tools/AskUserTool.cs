using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>
    /// LLM 向用户提问的工具。
    /// ExecuteAsync 本身只校验参数并返回一个占位结果；
    /// 真正的交互逻辑（显示卡片、等待用户选择）由 TaskPaneControl 的工具执行循环特殊处理。
    /// </summary>
    public class AskUserTool : ToolBase
    {
        public override string Name => "ask_user";
        public override string DisplayName => "向用户提问";
        public override ToolCategory Category => ToolCategory.System;

        public override string Description =>
            "Ask the user a question to clarify intent, confirm a choice, or request additional input. " +
            "ALWAYS call this tool instead of sending a plain text message whenever you have a question for the user. " +
            "Supports selectable options and free-text input. " +
            "Use scenarios: ambiguous instructions, choosing between multiple approaches, " +
            "confirming destructive operations, requesting missing parameters, follow-up questions after completing a task.";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["question"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "The question to ask the user"
                },
                ["options"] = new JObject
                {
                    ["type"] = "array",
                    ["description"] = "Selectable options for the user",
                    ["items"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["label"] = new JObject { ["type"] = "string", ["description"] = "Option text" },
                            ["description"] = new JObject { ["type"] = "string", ["description"] = "Optional description" }
                        },
                        ["required"] = new JArray("label")
                    }
                },
                ["allow_free_input"] = new JObject
                {
                    ["type"] = "boolean",
                    ["description"] = "Show a text input for custom answers (default: true)"
                }
            },
            ["required"] = new JArray("question")
        };

        /// <summary>
        /// 工具执行本身不做交互，只验证参数。
        /// 交互由 TaskPaneControl 处理（在工具循环中特殊分支）。
        /// </summary>
        public override Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            // 该工具实际不会被直接执行，TaskPaneControl 会拦截并走专用路径
            string question = RequireString(arguments, "question");

            return Task.FromResult(ToolExecutionResult.Ok("(waiting for user response)"));
        }
    }
}
