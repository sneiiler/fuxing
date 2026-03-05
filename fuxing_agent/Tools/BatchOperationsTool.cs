using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Word = Microsoft.Office.Interop.Word;

namespace FuXingAgent.Tools
{
    public class BatchOperationsTool
    {
        private readonly Connect _connect;

        public BatchOperationsTool(Connect connect) => _connect = connect;

        [Description("Execute multiple tool operations sequentially in one call to reduce round-trips. " +
            "Stops on first failure. " +
            "Available tools: format_content, edit_content, insert_content, read_content")]
        public async Task<string> batch_operations(
            [Description("操作列表，每项包含 tool（工具名）和 args（参数字典）")] BatchOperation[] operations)
        {
            if (operations == null || operations.Length == 0)
                throw new ArgumentException("operations 不能为空");

            var registry = _connect.ToolRegistryInstance;
            var app = _connect.WordApplication;
            bool wasScreenUpdating = app.ScreenUpdating;
            var results = new StringBuilder();
            int successCount = 0;

            try
            {
                app.ScreenUpdating = false;

                for (int i = 0; i < operations.Length; i++)
                {
                    var op = operations[i];
                    if (string.IsNullOrWhiteSpace(op.tool))
                    {
                        results.AppendLine($"[{i + 1}] ✗ 缺少工具名");
                        break;
                    }
                    if (op.tool == "batch_operations")
                    {
                        results.AppendLine($"[{i + 1}] ✗ 不允许嵌套 batch_operations");
                        break;
                    }
                    if (op.tool == "execute_word_script")
                    {
                        results.AppendLine($"[{i + 1}] ✗ batch_operations 中不允许调用 execute_word_script");
                        break;
                    }

                    var fn = registry.FindFunction(op.tool);
                    if (fn == null)
                    {
                        results.AppendLine($"[{i + 1}] ✗ 工具不存在: {op.tool}");
                        results.AppendLine($"（已完成 {successCount}/{operations.Length}，在第 {i + 1} 步失败后停止）");
                        return results.ToString();
                    }

                    try
                    {
                        var fnArgs = op.args != null ? new AIFunctionArguments(op.args) : null;
                        var result = await fn.InvokeAsync(fnArgs);
                        successCount++;
                        results.AppendLine($"[{i + 1}] ✓ {op.tool}: {result}");
                    }
                    catch (Exception ex)
                    {
                        results.AppendLine($"[{i + 1}] ✗ {op.tool}: {ex.Message}");
                        results.AppendLine($"（已完成 {successCount}/{operations.Length}，在第 {i + 1} 步失败后停止）");
                        return results.ToString();
                    }
                }

                results.Insert(0, $"全部 {successCount} 个操作执行成功：\n");
                return results.ToString();
            }
            finally
            {
                app.ScreenUpdating = wasScreenUpdating;
            }
        }
    }
}
