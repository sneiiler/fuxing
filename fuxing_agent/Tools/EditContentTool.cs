using System;
using System.ComponentModel;
using FuXingAgent.Core;
using Word = Microsoft.Office.Interop.Word;

namespace FuXingAgent.Tools
{
    public class EditContentTool
    {
        private readonly Connect _connect;
        public EditContentTool(Connect connect) => _connect = connect;

        [Description("Find and replace text in the document. Returns the number of replacements made. " +
            "replace_text=\"\" means delete. Uses Track Changes mode. " +
            "When use_wildcards=true, Word wildcard syntax applies: " +
            "? = single char, * = any string, [abc] = char class, [!abc] = negated class, " +
            "{n} = exactly n, {n,} = at least n, {n,m} = between n and m, " +
            "\\1 \\2 etc in replace_text for back-references.")]
        public string edit_content(
            [Description("查找文本")] string find_text,
            [Description("替换文本（空字符串=删除匹配内容）")] string replace_text = "",
            [Description("区分大小写")] bool match_case = true,
            [Description("使用 Word 通配符匹配")] bool use_wildcards = false)
        {
            if (string.IsNullOrEmpty(find_text))
                throw new ArgumentException("缺少 find_text");

            var app = _connect.WordApplication;
            var doc = app.ActiveDocument ?? throw new InvalidOperationException("没有活动文档");

            replace_text = (replace_text ?? "").Replace("\r\n", "\r").Replace("\n", "\r");

            int count = 0;
            object findObj = find_text;
            object replaceObj = replace_text;
            object matchCaseObj = match_case;
            object matchWholeWordObj = (object)false;
            object useWildcardsObj = use_wildcards;
            object forwardObj = true;
            object wdFindStop = Word.WdFindWrap.wdFindStop;
            object replaceOne = Word.WdReplace.wdReplaceOne;
            object missing = Type.Missing;

            using (WordHelper.BeginTrackRevisions(app))
            {
                while (count <= 10000)
                {
                    var range = doc.Content;
                    range.Find.ClearFormatting();
                    range.Find.Replacement.ClearFormatting();

                    bool found = range.Find.Execute(
                        ref findObj, ref matchCaseObj, ref matchWholeWordObj, ref useWildcardsObj,
                        ref missing, ref missing, ref forwardObj, ref wdFindStop,
                        ref missing, ref replaceObj, ref replaceOne,
                        ref missing, ref missing, ref missing, ref missing);

                    if (!found) break;
                    count++;
                }

                if (count > 10000)
                    throw new InvalidOperationException("替换次数超过 10000，已中止");
            }

            if (count == 0)
                return "未找到匹配文本";

            string action = string.IsNullOrEmpty(replace_text) ? "删除" : "替换";
            return $"已{action} {count} 处";
        }
    }
}