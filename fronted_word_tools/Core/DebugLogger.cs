using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace FuXing
{
    /// <summary>
    /// 开发者调试日志 — 当 DeveloperMode 开启时，将对话和工具调用信息写入文件。
    /// 日志路径: %USERPROFILE%\.fuxing\logs\fuxing_YYYY-MM-DD.log
    /// 每天自动创建新的日志文件，保留最近 30 天的日志。
    /// </summary>
    public sealed class DebugLogger
    {
        // ═══════════════════════════════════════════════════════════════
        //  常量
        // ═══════════════════════════════════════════════════════════════

        private const int MaxRetainDays = 30;
        private const string LogFilePrefix = "fuxing_";
        private const string LogFileExtension = ".log";

        // ═══════════════════════════════════════════════════════════════
        //  单例
        // ═══════════════════════════════════════════════════════════════

        public static readonly DebugLogger Instance = new DebugLogger();

        // ═══════════════════════════════════════════════════════════════
        //  状态
        // ═══════════════════════════════════════════════════════════════

        private readonly object _lock = new object();
        private readonly string _logDir;

        /// <summary>当前日志文件路径（按日期变化）</summary>
        private string _currentLogPath;
        private string _currentDate;

        /// <summary>是否启用（由外部设置，通常在加载配置后调用）</summary>
        public bool Enabled { get; set; }

        private DebugLogger()
        {
            _logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".fuxing", "logs");
        }

        /// <summary>获取当前日志文件的完整路径</summary>
        public string CurrentLogPath
        {
            get
            {
                EnsureCurrentLogPath();
                return _currentLogPath;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  公共日志方法
        // ═══════════════════════════════════════════════════════════════

        /// <summary>记录用户消息</summary>
        public void LogUserMessage(string message)
        {
            Write("USER", message);
        }

        /// <summary>记录 AI 完整响应</summary>
        public void LogAssistantMessage(string message)
        {
            Write("ASSISTANT", message);
        }

        /// <summary>记录系统提示词（仅首次或变更时）</summary>
        public void LogSystemPrompt(string prompt)
        {
            Write("SYSTEM_PROMPT", prompt);
        }

        /// <summary>记录工具调用发起（大模型请求）</summary>
        public void LogToolCall(string toolName, string callId, JObject arguments)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Tool: {toolName}  CallId: {callId}");
            sb.AppendLine($"Arguments: {arguments?.ToString(Newtonsoft.Json.Formatting.Indented) ?? "{}"}");
            Write("TOOL_CALL", sb.ToString());
        }

        /// <summary>记录工具执行结果</summary>
        public void LogToolResult(string toolName, string callId, bool success, string output)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Tool: {toolName}  CallId: {callId}  Success: {success}");
            sb.AppendLine($"Output: {output}");
            Write("TOOL_RESULT", sb.ToString());
        }

        /// <summary>记录错误</summary>
        public void LogError(string context, Exception ex)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Context: {context}");
            sb.AppendLine($"Exception: {ex.GetType().FullName}: {ex.Message}");
            sb.AppendLine($"Stack: {ex.StackTrace}");
            var inner = ex.InnerException;
            while (inner != null)
            {
                sb.AppendLine($"  Inner: {inner.GetType().FullName}: {inner.Message}");
                inner = inner.InnerException;
            }
            Write("ERROR", sb.ToString());
        }

        /// <summary>记录错误（纯文本）</summary>
        public void LogError(string context, string errorMessage)
        {
            Write("ERROR", $"Context: {context}\nMessage: {errorMessage}");
        }

        /// <summary>记录通用信息</summary>
        public void LogInfo(string message)
        {
            Write("INFO", message);
        }

        /// <summary>记录会话轮次摘要</summary>
        public void LogRoundStart(int round, int maxRounds)
        {
            Write("ROUND", $"=== Round {round + 1}/{maxRounds} ===");
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
                    EnsureDirectory();
                    EnsureCurrentLogPath();
                    CleanOldLogsIfNeeded();

                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    string entry = $"[{timestamp}] [{tag}]\n{body}\n{"".PadRight(60, '─')}\n";

                    File.AppendAllText(_currentLogPath, entry, Encoding.UTF8);
                }
            }
            catch
            {
                // 日志写入失败不应影响主流程
            }
        }

        private void EnsureDirectory()
        {
            if (!Directory.Exists(_logDir))
                Directory.CreateDirectory(_logDir);
        }

        /// <summary>确保日志文件路径与当前日期一致</summary>
        private void EnsureCurrentLogPath()
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (_currentDate != today || _currentLogPath == null)
            {
                _currentDate = today;
                _currentLogPath = Path.Combine(_logDir, $"{LogFilePrefix}{today}{LogFileExtension}");
            }
        }

        /// <summary>清理超过保留天数的旧日志（每天只检查一次）</summary>
        private string _lastCleanDate;
        private void CleanOldLogsIfNeeded()
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (_lastCleanDate == today) return;
            _lastCleanDate = today;

            try
            {
                var cutoff = DateTime.Now.AddDays(-MaxRetainDays);
                var logFiles = Directory.GetFiles(_logDir, $"{LogFilePrefix}*{LogFileExtension}");

                foreach (var file in logFiles)
                {
                    var fi = new FileInfo(file);
                    if (fi.LastWriteTime < cutoff)
                        fi.Delete();
                }
            }
            catch
            {
                // 清理失败不影响主流程
            }
        }
    }
}
