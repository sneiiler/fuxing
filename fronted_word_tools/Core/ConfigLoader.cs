using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;

namespace FuXing
{
    public class ConfigLoader
    {
        public class Config
        {
            /// <summary>大模型服务器 Base URL（如 http://127.0.0.1:8000）</summary>
            public string BaseURL { get; set; } = "http://127.0.0.1:8000";

            /// <summary>API Key / Secret Key</summary>
            public string ApiKey { get; set; } = "";

            /// <summary>模型名称</summary>
            public string ModelName { get; set; } = "";

            /// <summary>开发者模式（显示调试日志、额外诊断信息等）</summary>
            public bool DeveloperMode { get; set; } = false;

            /// <summary>上下文窗口限制（token 数），可选档位：32000 / 64000 / 128000</summary>
            public int ContextWindowLimit { get; set; } = 128000;

            /// <summary>智能体最大迭代轮次（工具调用循环上限），可选档位：5 / 10 / 20 / 30</summary>
            public int MaxToolRounds { get; set; } = 10;

            /// <summary>危险操作审批：执行脚本、批量操作、删除章节等高风险工具前弹窗确认</summary>
            public bool RequireApprovalForDangerousTools { get; set; } = true;
        }

        // 获取文档目录路径
        private string GetDocumentsFolder()
        {
            string documentsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string configFilePath = Path.Combine(documentsFolder, "fuxing_config.json");
            return configFilePath;
        }

        // 加载配置
        public Config LoadConfig()
        {
            try
            {
                string configFilePath = GetDocumentsFolder();

                if (File.Exists(configFilePath))
                {
                    var json = File.ReadAllText(configFilePath);
                    var config = JsonConvert.DeserializeObject<Config>(json);
                    return config ?? new Config();
                }
                else
                {
                    Debug.WriteLine("Config file not found, returning default config.");
                    return new Config();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading config: {ex.Message}");
                return new Config();
            }
        }

        // 保存配置
        public void SaveConfig(Config config)
        {
            try
            {
                string configFilePath = GetDocumentsFolder();
                var json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(configFilePath, json);
                Debug.WriteLine($"Config saved to: {configFilePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving config: {ex.Message}");
            }
        }
    }
}