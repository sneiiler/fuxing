using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Threading;
using System.Net.Http;

namespace FuXing
{
    public class NetWorkHelper
    {
        private string _baseUrl;
        private string _apiKey;
        private string _modelName;
        private bool _developerMode;
        private static readonly HttpClient _httpClient;

        static NetWorkHelper()
        {
            // 启用 TLS 1.2，现代 HTTPS API 要求此协议
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(120); // 流式请求需要较长超时
        }

        public NetWorkHelper()
        {
            LoadConfiguration();
        }

        // 从配置文件加载配置
        private void LoadConfiguration()
        {
            try
            {
                var configLoader = new ConfigLoader();
                var config = configLoader.LoadConfig();

                _baseUrl = (config.BaseURL ?? "http://127.0.0.1:8000").TrimEnd('/');
                _apiKey = config.ApiKey ?? "";
                _modelName = (config.ModelName ?? "").Trim();
                _developerMode = config.DeveloperMode;
            }
            catch (Exception ex)
            {
                _baseUrl = "http://127.0.0.1:8000";
                _apiKey = "";
                _modelName = "";
                _developerMode = false;
                System.Diagnostics.Debug.WriteLine($"Failed to load configuration: {ex.Message}");
            }
        }

        public class ChatCompletionChunk
        {
            public string id { get; set; }
            public string @object { get; set; }
            public long created { get; set; }
            public string model { get; set; }
            public Choice[] choices { get; set; }
        }

        public class Choice
        {
            public int index { get; set; }
            public Delta delta { get; set; }
            public string finish_reason { get; set; }
        }

        public class Delta
        {
            public string role { get; set; }
            public string content { get; set; }
            public ToolCallDelta[] tool_calls { get; set; }
        }

        public class ToolCallDelta
        {
            public int index { get; set; }
            public string id { get; set; }
            public string type { get; set; }
            public FunctionCallDelta function { get; set; }
        }

        public class FunctionCallDelta
        {
            public string name { get; set; }
            public string arguments { get; set; }
        }

        /// <summary>
        /// 流式请求结果 — 可能返回文本内容或工具调用
        /// </summary>
        public class StreamChatResult
        {
            public string Content { get; set; }
            public List<ToolCallRequest> ToolCalls { get; set; }
            public bool HasToolCalls => ToolCalls != null && ToolCalls.Count > 0;
            /// <summary>最终的 finish_reason（"stop", "tool_calls", "length" 等）</summary>
            public string FinishReason { get; set; }
            /// <summary>响应是否因 max_tokens 被截断</summary>
            public bool IsTruncated => FinishReason == "length";
        }

        /// <summary>
        /// 带上下文记忆的流式聊天请求（已重构为使用官方 OpenAI SDK）
        /// </summary>
        public async Task<StreamChatResult> SendStreamChatWithMemoryAsync(
            ChatMemory memory,
            JArray tools,
            Action<string> onChunkReceived,
            Action<string> onError, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_baseUrl))
            {
                onError?.Invoke("未配置 Base URL，请在设置中配置服务器地址");
                return null;
            }
            if (string.IsNullOrWhiteSpace(_modelName))
            {
                onError?.Invoke("未配置模型名称，请在设置中选择或输入模型");
                return null;
            }

            try
            {
                var options = new OpenAIClientOptions { Endpoint = new Uri(_baseUrl) };
                var openaiClient = new OpenAIClient(new ApiKeyCredential(_apiKey ?? "dummy"), options);
                var chatClient = openaiClient.GetChatClient(_modelName);

                var messages = new List<ChatMessage>();
                foreach (var msg in memory.PrepareMessages())
                {
                    if (msg.Role == ChatMessageRole.System)
                        messages.Add(new SystemChatMessage(msg.Content));
                    else if (msg.Role == ChatMessageRole.User)
                        messages.Add(new UserChatMessage(msg.Content));
                    else if (msg.Role == ChatMessageRole.Assistant)
                    {
                        if (msg.ToolCalls != null && msg.ToolCalls.Count > 0)
                        {
                            var toolCallsParam = new List<ChatToolCall>();
                            foreach (var tc in msg.ToolCalls)
                            {
                                toolCallsParam.Add(ChatToolCall.CreateFunctionToolCall(tc.Id, tc.FunctionName, BinaryData.FromString(tc.Arguments?.ToString(Formatting.None) ?? "{}")));
                            }
                            // Assuming parameterless or message param
                            var asstMsg = new AssistantChatMessage(toolCallsParam);
                            messages.Add(asstMsg);
                        }
                        else
                        {
                            messages.Add(new AssistantChatMessage(msg.Content ?? string.Empty));
                        }
                    }
                    else if (msg.Role == ChatMessageRole.Tool)
                    {
                        messages.Add(new ToolChatMessage(msg.ToolCallId, msg.Content ?? string.Empty));
                    }
                }

                var chatOptions = new ChatCompletionOptions() { Temperature = 0.7f };
                if (tools != null)
                {
                    foreach (JToken tToken in tools)
                    {
                        if (tToken is JObject t)
                        {
                            string name = t["function"]?["name"]?.ToString() ?? "";
                            string desc = t["function"]?["description"]?.ToString() ?? "";
                            string paramStr = t["function"]?["parameters"]?.ToString(Formatting.None) ?? "{}";
                            chatOptions.Tools.Add(ChatTool.CreateFunctionTool(name, desc, BinaryData.FromString(paramStr)));
                        }
                    }
                }

                var result = new StreamChatResult { Content = "", ToolCalls = new List<ToolCallRequest>() };
                
                var toolCallAccumulators = new Dictionary<int, ToolCallAccumulator>();

                var chatUpdates = chatClient.CompleteChatStreaming(messages, chatOptions, cancellationToken);
                foreach (var update in chatUpdates)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Text updates
                    if (update.ContentUpdate != null && update.ContentUpdate.Count > 0)
                    {
                        foreach (var part in update.ContentUpdate)
                        {
                            if (!string.IsNullOrEmpty(part.Text))
                            {
                                result.Content += part.Text;
                                onChunkReceived?.Invoke(part.Text);
                            }
                        }
                    }

                    // Tool call updates
                    if (update.ToolCallUpdates != null)
                    {
                        foreach (var tcUpdate in update.ToolCallUpdates)
                        {
                            if (!toolCallAccumulators.TryGetValue(tcUpdate.Index, out var acc))
                            {
                                acc = new ToolCallAccumulator();
                                toolCallAccumulators[tcUpdate.Index] = acc;
                            }
                            
                            if (!string.IsNullOrEmpty(tcUpdate.ToolCallId))
                                acc.Id = tcUpdate.ToolCallId;
                            
                            if (!string.IsNullOrEmpty(tcUpdate.FunctionName))
                                acc.Name += tcUpdate.FunctionName;
                                
                            if (tcUpdate.FunctionArgumentsUpdate != null)
                                acc.Args.Append(tcUpdate.FunctionArgumentsUpdate.ToString());
                        }
                    }

                    if (update.FinishReason != null)
                    {
                        result.FinishReason = update.FinishReason.Value.ToString();
                    }
                }
                
                if (toolCallAccumulators.Count > 0)
                {
                    BuildToolCallsFromAccumulators(toolCallAccumulators, result.ToolCalls);
                }

                // 记录完整的流式响应结果到调试日志
                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var innerMsg = ex.InnerException != null ? $" -> {ex.InnerException.Message}" : "";
                onError?.Invoke($"发送聊天请求失败: {ex.Message}{innerMsg}");
                return null;
            }
        }

        // 工具调用累积辅助类
        private class ToolCallAccumulator
        {
            public string Id = "";
            public string Name = "";
            public StringBuilder Args = new StringBuilder();
        }

        // 从累积器构建 ToolCallRequest 列表
        private static void BuildToolCallsFromAccumulators(
            Dictionary<int, ToolCallAccumulator> accumulators, List<ToolCallRequest> target)
        {
            var sortedKeys = new List<int>(accumulators.Keys);
            sortedKeys.Sort();
            foreach (int key in sortedKeys)
            {
                var acc = accumulators[key];
                JObject parsedArgs;
                try { parsedArgs = JObject.Parse(acc.Args.ToString()); }
                catch { parsedArgs = new JObject(); }

                target.Add(new ToolCallRequest
                {
                    Id = acc.Id,
                    FunctionName = acc.Name,
                    Arguments = parsedArgs
                });
            }
        }

        // 将前端模式转换为后端API期望的模式
        private string ConvertToBackendMode(string frontendMode)
        {
            switch (frontendMode)
            {
                case "问答":
                    return "chat";
                case "编辑":
                    return "edit";
                case "审核":
                    return "review";
                default:
                    return "chat";
            }
        }

        // 文件上传响应类
        public class FileUploadResponse
        {
            public string file_id { get; set; }
            public string upload_url { get; set; }
            public string content_type { get; set; }
            public long max_bytes { get; set; }
        }

        // 上传文件到后端服务器
        public async Task<FileUploadResponse> UploadFileAsync(string filePath)
        {
            try
            {
                string url = $"{_baseUrl}/files/upload";
                
                using (var form = new MultipartFormDataContent())
                {
                    // 读取文件内容
                    byte[] fileContent = File.ReadAllBytes(filePath);
                    string fileName = Path.GetFileName(filePath);
                    
                    // 添加文件到表单
                    var fileStream = new ByteArrayContent(fileContent);
                    fileStream.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                    form.Add(fileStream, "file", fileName);

                    System.Diagnostics.Debug.WriteLine($"开始上传文件: {fileName} 到 {url}");

                    using (var response = await _httpClient.PostAsync(url, form))
                    {
                        string responseContent = await response.Content.ReadAsStringAsync();
                        System.Diagnostics.Debug.WriteLine($"上传响应状态: {response.StatusCode}");
                        System.Diagnostics.Debug.WriteLine($"上传响应内容: {responseContent}");

                        if (response.IsSuccessStatusCode)
                        {
                            var uploadResponse = JsonConvert.DeserializeObject<FileUploadResponse>(responseContent);
                            return uploadResponse;
                        }
                        else
                        {
                            throw new Exception($"文件上传失败: {response.StatusCode} - {responseContent}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"文件上传异常: {ex.Message}");
                throw new Exception($"文件上传失败: {ex.Message}");
            }
        }

        // 根据模式和知识库生成系统消息
        private string GetSystemMessage(string mode, string knowledgeBase)
        {
            var baseMessage = "你是一个专业的AI助手，专门帮助用户处理Word文档相关的任务。";
            
            switch (mode)
            {
                case "问答":
                    baseMessage += "你的主要任务是回答用户的问题，提供准确和有用的信息。";
                    break;
                case "编辑":
                    baseMessage += "你的主要任务是帮助用户编辑和改进文本内容，包括语法纠错、表达优化、格式调整等。";
                    break;
                case "审核":
                    baseMessage += "你的主要任务是审核文档内容，检查是否符合标准规范，发现潜在问题。";
                    break;
            }

            switch (knowledgeBase)
            {
                case "遥感通用知识库":
                    baseMessage += "你具备遥感技术相关的专业知识。";
                    break;
                case "质量库":
                    baseMessage += "你熟悉质量管理和质量控制的相关标准。";
                    break;
                case "型号库":
                    baseMessage += "你了解各种产品型号和技术规格。";
                    break;
            }

            return baseMessage;
        }
    }
}