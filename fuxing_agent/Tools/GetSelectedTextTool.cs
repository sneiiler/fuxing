using System.ComponentModel;

namespace FuXingAgent.Tools
{
    public class GetSelectedTextTool
    {
        private readonly Connect _connect;
        public GetSelectedTextTool(Connect connect) => _connect = connect;

        [Description("Get the currently selected text in the document. Returns the text content and character count.")]
        public string get_selected_text()
        {
            var app = _connect.WordApplication;
            if (app == null)
                throw new System.InvalidOperationException("Word 应用程序不可用");

            var sel = app.Selection;
            if (sel == null || sel.Start == sel.End)
                return "当前没有选中任何文本。";

            string text = sel?.Text;

            if (string.IsNullOrEmpty(text) || text.Trim().Length == 0)
                return "当前没有选中任何文本。";

            return $"选中文本（{text.Length} 字符）：\n{text}";
        }
    }
}