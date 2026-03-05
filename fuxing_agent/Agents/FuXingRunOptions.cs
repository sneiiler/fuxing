using Microsoft.Agents.AI;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using FuXingAgent.Tools;

namespace FuXingAgent.Agents
{
    /// <summary>
    /// 福星 Agent 运行选项 — 携带 Word COM 的 STA 线程封送委托和系统提示词。
    /// </summary>
    public class FuXingRunOptions : AgentRunOptions
    {
        /// <summary>
        /// 将委托 marshal 到 Word 所在的 STA 主线程上同步执行并返回结果。
        /// 工具操作 Word COM 对象时必须在 STA 主线程。
        /// </summary>
        public Func<Func<object>, object> InvokeOnSta { get; set; }

        /// <summary>
        /// 系统提示词，由 UI 在每次运行前构建并注入。
        /// </summary>
        public string SystemPrompt { get; set; }

        /// <summary>
        /// 请求用户审批回调 — 在 UI 线程上显示审批卡片并等待用户决策。
        /// 参数：(工具显示名, 函数名, 参数摘要) → 返回 true=允许 / false=拒绝
        /// </summary>
        public Func<string, string, string, Task<bool>> RequestApprovalAsync { get; set; }

        /// <summary>
        /// ask_user 回调：在 UI 线程显示问答卡片并等待用户输入。
        /// 参数：(问题, 选项, 是否允许自由输入) → 返回用户答案
        /// </summary>
        public Func<string, List<AskUserOption>, bool, Task<string>> RequestUserInputAsync { get; set; }
    }
}
