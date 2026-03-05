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
    //  操作历史记录（供 UndoRedo 工具感知操作上下文）
    // ═══════════════════════════════════════════════════════════════

    public class OperationRecord
    {
        public string ToolName { get; set; }
        public string DisplayName { get; set; }
        public string Summary { get; set; }
        public DateTime Timestamp { get; set; }
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

        /// <summary>操作历史记录（仅记录会修改文档的操作，最多保留 100 条）</summary>
        private readonly List<OperationRecord> _operationHistory = new List<OperationRecord>();
        private const int MaxHistorySize = 100;

        /// <summary>只读工具不产生文档修改，不记入操作历史（基于 Category.Query 和 Category.System 自动判定）</summary>
        private bool IsReadOnly(string toolName)
        {
            if (!_tools.TryGetValue(toolName, out var tool)) return false;
            return tool.Category == ToolCategory.Query || tool.Category == ToolCategory.System;
        }

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

            // GetTypes() 在部分类型加载失败时抛出 ReflectionTypeLoadException，
            // 但其 .Types 属性仍包含已成功加载的类型（失败的为 null）。
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types;
                foreach (var le in ex.LoaderExceptions ?? Array.Empty<Exception>())
                    DebugLogger.Instance.LogError("ToolRegistry", $"类型加载失败: {le?.Message}");
            }

            foreach (var type in types)
            {
                if (type == null) continue;
                if (!toolInterface.IsAssignableFrom(type)) continue;
                if (type.IsInterface || type.IsAbstract) continue;
                if (type.GetConstructor(Type.EmptyTypes) == null) continue;

                try
                {
                    var tool = (ITool)Activator.CreateInstance(type);
                    _tools[tool.Name] = tool;
                }
                catch (Exception ex)
                {
                    DebugLogger.Instance.LogError("ToolRegistry", $"工具实例化失败 [{type.Name}]: {ex.Message}");
                }
            }

            DebugLogger.Instance.LogInfo(
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

        /// <summary>按工具名称白名单过滤的定义（用于动态子智能体的工具授权）</summary>
        public JArray GetToolDefinitionsByName(IEnumerable<string> toolNames)
        {
            var definitions = new JArray();
            foreach (var name in toolNames)
            {
                if (_tools.TryGetValue(name, out var tool))
                    definitions.Add(BuildToolDefinition(tool));
            }
            return definitions;
        }

        /// <summary>获取指定分类的工具名称列表（用于 system prompt 动态生成）</summary>
        public List<string> GetToolNamesByCategory(params ToolCategory[] categories)
        {
            var allowed = new HashSet<ToolCategory>(categories);
            return _tools.Values
                .Where(t => allowed.Contains(t.Category))
                .Select(t => t.Name)
                .OrderBy(n => n)
                .ToList();
        }

        /// <summary>获取需要用户审批的危险工具名称列表</summary>
        public List<string> GetDangerousToolNames()
        {
            return _tools.Values
                .Where(t => t.RequiresApproval)
                .Select(t => t.Name)
                .OrderBy(n => n)
                .ToList();
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
                var result = await tool.ExecuteAsync(connect, arguments);

                // 记录成功的文档修改操作到历史
                if (result.Success && !IsReadOnly(functionName))
                {
                    RecordOperation(functionName, tool.DisplayName, result.Output);
                }

                return result;
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

        // ═══════════════════════════════════════════════════════════════
        //  操作历史 API
        // ═══════════════════════════════════════════════════════════════

        /// <summary>记录一次文档修改操作</summary>
        private void RecordOperation(string toolName, string displayName, string summary)
        {
            // 截断过长的摘要
            if (summary != null && summary.Length > 200)
                summary = summary.Substring(0, 200) + "...";

            _operationHistory.Add(new OperationRecord
            {
                ToolName = toolName,
                DisplayName = displayName,
                Summary = summary ?? "",
                Timestamp = DateTime.Now
            });

            // 超出上限时移除最早的记录
            while (_operationHistory.Count > MaxHistorySize)
                _operationHistory.RemoveAt(0);
        }

        /// <summary>获取最近 N 条操作记录（最新的排在前面）</summary>
        public List<OperationRecord> GetRecentOperations(int count = 10)
        {
            int start = Math.Max(0, _operationHistory.Count - count);
            var recent = _operationHistory.GetRange(start, _operationHistory.Count - start);
            recent.Reverse();
            return recent;
        }

        /// <summary>获取操作历史总数</summary>
        public int OperationCount => _operationHistory.Count;

        /// <summary>清空操作历史（会话重置时调用）</summary>
        public void ClearOperationHistory() => _operationHistory.Clear();

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
        //  操作审批辅助（审批 UI 由调用方负责）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>根据工具名和实际参数判断是否需要用户审批确认</summary>
        public bool RequiresApproval(string functionName, JObject arguments = null)
        {
            if (!_tools.TryGetValue(functionName, out var tool)) return false;
            if (!tool.ShouldRequireApproval(arguments)) return false;
            var config = new ConfigLoader().LoadConfig();
            return config.RequireApprovalForDangerousTools;
        }

        /// <summary>构建审批摘要文本（仅返回参数部分，工具名由审批卡片单独渲染）</summary>
        public string BuildApprovalSummary(string functionName, JObject arguments)
        {
            if (!_tools.TryGetValue(functionName, out var tool))
                return $"未知工具: {functionName}";

            if (arguments == null || arguments.Count == 0)
                return "";

            var sb = new StringBuilder();
            foreach (var prop in arguments.Properties())
            {
                string val = prop.Value?.ToString() ?? "";
                // 截断过长的参数值（如代码片段）
                if (val.Length > 300)
                    val = val.Substring(0, 300) + "... (已截断)";
                sb.AppendLine($"{prop.Name}: {val}");
            }

            return sb.ToString().TrimEnd();
        }
    }
}
