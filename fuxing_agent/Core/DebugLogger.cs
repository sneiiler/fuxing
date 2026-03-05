using Microsoft.Extensions.AI;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace FuXingAgent.Core
{
    /// <summary>
    /// 调试日志：将对话与工具调用信息写入文件。
    /// 日志路径: %USERPROFILE%\.fuxing\logs\yyyyMMdd_HHmmss_对话标题.log
    /// </summary>
    public sealed class DebugLogger
    {
        private const int MaxRetainFiles = 30;
        private const string LogFileExtension = ".log";
        private const string SeparatorHeavy = "════════════════════════════════════════════════════════════";
        private const string SeparatorLight = "────────────────────────────────────────────────────────────";

        public static readonly DebugLogger Instance = new DebugLogger();

        private readonly object _lock = new object();
        private readonly string _logDir;
        private string _currentLogPath;
        private DateTime _currentSessionStart;
        private string _currentSessionTitle;

        public bool Enabled { get; set; }
        public string CurrentLogPath => _currentLogPath;

        private DebugLogger()
        {
            _logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".fuxing", "logs");
        }

        public void LogSessionStart(string sessionTitle)
        {
            if (!Enabled) return;
            lock (_lock)
            {
                EnsureDirectory();
                _currentSessionStart = DateTime.Now;
                _currentSessionTitle = NormalizeTitle(sessionTitle);
                _currentLogPath = Path.Combine(_logDir, BuildLogFileName(_currentSessionStart, _currentSessionTitle));
                CleanOldLogs();
            }
            var sb = new StringBuilder();
            sb.AppendLine(SeparatorHeavy);
            sb.AppendLine($"  新会话  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"  标题    {_currentSessionTitle}");
            sb.AppendLine(SeparatorHeavy);
            WriteRaw(sb.ToString());
        }

        public void UpdateSessionTitle(string sessionTitle)
        {
            if (!Enabled) return;
            lock (_lock)
            {
                if (string.IsNullOrEmpty(_currentLogPath) || _currentSessionStart == default(DateTime))
                    return;

                string normalized = NormalizeTitle(sessionTitle);
                if (string.Equals(normalized, _currentSessionTitle, StringComparison.Ordinal))
                    return;

                string newPath = Path.Combine(_logDir, BuildLogFileName(_currentSessionStart, normalized));
                try
                {
                    if (!string.Equals(_currentLogPath, newPath, StringComparison.OrdinalIgnoreCase) && File.Exists(_currentLogPath))
                    {
                        File.Move(_currentLogPath, newPath);
                        _currentLogPath = newPath;
                    }
                    _currentSessionTitle = normalized;
                }
                catch
                {
                    _currentSessionTitle = normalized;
                }
            }
        }

        public void LogRoundStart(int round, int maxRounds)
        {
            WriteRaw($"\n┌─── Round {round + 1}/{maxRounds} ───┐\n");
        }

        public void LogSystemPrompt(string prompt) => Write("SYSTEM", prompt);
        public void LogUserMessage(string message) => Write("USER", message);
        public void LogAssistantMessage(string message) => Write("ASSISTANT", message);

        public void LogAssistantToolCallMessage(string content, System.Collections.Generic.List<FunctionCallContent> toolCalls)
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
                foreach (var tc in toolCalls)
                    sb.AppendLine($"  • {tc.Name}  (id={tc.CallId})");
            Write("ASSISTANT→TOOLS", sb.ToString());
        }

        public void LogToolCall(string toolName, string callId, string arguments)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"  Tool: {toolName}");
            sb.AppendLine($"  CallId: {callId}");
            sb.AppendLine($"  Arguments: {arguments ?? "{}"}");
            Write("TOOL_CALL", sb.ToString());
        }

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

        public void LogToolSkipped(string toolName, string callId, string reason)
        {
            Write("TOOL_SKIP", $"  Tool: {toolName}  CallId: {callId}\n  Reason: {reason}");
        }

        public void LogApproval(string toolName, string callId, bool approved, string summary)
        {
            string icon = approved ? "✓ 已批准" : "✗ 已拒绝";
            var sb = new StringBuilder();
            sb.AppendLine($"  {icon}  Tool: {toolName}  CallId: {callId}");
            if (!string.IsNullOrEmpty(summary))
                sb.AppendLine($"  Summary: {summary}");
            Write("APPROVAL", sb.ToString());
        }

        public void LogTruncation(int round)
        {
            Write("TRUNCATED", $"  Round {round + 1} 响应被 max_tokens 截断，自动续传");
        }

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
                inner = inner.InnerException;
            }
            Write("ERROR", sb.ToString());
        }

        public void LogError(string context, string errorMessage)
        {
            Write("ERROR", $"  Context: {context}\n  Message: {errorMessage}");
        }

        public void LogInfo(string message) => Write("INFO", message);
        public void LogDebug(string component, string message) => Write("DEBUG", $"  [{component}] {message}");

        // ═══════════════════════════════════════════════════════════════
        //  公共日志方法：LLM 请求/响应完整记录
        // ═══════════════════════════════════════════════════════════════

        /// <summary>记录发送给 LLM 的完整请求（消息列表和选项）</summary>
        public void LogLlmRequest(string baseUrl, string model, int messageCount, string messagesJson, string optionsJson)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"  BaseURL: {baseUrl}");
            sb.AppendLine($"  Model: {model}");
            sb.AppendLine($"  MessageCount: {messageCount}");
            sb.AppendLine($"  Messages ({messagesJson?.Length ?? 0} chars):");
            sb.AppendLine(messagesJson ?? "(null)");
            sb.AppendLine($"  Options:");
            sb.AppendLine(optionsJson ?? "(null)");
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

        /// <summary>记录 LLM 请求的异常错误</summary>
        public void LogLlmError(string context, Exception ex)
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
            Write("LLM_ERROR", sb.ToString());
        }

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
            catch { }
        }

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

        private void CleanOldLogs()
        {
            try
            {
                var logFiles = Directory.GetFiles(_logDir, $"*{LogFileExtension}")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTime)
                    .ToArray();
                for (int i = MaxRetainFiles; i < logFiles.Length; i++)
                    logFiles[i].Delete();
            }
            catch { }
        }

        private static string BuildLogFileName(DateTime startTime, string title)
        {
            return $"{startTime:yyyyMMdd_HHmmss}_{title}{LogFileExtension}";
        }

        private static string NormalizeTitle(string title)
        {
            string t = string.IsNullOrWhiteSpace(title) ? "新对话" : title.Trim();
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(t.Length);
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                if (invalid.Contains(c)) continue;
                sb.Append(c);
            }

            string cleaned = sb.ToString().Trim();
            if (cleaned.Length == 0) cleaned = "新对话";
            if (cleaned.Length > 48) cleaned = cleaned.Substring(0, 48);
            return cleaned;
        }
    }
}
