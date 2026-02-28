using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>列出指定目录下的文件</summary>
    public class ListFilesTool : ToolBase
    {
        public override string Name => "list_files";
        public override string DisplayName => "列出目录文件";
        public override ToolCategory Category => ToolCategory.Query;

        public override string Description =>
            "List files in directory (name, size, date). " +
            "Defaults to current document's directory. Lists ALL files by default; " +
            "use extension_filter to narrow down (e.g. '.png,.jpg'). " +
            "Set recursive=true to search subdirectories.";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["folder_path"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "目录路径，空则用当前文档目录"
                },
                ["extension_filter"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "文件扩展名过滤，逗号分隔（如 .png,.jpg）。空则列出所有文件"
                },
                ["recursive"] = new JObject
                {
                    ["type"] = "boolean",
                    ["description"] = "是否递归搜索子目录（默认 false）"
                }
            }
        };

        public override Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            string folderPath = arguments?["folder_path"]?.ToString();

            // folder_path 为空时，默认使用当前打开文档所在目录
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                var app = connect.WordApplication;
                if (app.Documents.Count > 0)
                {
                    string docPath = app.ActiveDocument.FullName;
                    if (!string.IsNullOrEmpty(docPath))
                        folderPath = Path.GetDirectoryName(docPath);
                }

                if (string.IsNullOrWhiteSpace(folderPath))
                    return Task.FromResult(ToolExecutionResult.Fail("未指定目录且当前没有打开的文档，无法确定默认目录"));
            }

            if (!Directory.Exists(folderPath))
                return Task.FromResult(ToolExecutionResult.Fail($"目录不存在: {folderPath}"));

            string extensionFilter = arguments?["extension_filter"]?.ToString();
            HashSet<string> extensions = null;
            if (!string.IsNullOrWhiteSpace(extensionFilter))
            {
                extensions = new HashSet<string>(
                    extensionFilter.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries),
                    StringComparer.OrdinalIgnoreCase);
            }

            bool recursive = arguments?["recursive"]?.Value<bool>() == true;
            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            var lines = new List<string> { $"目录: {folderPath}{(recursive ? "（递归搜索）" : "")}", "" };
            int index = 0;

            foreach (var filePath in Directory.GetFiles(folderPath, "*.*", searchOption))
            {
                string ext = Path.GetExtension(filePath);
                if (extensions != null && !extensions.Contains(ext))
                    continue;

                var info = new FileInfo(filePath);
                index++;
                string sizeText = info.Length < 1024 * 1024
                    ? $"{info.Length / 1024.0:F1} KB"
                    : $"{info.Length / (1024.0 * 1024.0):F1} MB";

                // 递归搜索时显示相对路径，非递归时显示文件名
                string displayName = recursive
                    ? filePath.Substring(folderPath.Length).TrimStart(Path.DirectorySeparatorChar)
                    : info.Name;

                lines.Add($"{index}. {displayName}  ({sizeText}, 修改于 {info.LastWriteTime:yyyy-MM-dd HH:mm})");

                // 递归搜索时限制结果数量，避免输出过多
                if (recursive && index >= 200)
                {
                    lines.Add("... (结果已截断，共超过200个文件，请添加 extension_filter 缩小范围)");
                    break;
                }
            }

            if (index == 0)
                return Task.FromResult(ToolExecutionResult.Ok($"目录 {folderPath} 下没有找到匹配的文件。"));

            lines.Add("");
            lines.Add($"共 {index} 个文件");

            return Task.FromResult(ToolExecutionResult.Ok(string.Join("\n", lines)));
        }
    }
}
