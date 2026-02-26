using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>
    /// 所有 LLM 可调用工具的统一接口。
    /// 每个工具提供自身的名称、描述、参数 schema 和执行逻辑。
    /// </summary>
    public interface ITool
    {
        /// <summary>工具名称（对应 function-calling 的 function name）</summary>
        string Name { get; }

        /// <summary>工具描述（告诉 LLM 何时调用此工具）</summary>
        string Description { get; }

        /// <summary>参数 JSON Schema（无参数时返回 null 或空 JObject）</summary>
        JObject Parameters { get; }

        /// <summary>执行工具逻辑</summary>
        Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments);
    }
}
