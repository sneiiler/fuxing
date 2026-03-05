using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;

namespace FuXingAgent.Tools
{
    public class ListFilesTool
    {
        private readonly Connect _connect;
        public ListFilesTool(Connect connect) => _connect = connect;

        [Description("List files in directory (name, size, date). Defaults to current document's directory. Use extension_filter to narrow down (e.g. '.png,.jpg'). Set recursive=true to search subdirectories.")]
        public string list_files(
            [Description("目录路径，空则用当前文档目录")] string folder_path = null,
            [Description("文件扩展名过滤，逗号分隔（如 .png,.jpg）")] string extension_filter = null,
            [Description("是否递归搜索子目录")] bool recursive = false)
        {
            if (string.IsNullOrWhiteSpace(folder_path))
            {
                var app = _connect.WordApplication;
                if (app?.Documents.Count > 0)
                {
                    string docPath = app.ActiveDocument.FullName;
                    if (!string.IsNullOrEmpty(docPath))
                        folder_path = Path.GetDirectoryName(docPath);
                }
                if (string.IsNullOrWhiteSpace(folder_path))
                    throw new InvalidOperationException("无法确定目录：未打开文档且未指定 folder_path");
            }

            if (!Directory.Exists(folder_path))
                throw new InvalidOperationException($"目录不存在: {folder_path}");

            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = Directory.GetFiles(folder_path, "*.*", option);

            HashSet<string> extensions = null;
            if (!string.IsNullOrWhiteSpace(extension_filter))
            {
                extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var ext in extension_filter.Split(','))
                {
                    string e = ext.Trim();
                    if (!e.StartsWith(".")) e = "." + e;
                    extensions.Add(e);
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"目录: {folder_path}");
            int count = 0;
            const int maxResults = 200;

            foreach (var file in files)
            {
                if (count >= maxResults)
                {
                    sb.AppendLine($"... 已达上限 {maxResults} 条，实际文件更多");
                    break;
                }

                if (extensions != null && !extensions.Contains(Path.GetExtension(file)))
                    continue;

                var fi = new FileInfo(file);
                string size = fi.Length >= 1048576
                    ? $"{fi.Length / 1048576.0:F1} MB"
                    : $"{fi.Length / 1024.0:F1} KB";
                string date = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
                string name = recursive ? fi.FullName.Substring(folder_path.Length).TrimStart('\\') : fi.Name;

                sb.AppendLine($"  [{++count}] {name}  ({size}, {date})");
            }

            if (count == 0)
                sb.AppendLine("  （没有匹配的文件）");

            return sb.ToString();
        }
    }
}