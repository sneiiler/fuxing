using System;
using System.ComponentModel;
using Word = Microsoft.Office.Interop.Word;

namespace FuXingAgent.Tools
{
    public class AddCommentTool
    {
        private readonly Connect _connect;
        public AddCommentTool(Connect connect) => _connect = connect;

        [Description("Add review comment on selected text or searched text (target: selection/search). Use instead of direct editing when suggesting changes for user review.")]
        public string add_comment(
            [Description("批注内容")] string comment,
            [Description("定位方式: selection 或 search")] string target = "selection",
            [Description("要批注的文本（target=search 时必填）")] string search_text = null)
        {
            if (string.IsNullOrWhiteSpace(comment))
                throw new ArgumentException("缺少批注内容");

            var app = _connect.WordApplication;
            var doc = app.ActiveDocument ?? throw new InvalidOperationException("没有活动文档");

            Word.Range targetRange;

            if (target == "search")
            {
                if (string.IsNullOrWhiteSpace(search_text))
                    throw new ArgumentException("target=search 时必须提供 search_text");

                var range = doc.Content;
                range.Find.ClearFormatting();
                range.Find.Text = search_text;
                range.Find.Forward = true;
                range.Find.Wrap = Word.WdFindWrap.wdFindStop;
                if (!range.Find.Execute())
                    throw new InvalidOperationException($"未找到文件: {search_text}");
                targetRange = range;
            }
            else
            {
                var sel = app.Selection;
                if (string.IsNullOrEmpty(sel?.Text?.Trim()))
                    throw new InvalidOperationException("没有选中的文本，请先选中要批注的内容");
                targetRange = sel.Range;
            }

            doc.Comments.Add(targetRange, comment);
            string preview = targetRange.Text;
            if (preview != null && preview.Length > 50)
                preview = preview.Substring(0, 47) + "...";

            return $"已在「{preview}」处添加批注";
        }
    }
}