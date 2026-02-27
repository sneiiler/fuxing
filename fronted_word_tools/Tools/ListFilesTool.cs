using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>列出指定目录下的文档文件</summary>
    public class ListFilesTool : ToolBase
    {
        public override string Name => "list_files";
        public override string DisplayName => "列出文档文件";
        public override ToolCategory Category => ToolCategory.Query;

        public override string Description =>
            "List document files (.docx/.doc) in a specified directory, returning file name, size, and modification time. " +
            "Useful for discovering sub-documents to merge. " +
            "When folder_path is empty, defaults to the directory of the currently open document.";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["folder_path"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "目录路径，例如 D:\\合稿\\子文档。为空则默认使用当前打开文档所在目录"
                },
                ["extension_filter"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "文件扩展名过滤，逗号分隔，默认 .docx,.doc"
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

            string extensionFilter = arguments?["extension_filter"]?.ToString() ?? ".docx,.doc";
            var extensions = new HashSet<string>(
                extensionFilter.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.OrdinalIgnoreCase);

            var lines = new List<string> { $"目录: {folderPath}", "" };
            int index = 0;

            foreach (var filePath in Directory.GetFiles(folderPath))
            {
                string ext = Path.GetExtension(filePath);
                if (!extensions.Contains(ext))
                    continue;

                var info = new FileInfo(filePath);
                index++;
                string sizeText = info.Length < 1024 * 1024
                    ? $"{info.Length / 1024.0:F1} KB"
                    : $"{info.Length / (1024.0 * 1024.0):F1} MB";

                lines.Add($"{index}. {info.Name}  ({sizeText}, 修改于 {info.LastWriteTime:yyyy-MM-dd HH:mm})");
            }

            if (index == 0)
                return Task.FromResult(ToolExecutionResult.Ok($"目录 {folderPath} 下没有找到匹配的文档文件。"));

            lines.Add("");
            lines.Add($"共 {index} 个文件");

            return Task.FromResult(ToolExecutionResult.Ok(string.Join("\n", lines)));
        }
    }
}
