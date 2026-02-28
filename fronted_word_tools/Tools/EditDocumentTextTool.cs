using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>
    /// 统一的文本编辑工具，合并原 InsertTextTool 和 ReplaceSelectedTextTool。
    /// 通过 action 参数控制行为：insert_at_cursor / replace_selection / append_to_document
    /// </summary>
    public class EditDocumentTextTool : ToolBase
    {
        public override string Name => "edit_document_text";
        public override string DisplayName => "编辑文档文本";
        public override ToolCategory Category => ToolCategory.Editing;

        public override string Description =>
            "Insert, replace, or append text. Actions: insert_at_cursor (default), replace_selection, append_to_document.";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["action"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray("insert_at_cursor", "replace_selection", "append_to_document"),
                    ["description"] = "操作类型（默认 insert_at_cursor）"
                },
                ["text"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "要插入/替换/追加的文本内容"
                }
            },
            ["required"] = new JArray("text")
        };

        public override Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            string text = RequireString(arguments, "text");
            string action = OptionalString(arguments, "action", "insert_at_cursor");
            var doc = RequireActiveDocument(connect);

            switch (action)
            {
                case "insert_at_cursor":
                    return InsertAtCursor(connect, text);

                case "replace_selection":
                    return ReplaceSelection(connect, text);

                case "append_to_document":
                    return AppendToDocument(connect, doc, text);

                default:
                    throw new ToolArgumentException(
                        $"未知 action: {action}，可选: insert_at_cursor, replace_selection, append_to_document");
            }
        }

        private Task<ToolExecutionResult> InsertAtCursor(Connect connect, string text)
        {
            using (BeginTrackRevisions(connect))
            {
                connect.WordApplication.Selection.TypeText(text);
            }
            return Task.FromResult(
                ToolExecutionResult.Ok($"已在光标位置插入 {text.Length} 个字符。"));
        }

        private Task<ToolExecutionResult> ReplaceSelection(Connect connect, string text)
        {
            var selection = connect.WordApplication.Selection;
            if (string.IsNullOrEmpty(selection?.Text?.Trim()))
                throw new ToolArgumentException("没有选中的文本");

            int oldLen = selection.Text.Length;
            using (BeginTrackRevisions(connect))
            {
                selection.TypeText(text);
            }
            return Task.FromResult(
                ToolExecutionResult.Ok($"已将选中文本（{oldLen}字符）替换为新文本（{text.Length}字符）。"));
        }

        private Task<ToolExecutionResult> AppendToDocument(Connect connect, NetOffice.WordApi.Document doc, string text)
        {
            var endRange = doc.Range(doc.Content.End - 1, doc.Content.End - 1);
            using (BeginTrackRevisions(connect))
            {
                endRange.InsertAfter(text);
            }
            return Task.FromResult(
                ToolExecutionResult.Ok($"已在文档末尾追加 {text.Length} 个字符。"));
        }
    }
}
