using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FuXing.SubAgents
{
    // ═══════════════════════════════════════════════════════════════
    //  子智能体公共模型（动态编排模式）
    //
    //  核心设计：子智能体的 SystemPrompt / Task / 工具白名单
    //  全部由主 Agent（大模型）动态生成，不硬编码任务类型。
    // ═══════════════════════════════════════════════════════════════

    /// <summary>子智能体执行请求（由主 Agent 动态构造）</summary>
    public class SubAgentRequest
    {
        /// <summary>子智能体名称（用于日志追踪和 UI 展示，如 "DateChecker"）</summary>
        public string AgentName { get; set; }

        /// <summary>
        /// 动态生成的系统提示词。
        /// 定义该子智能体的角色、核心职责、输出格式要求等。
        /// 由主 Agent 根据当前任务意图自行生成。
        /// </summary>
        public string SystemPrompt { get; set; }

        /// <summary>发给子智能体的具体操作指令或当前问题</summary>
        public string TaskInstruction { get; set; }

        /// <summary>
        /// 预注入的文档上下文文本（可选，用于减少子智能体的工具调用轮次）。
        /// 由 RunSubAgentTool 自动根据 include_document_text 参数注入。
        /// </summary>
        public string DocumentContext { get; set; }

        /// <summary>最大对话轮次（含工具调用循环，防止无限循环）</summary>
        public int MaxRounds { get; set; } = 5;

        // ── 工具白名单（可选） ──

        /// <summary>
        /// 子智能体可用的工具定义（OpenAI tools 格式）。
        /// null 或空表示不使用任何工具，纯推理模式。
        /// 由 RunSubAgentTool 根据 allowed_tools 参数从 ToolRegistry 过滤生成。
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
        /// <summary>LLM 正在生成回复（用于显示"正在思考"状态）</summary>
        void OnLlmCallStart();

        /// <summary>LLM 返回了思考/推理内容（调用后自动替换"正在思考"状态）</summary>
        void OnThinking(string content);

        /// <summary>子智能体开始调用工具</summary>
        void OnToolCallStart(string toolName);

        /// <summary>工具调用完成</summary>
        void OnToolCallEnd(string toolName, bool success, string output);

        /// <summary>子智能体执行完成</summary>
        void OnComplete(bool success, string output);
    }
}
