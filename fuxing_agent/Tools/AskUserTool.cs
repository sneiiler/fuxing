using System.ComponentModel;

namespace FuXingAgent.Tools
{
    public class AskUserTool
    {
        private readonly Connect _connect;
        public AskUserTool(Connect connect) => _connect = connect;

        [Description("Ask the user a question to clarify intent, confirm a choice, or request additional input. " +
            "ALWAYS call this tool instead of sending a plain text message whenever you have a question. " +
            "Supports selectable options and free-text input.")]
        public string ask_user(
            [Description("要向用户提问的问题")] string question,
            [Description("可选的选项列表")] AskUserOption[] options = null,
            [Description("是否允许用户自由输入（默认 true）")] bool allow_free_input = true)
        {
            // 该工具不会被直接执行 —— UI 层拦截 ask_user 调用并走专用交互路径
            return "(waiting for user response)";
        }
    }
}