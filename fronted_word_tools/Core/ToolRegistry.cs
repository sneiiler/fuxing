using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
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
    /// 通过反射自动发现当前程序集中所有实现 <see cref="ITool"/> 接口的非抽象类，
    /// 无需在构造函数中手动列举每个工具。
    /// </summary>
    public class ToolRegistry
    {
        private readonly Dictionary<string, ITool> _tools = new Dictionary<string, ITool>();

        /// <summary>分类名称映射（用于 system prompt 中的分类标题）</summary>
        private static readonly Dictionary<ToolCategory, string> CategoryNames
            = new Dictionary<ToolCategory, string>
            {
                [ToolCategory.Query] = "信息查询",
                [ToolCategory.Editing] = "文本编辑",
                [ToolCategory.Formatting] = "格式化",
                [ToolCategory.Structure] = "结构操作",
                [ToolCategory.PageLayout] = "页面设置",
                [ToolCategory.Advanced] = "高级工具",
                [ToolCategory.System] = "系统工具",
            };

        public ToolRegistry()
        {
            AutoDiscover();
        }

        /// <summary>
        /// 反射自动发现并注册当前程序集内所有 ITool 实现类。
        /// 跳过接口 / 抽象类 / 无公共无参构造函数的类型。
        /// </summary>
        private void AutoDiscover()
        {
            var toolInterface = typeof(ITool);
            var assembly = Assembly.GetExecutingAssembly();

            foreach (var type in assembly.GetTypes())
            {
                if (!toolInterface.IsAssignableFrom(type)) continue;
                if (type.IsInterface || type.IsAbstract) continue;
                if (type.GetConstructor(Type.EmptyTypes) == null) continue;

                var tool = (ITool)Activator.CreateInstance(type);
                _tools[tool.Name] = tool;
            }

            System.Diagnostics.Debug.WriteLine(
                $"[ToolRegistry] 自动发现 {_tools.Count} 个工具: {string.Join(", ", _tools.Keys.OrderBy(k => k))}");
        }

        /// <summary>所有已注册工具的定义（OpenAI tools 格式）</summary>
        public JArray GetToolDefinitions()
        {
            var definitions = new JArray();
            foreach (var tool in _tools.Values)
                definitions.Add(BuildToolDefinition(tool));
            return definitions;
        }

        /// <summary>按工具分类过滤的定义（用于子智能体获取工具子集）</summary>
        public JArray GetToolDefinitions(params ToolCategory[] categories)
        {
            var allowed = new HashSet<ToolCategory>(categories);
            var definitions = new JArray();
            foreach (var tool in _tools.Values)
            {
                if (allowed.Contains(tool.Category))
                    definitions.Add(BuildToolDefinition(tool));
            }
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
            catch (ToolArgumentException ex)
            {
                // 参数校验失败——直接返回友好消息，无需记录堆栈
                return ToolExecutionResult.Fail(ex.Message);
            }
            catch (Exception ex)
            {
                // 输出完整异常链到调试控制台，便于诊断 COM 调用失败
                System.Diagnostics.Debug.WriteLine("════════════════════════════════════════");
                System.Diagnostics.Debug.WriteLine($"工具执行异常 [{functionName}]");
                System.Diagnostics.Debug.WriteLine($"异常类型: {ex.GetType().FullName}");
                System.Diagnostics.Debug.WriteLine($"消息: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"HResult: 0x{ex.HResult:X8}");
                System.Diagnostics.Debug.WriteLine($"堆栈:\n{ex.StackTrace}");

                var inner = ex.InnerException;
                int depth = 1;
                while (inner != null)
                {
                    System.Diagnostics.Debug.WriteLine($"── 内部异常 #{depth} ──");
                    System.Diagnostics.Debug.WriteLine($"  类型: {inner.GetType().FullName}");
                    System.Diagnostics.Debug.WriteLine($"  消息: {inner.Message}");
                    System.Diagnostics.Debug.WriteLine($"  HResult: 0x{inner.HResult:X8}");
                    System.Diagnostics.Debug.WriteLine($"  堆栈:\n{inner.StackTrace}");
                    inner = inner.InnerException;
                    depth++;
                }

                System.Diagnostics.Debug.WriteLine("════════════════════════════════════════");

                // 构造包含内部异常的详细错误信息返回给 LLM
                string detail = ex.Message;
                if (ex.InnerException != null)
                    detail += $" -> {ex.InnerException.Message}";

                return ToolExecutionResult.Fail($"执行失败: {detail}");
            }
        }

        /// <summary>检查工具名称是否已注册</summary>
        public bool IsRegistered(string functionName) => _tools.ContainsKey(functionName);

        /// <summary>
        /// 生成按分类组织的工具摘要（用于 system prompt），
        /// LLM 可根据分类更快地定位合适的工具。
        /// </summary>
        public string BuildToolPromptSummary()
        {
            var sb = new StringBuilder();

            // 按 Category 分组，每组内按 Name 排序
            var groups = _tools.Values
                .GroupBy(t => t.Category)
                .OrderBy(g => g.Key);

            foreach (var group in groups)
            {
                string categoryLabel = CategoryNames.TryGetValue(group.Key, out var name)
                    ? name
                    : group.Key.ToString();

                sb.AppendLine($"[{categoryLabel}]");
                foreach (var tool in group.OrderBy(t => t.Name))
                    sb.AppendLine($"  - {tool.Name}: {tool.Description}");
                sb.AppendLine();
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>获取工具中文显示名（用于 UI 展示）</summary>
        public string GetDisplayName(string functionName)
        {
            if (_tools.TryGetValue(functionName, out var tool))
                return tool.DisplayName;
            return functionName;
        }

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

        // ═══════════════════════════════════════════════════════════════
        //  危险操作审批辅助（审批 UI 由调用方负责）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>检查指定工具是否需要用户审批</summary>
        public bool RequiresApproval(string functionName)
        {
            if (!_tools.TryGetValue(functionName, out var tool)) return false;
            if (!tool.RequiresApproval) return false;
            var config = new ConfigLoader().LoadConfig();
            return config.RequireApprovalForDangerousTools;
        }

        /// <summary>构建审批摘要文本（展示给用户确认的操作信息）</summary>
        public string BuildApprovalSummary(string functionName, JObject arguments)
        {
            if (!_tools.TryGetValue(functionName, out var tool))
                return $"未知工具: {functionName}";

            var sb = new StringBuilder();
            sb.AppendLine($"AI 助手请求执行以下高风险操作：");
            sb.AppendLine();
            sb.AppendLine($"工具: {tool.DisplayName} ({tool.Name})");

            if (arguments != null && arguments.Count > 0)
            {
                sb.AppendLine("参数:");
                foreach (var prop in arguments.Properties())
                {
                    string val = prop.Value?.ToString() ?? "";
                    // 截断过长的参数值（如代码片段）
                    if (val.Length > 300)
                        val = val.Substring(0, 300) + "... (已截断)";
                    sb.AppendLine($"  {prop.Name}: {val}");
                }
            }

            return sb.ToString();
        }
    }
}
