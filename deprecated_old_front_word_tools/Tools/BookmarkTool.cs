using Newtonsoft.Json.Linq;
using System;
using System.Text;
using NetOffice.WordApi;
using NetOffice.WordApi.Enums;

namespace FuXing
{
    /// <summary>
    /// 书签管理工具，支持创建、删除、跳转和列出书签。
    /// 书签是交叉引用的基础设施。
    /// </summary>
    public class BookmarkTool : ToolBase
    {
        public override string Name => "bookmark";
        public override string DisplayName => "书签管理";
        public override ToolCategory Category => ToolCategory.Structure;

        public override string Description =>
            "Manage bookmarks (anchors for cross_reference). Actions: add/delete/goto/list. " +
            "Name must be alphanumeric+underscore, no spaces.";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["action"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray("add", "delete", "goto", "list"),
                    ["description"] = "操作类型"
                },
                ["name"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "书签名称（仅支持字母、数字和下划线，不能有空格）"
                }
            },
            ["required"] = new JArray("action")
        };

        public override System.Threading.Tasks.Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            var doc = RequireActiveDocument(connect);
            string action = RequireString(arguments, "action");

            switch (action)
            {
                case "add":
                {
                    string name = RequireString(arguments, "name");
                    var selection = connect.WordApplication.Selection;
                    doc.Bookmarks.Add(name, selection.Range);
                    string preview = selection.Text;
                    if (preview != null && preview.Length > 50)
                        preview = preview.Substring(0, 47) + "...";
                    return System.Threading.Tasks.Task.FromResult(
                        ToolExecutionResult.Ok($"已创建书签 \"{name}\"，位置: 「{preview}」"));
                }

                case "delete":
                {
                    string name = RequireString(arguments, "name");
                    if (!doc.Bookmarks.Exists(name))
                        throw new ToolArgumentException($"书签不存在: {name}");
                    doc.Bookmarks[name].Delete();
                    return System.Threading.Tasks.Task.FromResult(
                        ToolExecutionResult.Ok($"已删除书签 \"{name}\""));
                }

                case "goto":
                {
                    string name = RequireString(arguments, "name");
                    if (!doc.Bookmarks.Exists(name))
                        throw new ToolArgumentException($"书签不存在: {name}");
                    var bookmark = doc.Bookmarks[name];
                    bookmark.Range.Select();
                    return System.Threading.Tasks.Task.FromResult(
                        ToolExecutionResult.Ok($"已跳转到书签 \"{name}\""));
                }

                case "list":
                {
                    int count = doc.Bookmarks.Count;
                    if (count == 0)
                        return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Ok("文档中没有书签。"));

                    var sb = new StringBuilder();
                    sb.AppendLine($"文档中共 {count} 个书签:");
                    for (int i = 1; i <= count; i++)
                    {
                        var bm = doc.Bookmarks[i];
                        string preview = bm.Range.Text;
                        if (preview != null && preview.Length > 40)
                            preview = preview.Substring(0, 37) + "...";
                        sb.AppendLine($"  {i}. \"{bm.Name}\" → 「{preview}」");
                    }
                    return System.Threading.Tasks.Task.FromResult(ToolExecutionResult.Ok(sb.ToString().TrimEnd()));
                }

                default:
                    throw new ToolArgumentException($"未知 action: {action}，可选: add, delete, goto, list");
            }
        }
    }
}
