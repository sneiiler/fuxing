using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Text;

namespace FuXing
{
    /// <summary>
    /// 开发者调试日志 — 当 DeveloperMode 开启时，将对话和工具调用信息写入文件。
    /// 日志路径: %USERPROFILE%\.fuxing\logs\fuxing_debug.log
    /// 单文件上限 5 MB，超限时滚动（最多保留 3 个历史文件）。
    /// </summary>
    public sealed class DebugLogger
    {
        // ═══════════════════════════════════════════════════════════════
        //  常量
        // ═══════════════════════════════════════════════════════════════

        private const long MaxFileSize = 5 * 1024 * 1024; // 5 MB
        private const int MaxHistoryFiles = 3;
        private const string LogFileName = "fuxing_debug.log";

        // ═══════════════════════════════════════════════════════════════
        //  单例
        // ═══════════════════════════════════════════════════════════════

        public static readonly DebugLogger Instance = new DebugLogger();

        // ═══════════════════════════════════════════════════════════════
        //  状态
        // ═══════════════════════════════════════════════════════════════

        private readonly object _lock = new object();
        private readonly string _logDir;
        private readonly string _logPath;

        /// <summary>是否启用（由外部设置，通常在加载配置后调用）</summary>
        public bool Enabled { get; set; }

        private DebugLogger()
        {
            _logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".fuxing", "logs");
            _logPath = Path.Combine(_logDir, LogFileName);
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
                    RotateIfNeeded();

                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    string entry = $"[{timestamp}] [{tag}]\n{body}\n{"".PadRight(60, '─')}\n";

                    File.AppendAllText(_logPath, entry, Encoding.UTF8);
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

        private void RotateIfNeeded()
        {
            if (!File.Exists(_logPath)) return;

            var fi = new FileInfo(_logPath);
            if (fi.Length < MaxFileSize) return;

            // 删除最老的历史文件
            string oldest = Path.Combine(_logDir, $"fuxing_debug_{MaxHistoryFiles}.log");
            if (File.Exists(oldest))
                File.Delete(oldest);

            // 依次重命名 N-1 → N
            for (int i = MaxHistoryFiles - 1; i >= 1; i--)
            {
                string src = Path.Combine(_logDir, $"fuxing_debug_{i}.log");
                string dst = Path.Combine(_logDir, $"fuxing_debug_{i + 1}.log");
                if (File.Exists(src))
                    File.Move(src, dst);
            }

            // 当前文件 → _1
            string first = Path.Combine(_logDir, "fuxing_debug_1.log");
            File.Move(_logPath, first);
        }
    }
}
