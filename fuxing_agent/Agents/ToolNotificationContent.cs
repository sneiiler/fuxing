using Microsoft.Extensions.AI;

namespace FuXingAgent.Agents
{
    /// <summary>工具开始执行通知</summary>
    public class ToolExecutionStartContent : AIContent
    {
        public string ToolName { get; }

        public ToolExecutionStartContent(string toolName)
        {
            ToolName = toolName;
        }
    }

    /// <summary>工具执行完成通知</summary>
    public class ToolExecutionEndContent : AIContent
    {
        public string ToolName { get; }
        public bool Success { get; }

        public ToolExecutionEndContent(string toolName, bool success)
        {
            ToolName = toolName;
            Success = success;
        }
    }

    /// <summary>Agent 运行中的错误通知</summary>
    public class AgentErrorContent : AIContent
    {
        public string Message { get; }

        public AgentErrorContent(string message)
        {
            Message = message;
        }
    }
}
