using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>
    /// 所有 LLM 可调用工具的统一接口。
    /// 每个工具提供自身的名称、描述、参数 schema 和执行逻辑。
    /// 推荐继承 <see cref="ToolBase"/> 而非直接实现此接口。
    /// </summary>
    public interface ITool
    {
        /// <summary>工具名称（对应 function-calling 的 function name）</summary>
        string Name { get; }

        /// <summary>中文显示名（用于 UI 展示，如 "文本纠错"）</summary>
        string DisplayName { get; }

        /// <summary>工具描述（告诉 LLM 何时调用此工具）</summary>
        string Description { get; }

        /// <summary>参数 JSON Schema（无参数时返回 null 或空 JObject）</summary>
        JObject Parameters { get; }

        /// <summary>工具分类（用于按功能域组织工具列表）</summary>
        ToolCategory Category { get; }

        /// <summary>是否需要用户审批确认（静态标记，不依赖参数）</summary>
        bool RequiresApproval { get; }

        /// <summary>
        /// 根据实际调用参数判断是否需要审批确认。
        /// 默认回退到 <see cref="RequiresApproval"/> 属性。
        /// </summary>
        bool ShouldRequireApproval(JObject arguments);

        /// <summary>执行工具逻辑</summary>
        Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments);
    }
}
