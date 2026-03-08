using Microsoft.Extensions.AI;
using FuXingAgent.Core;
using FuXingAgent.Tools;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FuXingAgent.Agents
{
#pragma warning disable MEAI001
    /// <summary>
    /// 工具注册表：自动发现带 [Description] 的工具方法并注册为 AIFunction。
    /// </summary>
    public class ToolRegistry
    {
        private readonly Dictionary<string, AIFunction> _tools =
            new Dictionary<string, AIFunction>(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> _workflowFunctions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> _dangerousTools =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "batch_operations",
                "execute_word_script"
            };

        private static readonly Dictionary<string, string> _displayNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "batch_operations", "批量操作" },
                { "execute_word_script", "执行脚本" },
                { "web_request", "网络请求" }
            };

        public void Initialize(Connect connect)
        {
            _tools.Clear();
            var assembly = Assembly.GetExecutingAssembly();
            var cfg = connect?.ConfigLoaderInstance?.LoadConfig() ?? new ConfigLoader.Config();

            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || type.IsInterface || !type.IsClass) continue;
                if (type.Namespace == null ||
                    (!type.Namespace.StartsWith("FuXingAgent.Tools") &&
                     !type.Namespace.StartsWith("FuXingAgent.Workflows"))) continue;

                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (method.GetCustomAttribute<DescriptionAttribute>() == null) continue;

                    try
                    {
                        object instance = CreateToolInstance(type, connect);
                        AIFunction fn = new PolicyWrappedAIFunction(
                            AIFunctionFactory.Create(method, instance),
                            InvokeWithPolicies);

                        if (_dangerousTools.Contains(fn.Name) && cfg.RequireApprovalForDangerousTools)
                            fn = new ApprovalRequiredAIFunction(fn);

                        _tools[fn.Name] = fn;
                        if (type.Namespace.StartsWith("FuXingAgent.Workflows"))
                            _workflowFunctions.Add(fn.Name);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[ToolRegistry] 注册失败 {type.Name}.{method.Name}: {ex.Message}");
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine($"[ToolRegistry] 已注册 {_tools.Count} 个工具");
        }

        private object CreateToolInstance(Type type, Connect connect)
        {
            var ctor = type.GetConstructor(new[] { typeof(Connect) });
            if (ctor != null) return ctor.Invoke(new object[] { connect });

            throw new InvalidOperationException($"工具类 {type.Name} 必须提供 (Connect) 构造函数");
        }

        public List<AITool> GetAllTools(IList<string> allowedNames = null)
        {
            return _tools.Values
                .Where(fn => allowedNames == null || allowedNames.Contains(fn.Name, StringComparer.OrdinalIgnoreCase))
                .Cast<AITool>()
                .ToList();
        }

        public AIFunction FindFunction(string name)
        {
            return _tools.TryGetValue(name, out var fn) ? fn : null;
        }

        public string GetDisplayName(string functionName)
        {
            return _displayNames.TryGetValue(functionName, out var name) ? name : functionName;
        }

        public static string BuildApprovalSummary(IDictionary<string, object> args)
        {
            if (args == null || args.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var kv in args)
            {
                string val = kv.Value?.ToString() ?? "";
                if (val.Length > 300) val = val.Substring(0, 300) + "... (已截断)";
                sb.AppendLine($"{kv.Key}: {val}");
            }
            return sb.ToString().TrimEnd();
        }

        private object InvokeWithPolicies(AIFunction fn, AIFunctionArguments args)
        {
            var runOptions = ToolInvocationScope.CurrentOptions.Value;

            if (string.Equals(fn.Name, "ask_user", StringComparison.OrdinalIgnoreCase))
            {
                if (runOptions?.RequestUserInputAsync == null)
                    return "错误: ask_user 未配置 UI 回调";

                try
                {
                    ParseAskUserArgs(args, out var question, out var options, out var allowFreeInput);
                    if (string.IsNullOrWhiteSpace(question))
                        return "错误: ask_user 缺少 question 参数";

                    var answerTask = runOptions.RequestUserInputAsync(question, options, allowFreeInput);
                    var answer = WaitTaskSafely(answerTask);
                    return answer ?? string.Empty;
                }
                catch (Exception ex)
                {
                    return $"错误: ask_user 失败: {ex.Message}";
                }
            }

            try
            {
                // Workflow 自行管理 STA 调用，不整体包装到 UI 线程
                if (_workflowFunctions.Contains(fn.Name))
                    return fn.InvokeAsync(args, CancellationToken.None).GetAwaiter().GetResult();

                if (runOptions?.InvokeOnSta != null)
                {
                    return runOptions.InvokeOnSta(() =>
                        fn.InvokeAsync(args, CancellationToken.None).GetAwaiter().GetResult());
                }

                return fn.InvokeAsync(args, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                return $"错误: {ex.Message}";
            }
        }

        private static void ParseAskUserArgs(
            AIFunctionArguments args,
            out string question,
            out List<AskUserOption> options,
            out bool allowFreeInput)
        {
            question = "";
            options = new List<AskUserOption>();
            allowFreeInput = true;

            if (args == null) return;

            if (args.TryGetValue("question", out var qObj) && qObj != null)
            {
                if (qObj is JsonElement qEl && qEl.ValueKind == JsonValueKind.String)
                    question = qEl.GetString();
                else
                    question = qObj.ToString();
            }

            if (args.TryGetValue("allow_free_input", out var allowObj) && allowObj != null)
            {
                if (allowObj is bool b)
                    allowFreeInput = b;
                else if (allowObj is JsonElement allowEl &&
                         (allowEl.ValueKind == JsonValueKind.True || allowEl.ValueKind == JsonValueKind.False))
                    allowFreeInput = allowEl.GetBoolean();
                else if (bool.TryParse(allowObj.ToString(), out var parsed))
                    allowFreeInput = parsed;
            }

            if (args.TryGetValue("options", out var optsObj) && optsObj != null)
            {
                if (optsObj is JsonElement optsEl && optsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in optsEl.EnumerateArray())
                    {
                        string label = item.TryGetProperty("label", out var l) ? l.ToString() : "";
                        string desc = item.TryGetProperty("description", out var d) ? d.ToString() : null;
                        if (!string.IsNullOrWhiteSpace(label))
                            options.Add(new AskUserOption { label = label, description = desc });
                    }
                }
                else if (optsObj is IEnumerable<object> seq)
                {
                    foreach (var item in seq)
                    {
                        if (item is IDictionary<string, object> dict)
                        {
                            dict.TryGetValue("label", out var labelObj);
                            dict.TryGetValue("description", out var descObj);
                            string label = labelObj?.ToString() ?? "";
                            if (!string.IsNullOrWhiteSpace(label))
                                options.Add(new AskUserOption { label = label, description = descObj?.ToString() });
                        }
                    }
                }
            }
        }

        private static T WaitTaskSafely<T>(Task<T> task)
        {
            if (task == null) return default(T);
            if (task.IsCompleted) return task.GetAwaiter().GetResult();

            bool isStaUiThread =
                Thread.CurrentThread.GetApartmentState() == ApartmentState.STA &&
                Application.MessageLoop;

            if (!isStaUiThread)
                return task.GetAwaiter().GetResult();

            // 在 UI 线程上等待时保持消息泵运行，避免 Word 界面假死。
            while (!task.IsCompleted)
            {
                Application.DoEvents();
                Thread.Sleep(15);
            }

            return task.GetAwaiter().GetResult();
        }

        /// <summary>
        /// 用 DelegatingAIFunction 包装工具调用，在 InvokeCoreAsync 中注入 STA 封送和错误处理策略。
        /// </summary>
        private sealed class PolicyWrappedAIFunction : DelegatingAIFunction
        {
            private readonly Func<AIFunction, AIFunctionArguments, object> _invoke;

            public PolicyWrappedAIFunction(AIFunction inner, Func<AIFunction, AIFunctionArguments, object> invoke)
                : base(inner) => _invoke = invoke;

            protected override ValueTask<object> InvokeCoreAsync(
                AIFunctionArguments arguments, CancellationToken cancellationToken)
            {
                return new ValueTask<object>(_invoke(InnerFunction, arguments));
            }
        }
    }

    internal static class ToolInvocationScope
    {
        public static readonly AsyncLocal<FuXingRunOptions> CurrentOptions =
            new AsyncLocal<FuXingRunOptions>();

        public static IDisposable Enter(FuXingRunOptions options)
        {
            var previous = CurrentOptions.Value;
            CurrentOptions.Value = options;
            return new Scope(() => CurrentOptions.Value = previous);
        }

        private sealed class Scope : IDisposable
        {
            private Action _onDispose;

            public Scope(Action onDispose)
            {
                _onDispose = onDispose;
            }

            public void Dispose()
            {
                var action = Interlocked.Exchange(ref _onDispose, null);
                action?.Invoke();
            }
        }
    }
#pragma warning restore MEAI001
}
