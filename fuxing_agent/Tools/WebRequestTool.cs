using System;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Text;

namespace FuXingAgent.Tools
{
    public class WebRequestTool
    {
        private readonly Connect _connect;
        public WebRequestTool(Connect connect) => _connect = connect;

        [Description("Send an HTTP request and return the response body. Supports GET/POST with optional JSON body. Use for calling local services (e.g. AnyTxt JSON-RPC API at 127.0.0.1:9920).")]
        public string web_request(
            [Description("完整请求 URL，如 http://127.0.0.1:9920")] string url,
            [Description("HTTP 方法: GET/POST，默认 POST")] string method = "POST",
            [Description("请求体（JSON 字符串），GET 时可为空")] string body = null,
            [Description("Content-Type，默认 application/json")] string content_type = "application/json",
            [Description("超时秒数，默认 30")] int timeout_seconds = 30)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("url 不能为空");

            var uri = new Uri(url);
            if (!IsAllowedHost(uri))
                throw new InvalidOperationException($"安全限制：仅允许访问本地服务（127.0.0.1 / localhost），当前目标: {uri.Host}");

            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.Method = method.ToUpperInvariant();
            request.Accept = "application/json";
            request.Timeout = timeout_seconds * 1000;
            request.ReadWriteTimeout = timeout_seconds * 1000;

            if (!string.IsNullOrEmpty(body) && request.Method != "GET")
            {
                request.ContentType = content_type;
                byte[] data = Encoding.UTF8.GetBytes(body);
                request.ContentLength = data.Length;
                using (var stream = request.GetRequestStream())
                    stream.Write(data, 0, data.Length);
            }

            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                    return ReadResponse(response);
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse errorResponse)
            {
                string errorBody = ReadResponse(errorResponse);
                throw new InvalidOperationException(
                    $"HTTP {(int)errorResponse.StatusCode} {errorResponse.StatusDescription}\n{errorBody}");
            }
        }

        private static bool IsAllowedHost(Uri uri)
        {
            string host = uri.Host;
            return host == "127.0.0.1" || host == "localhost" || host == "::1";
        }

        private static string ReadResponse(HttpWebResponse response)
        {
            using (var stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                string text = reader.ReadToEnd();
                const int maxLength = 50000;
                if (text.Length > maxLength)
                    return text.Substring(0, maxLength) + $"\n... (响应已截断，总长 {text.Length} 字符)";
                return text;
            }
        }
    }
}
