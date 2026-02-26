using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FuXing
{
    // ═══════════════════════════════════════════════════════════════
    //  工具执行结果
    // ═══════════════════════════════════════════════════════════════

    public class ToolExecutionResult
    {
        public bool Success { get; set; }
        public string Output { get; set; }

        public static ToolExecutionResult Ok(string output) =>
            new ToolExecutionResult { Success = true, Output = output };

        public static ToolExecutionResult Fail(string error) =>
            new ToolExecutionResult { Success = false, Output = error };
    }

    // ═══════════════════════════════════════════════════════════════
    //  工具注册表 — 将插件功能封装为 LLM 可调用的 tools
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 管理所有可供大模型调用的工具定义和执行逻辑。
    /// 各工具的实现位于 Tools/ 目录下的独立文件中，均实现 <see cref="ITool"/> 接口。
    /// </summary>
    public class ToolRegistry
    {
        private readonly Dictionary<string, ITool> _tools = new Dictionary<string, ITool>();

        public ToolRegistry()
        {
            Register(new CorrectSelectedTextTool());
            Register(new CorrectAllTextTool());
            Register(new FormatSelectedTableTool());
            Register(new FormatAllTablesTool());
            Register(new CheckStandardTool());
            Register(new GetSelectedTextTool());
            Register(new GetDocumentInfoTool());
            Register(new LoadDefaultStylesTool());
            Register(new InsertTextTool());
            Register(new ReplaceSelectedTextTool());
        }

        private void Register(ITool tool)
        {
            _tools[tool.Name] = tool;
        }

        /// <summary>所有已注册工具的定义（OpenAI tools 格式）</summary>
        public JArray GetToolDefinitions()
        {
            var definitions = new JArray();
            foreach (var tool in _tools.Values)
                definitions.Add(BuildToolDefinition(tool));
            return definitions;
        }

        /// <summary>
        /// 执行指定的工具调用。
        /// 在 UI 线程上运行（由调用方保证 Invoke），因为 Word COM 必须在 STA 线程。
        /// </summary>
        public async Task<ToolExecutionResult> ExecuteAsync(string functionName, JObject arguments)
        {
            var connect = Connect.CurrentInstance;
            if (connect == null)
                return ToolExecutionResult.Fail("插件实例不可用");

            if (!_tools.TryGetValue(functionName, out var tool))
                return ToolExecutionResult.Fail($"未知工具: {functionName}");

            try
            {
                return await tool.ExecuteAsync(connect, arguments);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"工具执行异常 [{functionName}]: {ex.Message}");
                return ToolExecutionResult.Fail($"执行失败: {ex.Message}");
            }
        }

        /// <summary>检查工具名称是否已注册</summary>
        public bool IsRegistered(string functionName) => _tools.ContainsKey(functionName);

        // ═══════════════════════════════════════════════════════════════
        //  辅助方法
        // ═══════════════════════════════════════════════════════════════

        private static JObject BuildToolDefinition(ITool tool)
        {
            var parameters = tool.Parameters;

            // 无参数时使用空 object schema
            if (parameters == null || !parameters.HasValues)
            {
                parameters = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject()
                };
            }

            return new JObject
            {
                ["type"] = "function",
                ["function"] = new JObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = parameters
                }
            };
        }
    }
}
