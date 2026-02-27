using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FuXing.SubAgents
{
    // ═══════════════════════════════════════════════════════════════
    //  子智能体公共模型
    // ═══════════════════════════════════════════════════════════════

    /// <summary>子智能体任务类型</summary>
    public enum SubAgentTask
    {
        /// <summary>分析文档结构（推断标题层级、检测格式不一致）</summary>
        AnalyzeStructure,

        /// <summary>提取文档关键信息（用于前后冲突检查）</summary>
        ExtractKeyInfo,

        /// <summary>自定义任务（由 prompt 描述）</summary>
        Custom
    }

    /// <summary>子智能体执行请求</summary>
    public class SubAgentRequest
    {
        /// <summary>任务类型</summary>
        public SubAgentTask Task { get; set; }

        /// <summary>用户 / 主 Agent 的具体指令</summary>
        public string Prompt { get; set; }

        /// <summary>预注入的文档结构数据（由 DocumentStructureExtractor 生成）</summary>
        public DocumentStructure DocumentStructure { get; set; }

        /// <summary>预注入的文档文本片段（用于关键信息提取等）</summary>
        public string DocumentText { get; set; }

        /// <summary>最大对话轮次（含工具调用循环，防止无限循环）</summary>
        public int MaxRounds { get; set; } = 5;

        // ── 工具共用（可选） ──

        /// <summary>
        /// 子智能体可用的工具定义（OpenAI tools 格式）。
        /// null 表示不使用任何工具，纯推理模式。
        /// </summary>
        public JArray ToolDefinitions { get; set; }

        /// <summary>
        /// 工具执行委托，由调用方提供。
        /// 签名：(functionName, arguments) => ToolExecutionResult。
        /// 调用方负责线程调度（Word COM 需在 STA 线程）。
        /// null 时即使 LLM 返回 tool_calls 也会被忽略。
        /// </summary>
        public Func<string, JObject, Task<ToolExecutionResult>> ToolExecutor { get; set; }
    }

    /// <summary>子智能体执行结果</summary>
    public class SubAgentResult
    {
        public bool Success { get; set; }

        /// <summary>子智能体的最终文本回复</summary>
        public string Output { get; set; }

        /// <summary>消耗的 token 估算</summary>
        public int EstimatedTokens { get; set; }

        /// <summary>实际执行的轮次</summary>
        public int RoundsUsed { get; set; }

        public static SubAgentResult Ok(string output, int rounds, int tokens) =>
            new SubAgentResult { Success = true, Output = output, RoundsUsed = rounds, EstimatedTokens = tokens };

        public static SubAgentResult Fail(string error) =>
            new SubAgentResult { Success = false, Output = error };
    }

    /// <summary>LLM 非流式响应解析结果（内部使用）</summary>
    internal class LlmResponse
    {
        public string Content { get; set; }
        public List<ToolCallRequest> ToolCalls { get; set; }
        public bool HasToolCalls => ToolCalls != null && ToolCalls.Count > 0;
        public string FinishReason { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════
    //  子智能体执行进度回调
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 子智能体执行过程中的进度回调接口。
    /// UI 层实现此接口，实时展示子智能体的思考、工具调用等内部步骤。
    /// </summary>
    public interface ISubAgentProgress
    {
        /// <summary>新一轮对话开始</summary>
        void OnRoundStart(int round, int maxRounds);

        /// <summary>LLM 返回了思考/推理内容</summary>
        void OnThinking(string content);

        /// <summary>子智能体开始调用工具</summary>
        void OnToolCallStart(string toolName);

        /// <summary>工具调用完成</summary>
        void OnToolCallEnd(string toolName, bool success, string output);

        /// <summary>子智能体执行完成</summary>
        void OnComplete(bool success, string output);
    }
}
