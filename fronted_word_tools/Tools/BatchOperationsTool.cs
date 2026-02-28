using Newtonsoft.Json.Linq;
using System;
using System.Text;

namespace FuXing
{
    /// <summary>
    /// 批量执行多个工具操作，减少 LLM 多轮调用的 round-trip。
    /// 按顺序执行，任一失败则停止并报告。
    /// </summary>
    public class BatchOperationsTool : ToolBase
    {
        public override string Name => "batch_operations";
        public override string DisplayName => "批量操作";
        public override ToolCategory Category => ToolCategory.Advanced;
        public override bool RequiresApproval => true;

        public override string Description =>
            "Execute multiple tool operations sequentially in one call to reduce round-trips. Stops on first failure. " +
            "Available tools: format_content, search_and_replace, edit_document_text, insert_table, insert_caption, " +
            "insert_toc, insert_image, set_page_setup, set_header_footer, navigate_to_heading, delete_section";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["operations"] = new JObject
                {
                    ["type"] = "array",
                    ["description"] = "要批量执行的操作列表",
                    ["items"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["tool"] = new JObject { ["type"] = "string", ["description"] = "工具名称" },
                            ["args"] = new JObject { ["type"] = "object", ["description"] = "工具参数" }
                        },
                        ["required"] = new JArray("tool", "args")
                    }
                }
            },
            ["required"] = new JArray("operations")
        };

        public override async System.Threading.Tasks.Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            var operations = arguments["operations"] as JArray;
            if (operations == null || operations.Count == 0)
                return ToolExecutionResult.Fail("缺少 operations 参数或为空数组");

            var registry = connect.ToolRegistry;
            var results = new StringBuilder();
            int successCount = 0;

            // 批量操作期间关闭屏幕刷新，避免每步渲染导致 Word 卡顿
            var app = connect.WordApplication;
            bool wasScreenUpdating = app.ScreenUpdating;
            try
            {
            app.ScreenUpdating = false;

            for (int i = 0; i < operations.Count; i++)
            {
                var op = operations[i] as JObject;
                string toolName = op?["tool"]?.ToString();
                var args = op?["args"] as JObject ?? new JObject();

                if (string.IsNullOrWhiteSpace(toolName))
                {
                    results.AppendLine($"[{i + 1}] ❌ 缺少工具名");
                    break;
                }

                if (toolName == "batch_operations")
                {
                    results.AppendLine($"[{i + 1}] ❌ 不允许嵌套 batch_operations");
                    break;
                }
                if (toolName == "execute_word_script")
                {
                    results.AppendLine($"[{i + 1}] ❌ batch_operations 中不允许调用 execute_word_script");
                    break;
                }

                var result = await registry.ExecuteAsync(toolName, args);

                if (result.Success)
                {
                    successCount++;
                    results.AppendLine($"[{i + 1}] ✓ {toolName}: {result.Output}");
                }
                else
                {
                    results.AppendLine($"[{i + 1}] ❌ {toolName}: {result.Output}");
                    results.AppendLine($"（已完成 {successCount}/{operations.Count}，在第 {i + 1} 步失败后停止）");
                    return ToolExecutionResult.Fail(results.ToString());
                }
            }

            results.Insert(0, $"全部 {successCount} 个操作执行成功：\n");
            return ToolExecutionResult.Ok(results.ToString());

            }
            finally
            {
                // 无论成功失败，都恢复屏幕刷新
                app.ScreenUpdating = wasScreenUpdating;
            }
        }
    }
}
