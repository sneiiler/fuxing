using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Diagnostics;

namespace FuXingAgent.Core
{
    /// <summary>
    /// 资源管理器 - 图标和其他资源文件
    /// </summary>
    public static class ResourceManager
    {
        private static readonly string ResourcesPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
            "Resources");

        public static Image GetIcon(string iconName)
        {
            try
            {
                string iconPath = Path.Combine(ResourcesPath, iconName);
                if (File.Exists(iconPath))
                {
                    using (var fs = new FileStream(iconPath, FileMode.Open, FileAccess.Read))
                        return Image.FromStream(fs);
                }
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ResourceManager] GetIcon 错误: {ex.Message}");
                return null;
            }
        }

        public static object GetIconAsPictureDisp(string iconName)
        {
            try
            {
                using (var image = GetIcon(iconName))
                {
                    if (image != null)
                        return ImageConverter.ImageToPictureDisp(image);
                }
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ResourceManager] GetIconAsPictureDisp 错误: {ex.Message}");
                return null;
            }
        }

        public static void CopyResourcesToOutput()
        {
            // 图标已通过 MSBuild Content CopyToOutputDirectory 部署到 DLL 同级 Resources/ 目录
            if (!Directory.Exists(ResourcesPath))
                Debug.WriteLine($"[ResourceManager] 资源目录不存在: {ResourcesPath}");
        }

        public static string GetResourceStatus()
        {
            var status = new System.Text.StringBuilder();
            status.AppendLine($"资源路径: {ResourcesPath}");
            status.AppendLine($"路径存在: {Directory.Exists(ResourcesPath)}");
            if (Directory.Exists(ResourcesPath))
            {
                var files = Directory.GetFiles(ResourcesPath, "*.png");
                status.AppendLine($"PNG 文件数量: {files.Length}");
            }
            return status.ToString();
        }

        private class ImageConverter : System.Windows.Forms.AxHost
        {
            private ImageConverter() : base("00000000-0000-0000-0000-000000000000") { }

            public static object ImageToPictureDisp(Image image)
            {
                try { return GetIPictureDispFromPicture(image); }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ImageConverter] 转换错误: {ex.Message}");
                    return null;
                }
            }
        }
    }
}
