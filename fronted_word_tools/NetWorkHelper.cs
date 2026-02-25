using Newtonsoft.Json;
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Net.Http;

namespace FuXing
{
    public class NetWorkHelper
    {
        private string _llmServerIP;
        private int _llmServerPort;
        private string _checkStandardServerIP;
        private int _checkStandardServerPort;
        private string _openaiServerIP;
        private int _openaiServerPort;
        private int _timeout = 10000; // 10秒超时
        private static readonly HttpClient _httpClient = new HttpClient();

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

                _llmServerIP = config.llmServerIP;
                _llmServerPort = config.llmServerPort;
                _checkStandardServerIP = config.CheckStandardIP;
                _checkStandardServerPort = config.CheckStandardPort;
                _openaiServerIP = config.OpenAIServerIP;
                _openaiServerPort = config.OpenAIServerPort;
            }
            catch (Exception ex)
            {
                // 如果加载配置失败，使用默认值
                _llmServerIP = "127.0.0.1";
                _llmServerPort = 11434;
                _checkStandardServerIP = "192.168.1.1";
                _checkStandardServerPort = 80;
                _openaiServerIP = "127.0.0.1";
                _openaiServerPort = 8000;
                System.Diagnostics.Debug.WriteLine($"Failed to load configuration: {ex.Message}");
            }
        }

        // 发送AI文本纠错请求
        public string SendTextCorrectionRequest(string text)
        {
            try
            {
                string url = $"http://{_llmServerIP}:{_llmServerPort}/api/correct";
                
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.ContentType = "application/json; charset=UTF-8";
                request.Timeout = _timeout;

                var requestData = new { text = text };
                string jsonData = JsonConvert.SerializeObject(requestData);
                byte[] data = Encoding.UTF8.GetBytes(jsonData);
                request.ContentLength = data.Length;

                using (Stream stream = request.GetRequestStream())
                {
                    stream.Write(data, 0, data.Length);
                }

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                {
                    string responseText = reader.ReadToEnd();
                    dynamic responseData = JsonConvert.DeserializeObject(responseText);
                    return responseData?.corrected_text ?? text;
                }
            }
            catch (Exception ex)
            {
                return $"文本纠错失败: {ex.Message}";
            }
        }

        // 获取标准系统的SID
        private string GetStandardSystemSid()
        {
            try
            {
                string loginUrl = $"http://{_checkStandardServerIP}:{_checkStandardServerPort}/portal/r/w?cmd=com.awspaas.user.login";
                
                HttpWebRequest loginRequest = (HttpWebRequest)WebRequest.Create(loginUrl);
                loginRequest.Method = "POST";
                loginRequest.ContentType = "application/x-www-form-urlencoded; charset=UTF-8";
                loginRequest.Timeout = _timeout;

                string postData = "type=login&jsonData={\"userid\":\"guest\",\"userpwd\":\"guest\"}";
                byte[] data = Encoding.UTF8.GetBytes(postData);
                loginRequest.ContentLength = data.Length;

                using (Stream stream = loginRequest.GetRequestStream())
                {
                    stream.Write(data, 0, data.Length);
                }

                using (HttpWebResponse response = (HttpWebResponse)loginRequest.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                {
                    string responseBody = reader.ReadToEnd();
                    dynamic responseData = JsonConvert.DeserializeObject(responseBody);
                    return responseData?.data?.sid ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Login failed: {ex.Message}");
                return string.Empty;
            }
        }

        // 使用SID执行标准校验查询
        public string SendStandardCheckRequest(string searchInfo)
        {
            try
            {
                string sid = GetStandardSystemSid();
                if (string.IsNullOrEmpty(sid))
                {
                    return "无法获取登录凭证，标准校验失败";
                }

                string checkUrl = $"http://{_checkStandardServerIP}:{_checkStandardServerPort}/portal/r/w?sid={sid}&cmd=com.awspaas.user.apps.standard.pubController";
                
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(checkUrl);
                request.Method = "POST";
                request.ContentType = "application/x-www-form-urlencoded; charset=UTF-8";
                request.Headers.Add("Cookie", "AWSLOGINUID=null; AWSLOGINPWD=null; AWSLOGINRSAPWD=null");
                request.Timeout = _timeout;

                string searchInfo_cleaned = Regex.Replace(searchInfo, @"[^\w\s\u4e00-\u9fa5\n-]", "");
                string postData = $"type=serachInfo&jsonData={{\"tableName\":\"BO_EU_STANDARD_QUERY\",\"serachInfo\":\"{searchInfo_cleaned}\"}}";
                byte[] data = Encoding.UTF8.GetBytes(postData);
                request.ContentLength = data.Length;

                using (Stream stream = request.GetRequestStream())
                {
                    stream.Write(data, 0, data.Length);
                }

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                {
                    string responseText = reader.ReadToEnd();
                    dynamic responseData = JsonConvert.DeserializeObject(responseText);
                    string htmlContent = responseData?.data;

                    if (!string.IsNullOrEmpty(htmlContent))
                    {
                        // 处理HTML内容，转换为纯文本
                        htmlContent = Regex.Replace(htmlContent, @"<\\?\/?br\s*\/?>(?=\s*[^\s])", Environment.NewLine + Environment.NewLine);
                        string plainText = Regex.Replace(htmlContent, "<[^>]+?>", "");
                        return Environment.NewLine + plainText;
                    }
                    else
                    {
                        return "未找到相关标准信息";
                    }
                }
            }
            catch (Exception ex)
            {
                return $"标准校验失败: {ex.Message}";
            }
        }

        // OpenAI兼容的流式聊天API模型类
        public class ChatMessage
        {
            public string role { get; set; }
            public string content { get; set; }
        }

        public class ChatCompletionRequest
        {
            public string model { get; set; } = "gpt-3.5-turbo";
            public ChatMessage[] messages { get; set; }
            public bool stream { get; set; } = true;
            public double temperature { get; set; } = 0.7;
            public int max_tokens { get; set; } = 2048;
            // Office插件特定的元数据
            public string mode { get; set; }
            public object doc { get; set; }
            public object selection_range { get; set; }
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
        }

        // 发送流式聊天请求到8000端口的OpenAI兼容API - 使用真正的流式处理
        public async Task SendStreamChatRequestAsync(string userMessage, string mode, string knowledgeBase, Action<string> onChunkReceived, Action onCompleted, Action<string> onError)
        {
            try
            {
                string url = $"http://{_openaiServerIP}:{_openaiServerPort}/v1/chat/completions";
                
                // 构建消息数组，可以根据mode和knowledgeBase调整系统消息
                var systemMessage = GetSystemMessage(mode, knowledgeBase);
                var messages = new[]
                {
                    new ChatMessage { role = "system", content = systemMessage },
                    new ChatMessage { role = "user", content = userMessage }
                };

                var requestData = new ChatCompletionRequest
                {
                    model = "gpt-3.5-turbo",
                    messages = messages,
                    stream = true,
                    temperature = 0.7,
                    max_tokens = 2048,
                    // 添加Office插件特定的元数据
                    mode = ConvertToBackendMode(mode),
                    // TODO: 未来可以添加文档引用和选择范围
                    // doc = new { doc_id = "current_document", doc_version_id = "1" },
                    // selection_range = new { startParagraphId = "p1", startOffset = 0, endParagraphId = "p1", endOffset = 100 }
                };

                string jsonData = JsonConvert.SerializeObject(requestData);
                var content = new StringContent(jsonData, Encoding.UTF8, "application/json");

                // 使用SendAsync而不是PostAsync来获得更好的流式控制
                var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = content
                };

                using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        using (var stream = await response.Content.ReadAsStreamAsync())
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            string line;
                            System.Diagnostics.Debug.WriteLine("开始读取流式响应...");
                            
                            while ((line = await reader.ReadLineAsync()) != null)
                            {
                                System.Diagnostics.Debug.WriteLine($"收到原始数据行: {line}");
                                
                                if (line.StartsWith("data: "))
                                {
                                    string data = line.Substring(6);
                                    if (data == "[DONE]")
                                    {
                                        System.Diagnostics.Debug.WriteLine("收到流式结束标记");
                                        onCompleted?.Invoke();
                                        break;
                                    }

                                    try
                                    {
                                        var chunk = JsonConvert.DeserializeObject<ChatCompletionChunk>(data);
                                        if (chunk?.choices?.Length > 0 && chunk.choices[0].delta?.content != null)
                                        {
                                            string content_chunk = chunk.choices[0].delta.content;
                                            System.Diagnostics.Debug.WriteLine($"解析到内容块: '{content_chunk}'");
                                            onChunkReceived?.Invoke(content_chunk);
                                        }
                                        else
                                        {
                                            System.Diagnostics.Debug.WriteLine($"忽略空内容块: {data}");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"解析流式数据失败: {ex.Message}, 数据: {data}");
                                    }
                                }
                                else if (!string.IsNullOrWhiteSpace(line))
                                {
                                    System.Diagnostics.Debug.WriteLine($"忽略非data行: {line}");
                                }
                            }
                            
                            System.Diagnostics.Debug.WriteLine("流式响应读取完成");
                        }
                    }
                    else
                    {
                        onError?.Invoke($"请求失败: {response.StatusCode} - {response.ReasonPhrase}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"流式请求异常: {ex.Message}");
                onError?.Invoke($"发送聊天请求失败: {ex.Message}");
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
                string url = $"http://{_openaiServerIP}:{_openaiServerPort}/v1/files/upload";
                
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