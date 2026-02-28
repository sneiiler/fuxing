using Newtonsoft.Json.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>撤销或重做文档操作，支持查看操作历史</summary>
    public class UndoRedoTool : ToolBase
    {
        public override string Name => "undo_redo";
        public override string DisplayName => "撤销/重做";
        public override ToolCategory Category => ToolCategory.Editing;

        public override string Description =>
            "Undo, redo, or list recent operations.\n" +
            "- action: \"undo\" (default), \"redo\", or \"list\" (show recent modifying operations)\n" +
            "- times: number of operations to undo/redo (default 1, max 50)\n" +
            "IMPORTANT: Call with action=\"list\" FIRST to see operation history before undoing, " +
            "so you know exactly how many steps to undo. Each tool call typically creates 1 undo step.";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["action"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray("undo", "redo", "list"),
                    ["description"] = "操作类型：undo（撤销）、redo（重做）、list（查看最近操作历史，建议在 undo 前先调用）"
                },
                ["times"] = new JObject
                {
                    ["type"] = "integer",
                    ["description"] = "操作次数（默认 1，最大 50）"
                }
            }
        };

        public override Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            RequireActiveDocument(connect);

            string action = OptionalString(arguments, "action", "undo");
            int times = OptionalInt(arguments, "times", 1);

            if (action == "list")
            {
                return Task.FromResult(BuildOperationList(connect));
            }

            if (times < 1 || times > 50)
                throw new ToolArgumentException("times 必须在 1-50 之间");

            var doc = connect.WordApplication.ActiveDocument;
            int successCount = 0;

            if (action == "undo")
            {
                for (int i = 0; i < times; i++)
                {
                    if (!doc.Undo()) break;
                    successCount++;
                }

                var sb = new StringBuilder();
                sb.Append($"已撤销 {successCount} 步操作");
                if (successCount < times)
                    sb.Append($"（请求 {times} 步，可撤销操作已用尽）");

                // 附带操作历史上下文
                sb.AppendLine();
                sb.Append(BuildHistoryContext(connect, "undo"));

                return Task.FromResult(ToolExecutionResult.Ok(sb.ToString()));
            }

            if (action == "redo")
            {
                for (int i = 0; i < times; i++)
                {
                    if (!doc.Redo()) break;
                    successCount++;
                }

                var sb = new StringBuilder();
                sb.Append($"已重做 {successCount} 步操作");
                if (successCount < times)
                    sb.Append($"（请求 {times} 步，可重做操作已用尽）");

                return Task.FromResult(ToolExecutionResult.Ok(sb.ToString()));
            }

            throw new ToolArgumentException($"未知 action: {action}，可选: undo, redo, list");
        }

        /// <summary>构建操作历史列表</summary>
        private ToolExecutionResult BuildOperationList(Connect connect)
        {
            var ops = connect.ToolRegistry.GetRecentOperations(20);

            if (ops.Count == 0)
                return ToolExecutionResult.Ok("本次会话中尚无文档修改操作记录。");

            var sb = new StringBuilder();
            sb.AppendLine($"本次会话的文档修改操作历史（共 {connect.ToolRegistry.OperationCount} 条，最近 {ops.Count} 条，最新在前）：");
            sb.AppendLine("（每条操作通常对应 1 个 undo 步骤，execute_word_script 可能对应多步）");
            sb.AppendLine();

            for (int i = 0; i < ops.Count; i++)
            {
                var op = ops[i];
                string timeStr = op.Timestamp.ToString("HH:mm:ss");
                sb.AppendLine($"  #{i + 1} [{timeStr}] {op.DisplayName}({op.ToolName}): {op.Summary}");
            }

            return ToolExecutionResult.Ok(sb.ToString().TrimEnd());
        }

        /// <summary>构建附在 undo/redo 结果后的历史上下文摘要</summary>
        private string BuildHistoryContext(Connect connect, string action)
        {
            var ops = connect.ToolRegistry.GetRecentOperations(5);
            if (ops.Count == 0) return "";

            var sb = new StringBuilder();
            sb.AppendLine("最近的操作记录（最新在前，供参考）：");
            for (int i = 0; i < ops.Count; i++)
            {
                var op = ops[i];
                sb.AppendLine($"  #{i + 1} {op.DisplayName}: {op.Summary}");
            }
            return sb.ToString().TrimEnd();
        }
    }
}
