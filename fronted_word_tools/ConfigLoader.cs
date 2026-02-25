using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;

namespace WordTools
{
    public class ConfigLoader
    {
        public class Config
        {
            public string llmServerIP { get; set; } = "127.0.0.1";
            public int llmServerPort { get; set; } = 11434;
            public int UpdatePort { get; set; } = 11450;
            public int OtherPort { get; set; } = 0;
            public string CheckStandardIP { get; set; } = "192.168.1.1";
            public int CheckStandardPort { get; set; } = 80;
            // 新增：OpenAI兼容API服务器配置
            public string OpenAIServerIP { get; set; } = "127.0.0.1";
            public int OpenAIServerPort { get; set; } = 8000;
        }

        // 获取文档目录路径
        private string GetDocumentsFolder()
        {
            string documentsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string configFilePath = Path.Combine(documentsFolder, "office_tools_config.json");
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