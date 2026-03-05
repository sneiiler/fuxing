using Microsoft.Extensions.AI;
using FuXingAgent.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FuXingAgent.Agents
{
    /// <summary>
    /// Agent 系统初始化与生命周期管理。
    /// 使用 Microsoft.Extensions.AI 的 IChatClient 驱动 LLM 通信，
    /// 结合 Microsoft.Agents.AI 的编排能力管理 Agent 工作流。
    /// </summary>
    public sealed class AgentBootstrap
    {
        private readonly ConfigLoader _configLoader;
        private IChatClient _chatClient;
        private string _currentModel;

        public IChatClient ChatClient => _chatClient;
        public string CurrentModel => _currentModel;

        public AgentBootstrap(ConfigLoader configLoader)
        {
            _configLoader = configLoader;
        }

        /// <summary>根据配置创建或刷新 IChatClient</summary>
        public void Initialize()
        {
            var config = _configLoader.LoadConfig();
            BuildChatClient(config);
        }

        /// <summary>配置变更时重建 IChatClient</summary>
        public void Refresh()
        {
            var config = _configLoader.LoadConfig();
            BuildChatClient(config);
        }

        private void BuildChatClient(ConfigLoader.Config config)
        {
            string baseUrl = (config.BaseURL ?? "http://127.0.0.1:8000").TrimEnd('/');
            string apiKey = config.ApiKey ?? "dummy";
            _currentModel = (config.ModelName ?? "").Trim();

            // 使用 OpenAI 兼容的 IChatClient
            var openAiClient = new OpenAI.OpenAIClient(
                new System.ClientModel.ApiKeyCredential(apiKey),
                new OpenAI.OpenAIClientOptions { Endpoint = new Uri(baseUrl) });

            _chatClient = openAiClient
                .GetChatClient(_currentModel)
                .AsIChatClient();
        }
    }
}
