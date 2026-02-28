using Newtonsoft.Json.Linq;
using NetOffice.WordApi;
using NetOffice.WordApi.Enums;

namespace FuXing
{
    /// <summary>全文查找替换工具，支持普通文本和通配符匹配</summary>
    public class SearchAndReplaceTool : ToolBase
    {
        public override string Name => "search_and_replace";
        public override string DisplayName => "查找替换";
        public override ToolCategory Category => ToolCategory.Editing;

        public override string Description =>
            "Find and replace known text in the document. Supports wildcards (use_wildcards) and scope control (all/first). " +
            "NOT for error detection/proofreading — use correct_text instead.";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["find_text"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "要查找的文本"
                },
                ["replace_text"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "替换为的文本"
                },
                ["match_case"] = new JObject
                {
                    ["type"] = "boolean",
                    ["description"] = "是否区分大小写（默认 false）"
                },
                ["match_whole_word"] = new JObject
                {
                    ["type"] = "boolean",
                    ["description"] = "是否全字匹配（默认 false）"
                },
                ["use_wildcards"] = new JObject
                {
                    ["type"] = "boolean",
                    ["description"] = "是否启用 Word 通配符模式（默认 false）"
                },
                ["scope"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray("all", "first"),
                    ["description"] = "替换范围：all=全部替换（默认），first=仅替换首个"
                }
            },
            ["required"] = new JArray("find_text", "replace_text")
        };

        public override System.Threading.Tasks.Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            string findText = arguments["find_text"]?.ToString();
            string replaceText = arguments["replace_text"]?.ToString();

            if (string.IsNullOrEmpty(findText))
                return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Fail("缺少 find_text 参数"));
            if (replaceText == null)
                return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Fail("缺少 replace_text 参数"));

            bool matchCase = arguments["match_case"] != null && (bool)arguments["match_case"];
            bool matchWholeWord = arguments["match_whole_word"] != null && (bool)arguments["match_whole_word"];
            bool useWildcards = arguments["use_wildcards"] != null && (bool)arguments["use_wildcards"];
            string scope = arguments["scope"]?.ToString() ?? "all";

            var app = connect.WordApplication;
            var doc = app.ActiveDocument;

            using (BeginTrackRevisions(connect))
            {
                if (scope == "all")
                {
                    int count = CountMatches(doc, findText, matchCase, matchWholeWord, useWildcards);
                    ExecuteReplace(doc, findText, replaceText, matchCase, matchWholeWord, useWildcards, WdReplace.wdReplaceAll);
                    return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Ok(
                        $"已替换 {count} 处「{findText}」→「{replaceText}」"));
                }
                else
                {
                    bool found = ExecuteReplace(doc, findText, replaceText, matchCase, matchWholeWord, useWildcards, WdReplace.wdReplaceOne);
                    return System.Threading.Tasks.Task.FromResult(found
                        ? ToolExecutionResult.Ok($"已替换首个「{findText}」→「{replaceText}」")
                        : ToolExecutionResult.Fail($"未找到「{findText}」"));
                }
            }
        }

        private bool ExecuteReplace(Document doc, string findText, string replaceText,
            bool matchCase, bool matchWholeWord, bool useWildcards, WdReplace replaceMode)
        {
            var range = doc.Content;
            range.Find.ClearFormatting();
            range.Find.Replacement.ClearFormatting();
            // 使用全位置参数调用 Execute
            return range.Find.Execute(
                findText, matchCase, matchWholeWord, useWildcards,
                false, false, true,
                replaceMode == WdReplace.wdReplaceAll ? WdFindWrap.wdFindContinue : WdFindWrap.wdFindStop,
                false, replaceText, replaceMode);
        }

        private int CountMatches(Document doc, string findText,
            bool matchCase, bool matchWholeWord, bool useWildcards)
        {
            int count = 0;
            var range = doc.Content;
            range.Find.ClearFormatting();
            range.Find.Text = findText;
            range.Find.Forward = true;
            range.Find.Wrap = WdFindWrap.wdFindStop;
            range.Find.MatchCase = matchCase;
            range.Find.MatchWholeWord = matchWholeWord;
            range.Find.MatchWildcards = useWildcards;

            while (range.Find.Execute())
            {
                count++;
                range.Start = range.End;
                range.End = doc.Content.End;
                range.Find.ClearFormatting();
                range.Find.Text = findText;
                range.Find.Forward = true;
                range.Find.Wrap = WdFindWrap.wdFindStop;
                range.Find.MatchCase = matchCase;
                range.Find.MatchWholeWord = matchWholeWord;
                range.Find.MatchWildcards = useWildcards;
            }
            return count;
        }
    }
}
