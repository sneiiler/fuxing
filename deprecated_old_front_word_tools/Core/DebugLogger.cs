using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace FuXing
{
    /// <summary>
    /// 开发者调试日志 — 当 DeveloperMode 开启时，将对话和工具调用信息写入文件。
    /// 日志路径: %USERPROFILE%\.fuxing\logs\fuxing_YYYY-MM-DD_HHmmss.log
    /// 每个会话创建独立的日志文件，保留最近 30 个会话日志。
    /// </summary>
    public sealed class DebugLogger
    {
        // ═══════════════════════════════════════════════════════════════
        //  常量
        // ═══════════════════════════════════════════════════════════════

        private const int MaxRetainFiles = 30;
        private const string LogFilePrefix = "fuxing_";
        private const string LogFileExtension = ".log";

        // 分隔线样式
        private const string SeparatorHeavy = "════════════════════════════════════════════════════════════";
        private const string SeparatorLight = "────────────────────────────────────────────────────────────";
        private const string SeparatorDot   = "· · · · · · · · · · · · · · · · · · · · · · · · · · · · · ·";

        // ═══════════════════════════════════════════════════════════════
        //  单例
        // ═══════════════════════════════════════════════════════════════

        public static readonly DebugLogger Instance = new DebugLogger();

        // ═══════════════════════════════════════════════════════════════
        //  状态
        // ═══════════════════════════════════════════════════════════════

        private readonly object _lock = new object();
        private readonly string _logDir;

        /// <summary>当前日志文件路径（按会话变化）</summary>
        private string _currentLogPath;

        /// <summary>是否启用（由外部设置，通常在加载配置后调用）</summary>
        public bool Enabled { get; set; }

        private DebugLogger()
        {
            _logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".fuxing", "logs");
        }

        /// <summary>获取当前日志文件的完整路径</summary>
        public string CurrentLogPath => _currentLogPath;

        // ═══════════════════════════════════════════════════════════════
        //  公共日志方法：会话生命周期
        // ═══════════════════════════════════════════════════════════════

        /// <summary>开始新会话 — 创建独立的日志文件</summary>
        public void LogSessionStart()
        {
            if (!Enabled) return;

            lock (_lock)
            {
                EnsureDirectory();

                // 每次新会话生成唯一的日志文件
                string now = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
                _currentLogPath = Path.Combine(_logDir, $"{LogFilePrefix}{now}{LogFileExtension}");

                CleanOldLogs();
            }

            var sb = new StringBuilder();
            sb.AppendLine(SeparatorHeavy);
            sb.AppendLine($"  新会话  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine(SeparatorHeavy);
            WriteRaw(sb.ToString());
        }

        /// <summary>记录会话轮次开始</summary>
        public void LogRoundStart(int round, int maxRounds)
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine($"┌─── Round {round + 1}/{maxRounds} ───┐");
            WriteRaw(sb.ToString());
        }

        // ═══════════════════════════════════════════════════════════════
        //  公共日志方法：对话消息（完整保留）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>记录系统提示词</summary>
        public void LogSystemPrompt(string prompt)
        {
            Write("SYSTEM", prompt);
        }

        /// <summary>记录用户消息（含自动注入的上下文）</summary>
        public void LogUserMessage(string message)
        {
            Write("USER", message);
        }

        /// <summary>
        /// 记录 AI 纯文本回复（最终回复，无工具调用）。
        /// </summary>
        public void LogAssistantMessage(string message)
        {
            Write("ASSISTANT", message);
        }

        /// <summary>
        /// 记录 AI 带工具调用的回复。
        /// 完整保留 Content（含 think 等）和调用列表，便于审阅完整对话。
        /// </summary>
        public void LogAssistantToolCallMessage(string content, System.Collections.Generic.List<ToolCallRequest> toolCalls)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(content))
            {
                sb.AppendLine("[Content]");
                sb.AppendLine(content);
                sb.AppendLine();
            }
            sb.AppendLine($"[Tool Calls] ({toolCalls?.Count ?? 0})");
            if (toolCalls != null)
            {
                foreach (var tc in toolCalls)
                    sb.AppendLine($"  • {tc.FunctionName}  (id={tc.Id})");
            }
            Write("ASSISTANT→TOOLS", sb.ToString());
        }

        // ═══════════════════════════════════════════════════════════════
        //  公共日志方法：工具执行
        // ═══════════════════════════════════════════════════════════════

        /// <summary>记录工具调用发起（含完整参数）</summary>
        public void LogToolCall(string toolName, string callId, JObject arguments)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"  Tool: {toolName}");
            sb.AppendLine($"  CallId: {callId}");
            sb.AppendLine($"  Arguments: {arguments?.ToString(Newtonsoft.Json.Formatting.None) ?? "{}"}");
            Write("TOOL_CALL", sb.ToString());
        }

        /// <summary>记录工具执行结果（含完整输出）</summary>
        public void LogToolResult(string toolName, string callId, bool success, string output)
        {
            string icon = success ? "✓" : "✗";
            var sb = new StringBuilder();
            sb.AppendLine($"  {icon} Tool: {toolName}  CallId: {callId}");
            sb.AppendLine($"  Success: {success}");
            sb.AppendLine($"  Output:");
            sb.AppendLine(Indent(output ?? "(null)", 4));
            Write("TOOL_RESULT", sb.ToString());
        }

        /// <summary>记录工具被跳过（超出单轮上限等）</summary>
        public void LogToolSkipped(string toolName, string callId, string reason)
        {
            Write("TOOL_SKIP", $"  Tool: {toolName}  CallId: {callId}\n  Reason: {reason}");
        }

        /// <summary>记录操作审批结果</summary>
        public void LogApproval(string toolName, string callId, bool approved, string summary)
        {
            string icon = approved ? "✓ 已批准" : "✗ 已拒绝";
            var sb = new StringBuilder();
            sb.AppendLine($"  {icon}  Tool: {toolName}  CallId: {callId}");
            if (!string.IsNullOrEmpty(summary))
                sb.AppendLine($"  Summary: {summary}");
            Write("APPROVAL", sb.ToString());
        }

        /// <summary>记录响应截断并自动续传</summary>
        public void LogTruncation(int round)
        {
            Write("TRUNCATED", $"  Round {round + 1} 响应被 max_tokens 截断，自动续传");
        }

        // ═══════════════════════════════════════════════════════════════
        //  公共日志方法：错误
        // ═══════════════════════════════════════════════════════════════

        /// <summary>记录异常（完整堆栈 + 所有 InnerException）</summary>
        public void LogError(string context, Exception ex)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"  Context: {context}");
            sb.AppendLine($"  Exception: {ex.GetType().FullName}");
            sb.AppendLine($"  Message: {ex.Message}");
            if (!string.IsNullOrEmpty(ex.StackTrace))
            {
                sb.AppendLine($"  StackTrace:");
                sb.AppendLine(Indent(ex.StackTrace, 4));
            }
            var inner = ex.InnerException;
            int depth = 0;
            while (inner != null)
            {
                depth++;
                sb.AppendLine($"  InnerException[{depth}]: {inner.GetType().FullName}: {inner.Message}");
                if (!string.IsNullOrEmpty(inner.StackTrace))
                    sb.AppendLine(Indent(inner.StackTrace, 6));
                inner = inner.InnerException;
            }
            Write("ERROR", sb.ToString());
        }

        /// <summary>记录错误（纯文本）</summary>
        public void LogError(string context, string errorMessage)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"  Context: {context}");
            sb.AppendLine($"  Message: {errorMessage}");
            Write("ERROR", sb.ToString());
        }

        /// <summary>记录通用信息</summary>
        public void LogInfo(string message)
        {
            Write("INFO", message);
        }

        /// <summary>记录内部组件调试信息（GraphBuilder / GraphCache 等）</summary>
        public void LogDebug(string component, string message)
        {
            Write("DEBUG", $"  [{component}] {message}");
        }

        // ═══════════════════════════════════════════════════════════════
        //  公共日志方法：LLM 请求/响应完整记录
        // ═══════════════════════════════════════════════════════════════

        /// <summary>记录发送给 LLM 的完整 HTTP 请求体（JSON）</summary>
        public void LogLlmRequest(string url, string requestJson)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"  URL: {url}");
            sb.AppendLine($"  Body ({requestJson?.Length ?? 0} chars):");
            sb.AppendLine(requestJson ?? "(null)");
            Write("LLM_REQUEST", sb.ToString());
        }

        /// <summary>记录 LLM 流式响应的完整累积结果</summary>
        public void LogLlmStreamResponse(string content, int toolCallCount, string finishReason)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"  FinishReason: {finishReason ?? "(null)"}");
            sb.AppendLine($"  ToolCalls: {toolCallCount}");
            sb.AppendLine($"  Content ({content?.Length ?? 0} chars):");
            sb.AppendLine(content ?? "(empty)");
            Write("LLM_STREAM_RESPONSE", sb.ToString());
        }

        /// <summary>记录 LLM 非流式响应的完整原始 JSON</summary>
        public void LogLlmResponse(string rawJson)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"  RawBody ({rawJson?.Length ?? 0} chars):");
            sb.AppendLine(rawJson ?? "(null)");
            Write("LLM_RESPONSE", sb.ToString());
        }

        /// <summary>记录 LLM 请求的 HTTP 错误</summary>
        public void LogLlmHttpError(string url, int statusCode, string errorBody)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"  URL: {url}");
            sb.AppendLine($"  StatusCode: {statusCode}");
            sb.AppendLine($"  ErrorBody:");
            sb.AppendLine(Indent(errorBody ?? "(null)", 4));
            Write("LLM_HTTP_ERROR", sb.ToString());
        }

        /// <summary>记录子智能体的完整交互过程</summary>
        public void LogSubAgentRound(string agentName, int round, int maxRounds, string content, int toolCallCount, string finishReason)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"  Agent: {agentName}");
            sb.AppendLine($"  Round: {round + 1}/{maxRounds}");
            sb.AppendLine($"  FinishReason: {finishReason ?? "(null)"}");
            sb.AppendLine($"  ToolCalls: {toolCallCount}");
            sb.AppendLine($"  Content ({content?.Length ?? 0} chars):");
            sb.AppendLine(content ?? "(empty)");
            Write("SUBAGENT_ROUND", sb.ToString());
        }

        // ═══════════════════════════════════════════════════════════════
        //  内部实现
        // ═══════════════════════════════════════════════════════════════

        private void Write(string tag, string body)
        {
            if (!Enabled) return;

            try
            {
                lock (_lock)
                {
                    if (_currentLogPath == null) return;

                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    var sb = new StringBuilder();
                    sb.AppendLine(SeparatorLight);
                    sb.AppendLine($"[{timestamp}] [{tag}]");
                    sb.AppendLine(body.TrimEnd());
                    sb.AppendLine();

                    File.AppendAllText(_currentLogPath, sb.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // 日志写入失败不应影响主流程
            }
        }

        /// <summary>直接写入原始文本（不加 tag/timestamp），用于分隔符等</summary>
        private void WriteRaw(string text)
        {
            if (!Enabled) return;

            try
            {
                lock (_lock)
                {
                    if (_currentLogPath == null) return;

                    File.AppendAllText(_currentLogPath, text, Encoding.UTF8);
                }
            }
            catch { }
        }

        /// <summary>给多行文本添加缩进</summary>
        private static string Indent(string text, int spaces)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string pad = new string(' ', spaces);
            return pad + text.Replace("\n", "\n" + pad);
        }

        private void EnsureDirectory()
        {
            if (!Directory.Exists(_logDir))
                Directory.CreateDirectory(_logDir);
        }

        /// <summary>清理超出保留数量的旧日志文件，按创建时间从旧到新排序，保留最近 MaxRetainFiles 个</summary>
        private void CleanOldLogs()
        {
            try
            {
                var logFiles = Directory.GetFiles(_logDir, $"{LogFilePrefix}*{LogFileExtension}")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTime)
                    .ToArray();

                for (int i = MaxRetainFiles; i < logFiles.Length; i++)
                    logFiles[i].Delete();
            }
            catch
            {
                // 清理失败不影响主流程
            }
        }
    }
}
