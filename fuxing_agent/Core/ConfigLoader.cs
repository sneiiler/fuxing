using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;

namespace FuXingAgent.Core
{
    public class ConfigLoader
    {
        public class Config
        {
            public string BaseURL { get; set; } = "http://127.0.0.1:8000";
            public string ApiKey { get; set; } = "";
            public string ModelName { get; set; } = "";
            public bool DeveloperMode { get; set; } = false;
            public int ContextWindowLimit { get; set; } = 128000;
            public int MaxToolRounds { get; set; } = 50;
            public bool RequireApprovalForDangerousTools { get; set; } = true;
            public bool ShowStartupWarning { get; set; } = true;
            public int MaxScriptIterations { get; set; } = 200;
        }

        private string GetConfigFilePath()
        {
            string documentsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(documentsFolder, "fuxing_config.json");
        }

        public Config LoadConfig()
        {
            try
            {
                string configFilePath = GetConfigFilePath();
                if (File.Exists(configFilePath))
                {
                    var json = File.ReadAllText(configFilePath);
                    return JsonConvert.DeserializeObject<Config>(json) ?? new Config();
                }
                return new Config();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading config: {ex.Message}");
                return new Config();
            }
        }

        public void SaveConfig(Config config)
        {
            try
            {
                string configFilePath = GetConfigFilePath();
                var json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(configFilePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving config: {ex.Message}");
            }
        }
    }
}
