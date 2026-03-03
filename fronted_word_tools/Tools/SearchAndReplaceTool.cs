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
            "NOT for error detection/proofreading — use correct_text instead. " +
            "NOTE: This tool operates on normal body text only. " +
            "ContentControl placeholder text (e.g. \"单击或点击此处输入文字。\") cannot be replaced by this tool — " +
            "use edit_document_text with insert_at_node/replace_node_content instead.";

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

            // 如果目标文本存在于 ContentControl 占位符中，拒绝操作并引导正确做法
            if (IsContentControlPlaceholder(doc, findText))
            {
                return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Fail(
                    $"「{findText}」是 ContentControl 的占位符文本，无法通过查找替换修改。" +
                    "请使用 edit_document_text 的 replace_node_content 操作，通过 node_id 定位后直接写入内容。"));
            }

            using (BeginTrackRevisions(connect))
            {
                // wdFindStop：到达文档末尾即停止，不绕回。避免 CC 边界导致无限循环。
                var range = doc.Content;
                range.Find.ClearFormatting();
                range.Find.Replacement.ClearFormatting();

                var replaceMode = scope == "all" ? WdReplace.wdReplaceAll : WdReplace.wdReplaceOne;
                bool found = range.Find.Execute(
                    findText, matchCase, matchWholeWord, useWildcards,
                    false, false, true, WdFindWrap.wdFindStop,
                    false, replaceText, replaceMode);

                if (scope == "all")
                {
                    // wdReplaceAll + wdFindStop 返回 true 表示至少替换了一处
                    return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Ok(
                        found ? $"已替换所有「{findText}」→「{replaceText}」"
                              : $"未找到「{findText}」，无替换"));
                }
                else
                {
                    return System.Threading.Tasks.Task.FromResult(found
                        ? ToolExecutionResult.Ok($"已替换首个「{findText}」→「{replaceText}」")
                        : ToolExecutionResult.Fail($"未找到「{findText}」"));
                }
            }
        }

        /// <summary>
        /// 检查目标文本是否是某个 ContentControl 的占位符文本。
        /// CC 占位符是结构化控件的默认显示文本，不能通过 Find/Replace 操作，
        /// 必须通过 CC.Range.Text 赋值来写入真实内容。
        /// </summary>
        private static bool IsContentControlPlaceholder(Document doc, string findText)
        {
            foreach (ContentControl cc in doc.ContentControls)
            {
                if (!cc.ShowingPlaceholderText) continue;

                // 跳过我们自己的 fxg 锚点 CC
                string tag = cc.Tag;
                if (tag != null && tag.StartsWith(FuXing.Core.AnchorManager.TagPrefix)) continue;

                string ccText = cc.Range.Text;
                if (ccText != null && ccText.Contains(findText))
                    return true;
            }
            return false;
        }
    }
}
