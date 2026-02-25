using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Diagnostics;

namespace WordTools
{
    /// <summary>
    /// 资源管理器 - 处理图标和其他资源文件
    /// </summary>
    public static class ResourceManager
    {
        private static readonly string ResourcesPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), 
            "Resources");

        /// <summary>
        /// 获取图标Base64编码字符串，用于Ribbon XML
        /// </summary>
        /// <param name="iconName">图标文件名，不含路径</param>
        /// <returns>Base64编码图片数据</returns>
        public static string GetIconBase64(string iconName)
        {
            try
            {
                string iconPath = Path.Combine(ResourcesPath, iconName);
                Debug.WriteLine($"[ResourceManager] 尝试获取Base64图标: {iconPath}");
                
                if (File.Exists(iconPath))
                {
                    byte[] imageBytes = File.ReadAllBytes(iconPath);
                    string base64 = Convert.ToBase64String(imageBytes);
                    Debug.WriteLine($"[ResourceManager] Base64图标获取成功，长度: {base64.Length}");
                    return base64;
                }
                else
                {
                    Debug.WriteLine($"[ResourceManager] 图标文件不存在: {iconPath}");
                }
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ResourceManager] GetIconBase64错误: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 获取图标Image对象
        /// </summary>
        /// <param name="iconName">图标文件名</param>
        /// <returns>Image对象</returns>
        public static Image GetIcon(string iconName)
        {
            try
            {
                string iconPath = Path.Combine(ResourcesPath, iconName);
                Debug.WriteLine($"[ResourceManager] 尝试获取图标: {iconPath}");
                
                if (File.Exists(iconPath))
                {
                    // 使用更安全的方式加载图片，避免文件锁定
                    using (var fs = new FileStream(iconPath, FileMode.Open, FileAccess.Read))
                    {
                        var image = Image.FromStream(fs);
                        Debug.WriteLine($"[ResourceManager] 图标加载成功: {iconName}, 大小: {image.Width}x{image.Height}");
                        return image;
                    }
                }
                else
                {
                    Debug.WriteLine($"[ResourceManager] 图标文件不存在: {iconPath}");
                }
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ResourceManager] GetIcon错误: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 将Image转换为IPictureDisp，用于Ribbon图标
        /// </summary>
        /// <param name="image">要转换的图片</param>
        /// <returns>IPictureDisp对象</returns>
        public static object ImageToPictureDisp(Image image)
        {
            try
            {
                if (image == null) return null;
                
                Debug.WriteLine($"[ResourceManager] 转换图片为IPictureDisp，大小: {image.Width}x{image.Height}");
                
                // 使用System.Windows.Forms.AxHost来转换
                return ImageConverter.ImageToPictureDisp(image);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ResourceManager] ImageToPictureDisp错误: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 获取从图标名称到IPictureDisp的完整转换
        /// </summary>
        /// <param name="iconName">图标文件名</param>
        /// <returns>IPictureDisp对象</returns>
        public static object GetIconAsPictureDisp(string iconName)
        {
            try
            {
                using (var image = GetIcon(iconName))
                {
                    if (image != null)
                    {
                        return ImageToPictureDisp(image);
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ResourceManager] GetIconAsPictureDisp错误: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 检查Resources文件夹中图标文件是否存在
        /// </summary>
        /// <returns>资源状态信息</returns>
        public static string GetResourceStatus()
        {
            var status = new System.Text.StringBuilder();
            status.AppendLine($"资源路径: {ResourcesPath}");
            status.AppendLine($"路径存在: {Directory.Exists(ResourcesPath)}");
            
            if (Directory.Exists(ResourcesPath))
            {
                var files = Directory.GetFiles(ResourcesPath, "*.png");
                status.AppendLine($"PNG文件数量: {files.Length}");
                
                var requiredIcons = new[]
                {
                    "icons8-better-150.png",
                    "icons8-deepseek-150.png",
                    "icons8-spellcheck-70.png", 
                    "icons8-spellcheck-all-100.png",
                    "icons8-table_single-96.png",
                    "icons8-table_all-100.png",
                    "icons8-taskpane-128.png",
                    "icons8-setting-128.png",
                    "icons8-clean-96.png"
                };

                status.AppendLine("\n必需图标文件:");
                foreach (var icon in requiredIcons)
                {
                    var exists = File.Exists(Path.Combine(ResourcesPath, icon));
                    status.AppendLine($"  {icon}: {(exists ? "?" : "?")}");
                }
            }
            
            return status.ToString();
        }

        /// <summary>
        /// 复制Resources文件夹到输出目录
        /// </summary>
        public static void CopyResourcesToOutput()
        {
            try
            {
                string sourceDir = "Resources";
                string targetDir = Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), 
                    "Resources");

                Debug.WriteLine($"[ResourceManager] 复制资源文件: {sourceDir} -> {targetDir}");

                if (Directory.Exists(sourceDir) && !Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                    
                    foreach (string file in Directory.GetFiles(sourceDir))
                    {
                        string fileName = Path.GetFileName(file);
                        string destFile = Path.Combine(targetDir, fileName);
                        File.Copy(file, destFile, true);
                        Debug.WriteLine($"[ResourceManager] 已复制: {fileName}");
                    }
                }
                else if (Directory.Exists(targetDir))
                {
                    Debug.WriteLine("[ResourceManager] 目标资源目录已存在");
                }
                else
                {
                    Debug.WriteLine($"[ResourceManager] 源资源目录不存在: {sourceDir}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ResourceManager] 复制资源文件错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 内部图片转换器类
        /// </summary>
        private class ImageConverter : System.Windows.Forms.AxHost
        {
            private ImageConverter() : base("00000000-0000-0000-0000-000000000000") { }
            
            public static object ImageToPictureDisp(Image image)
            {
                try
                {
                    return GetIPictureDispFromPicture(image);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ImageConverter] 转换错误: {ex.Message}");
                    return null;
                }
            }
        }
    }
}