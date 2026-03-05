using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FuXing
{
    /// <summary>
    /// 会话管理器 — 负责会话的创建、加载、保存、删除和列表查询。
    /// 存储目录: %USERPROFILE%\.fuxing\sessions\
    /// 每个会话一个 JSON 文件: {id}.json
    /// </summary>
    public sealed class SessionManager
    {
        public static readonly SessionManager Instance = new SessionManager();

        private readonly string _sessionsDir;

        private SessionManager()
        {
            _sessionsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".fuxing", "sessions");
        }

        // ═══════════════════════════════════════════════════════════════
        //  公共 API
        // ═══════════════════════════════════════════════════════════════

        /// <summary>创建一个新的空会话并持久化</summary>
        public ChatSession CreateSession()
        {
            EnsureDirectory();
            var session = new ChatSession
            {
                Id = Guid.NewGuid().ToString("N"),
                Title = "新对话",
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            WriteSession(session);
            return session;
        }

        /// <summary>保存会话（从 ChatMemory 导出消息后写入文件）</summary>
        public void SaveSession(ChatSession session, ChatMemory memory)
        {
            if (session == null) return;
            session.Messages = memory.ExportMessages();
            session.UpdatedAt = DateTime.Now;
            WriteSession(session);
        }

        /// <summary>仅保存会话元数据（标题等），不更新消息列表</summary>
        public void UpdateTitle(string sessionId, string title)
        {
            var session = LoadSession(sessionId);
            if (session == null) return;
            session.Title = title;
            session.UpdatedAt = DateTime.Now;
            WriteSession(session);
        }

        /// <summary>加载指定会话的完整数据</summary>
        public ChatSession LoadSession(string sessionId)
        {
            string path = GetSessionPath(sessionId);
            if (!File.Exists(path)) return null;
            try
            {
                string json = File.ReadAllText(path, System.Text.Encoding.UTF8);
                return JsonConvert.DeserializeObject<ChatSession>(json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>删除指定会话</summary>
        public void DeleteSession(string sessionId)
        {
            string path = GetSessionPath(sessionId);
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { }
        }

        /// <summary>
        /// 列出所有会话（按最近更新时间降序）。
        /// 返回完整的 ChatSession 对象列表。
        /// </summary>
        public List<ChatSession> ListSessions()
        {
            EnsureDirectory();
            var result = new List<ChatSession>();
            try
            {
                foreach (var file in Directory.GetFiles(_sessionsDir, "*.json"))
                {
                    try
                    {
                        string json = File.ReadAllText(file, System.Text.Encoding.UTF8);
                        var session = JsonConvert.DeserializeObject<ChatSession>(json);
                        if (session != null)
                            result.Add(session);
                    }
                    catch { /* 跳过损坏的文件 */ }
                }
            }
            catch { }

            return result.OrderByDescending(s => s.UpdatedAt).ToList();
        }

        // ═══════════════════════════════════════════════════════════════
        //  内部实现
        // ═══════════════════════════════════════════════════════════════

        private void EnsureDirectory()
        {
            if (!Directory.Exists(_sessionsDir))
                Directory.CreateDirectory(_sessionsDir);
        }

        private string GetSessionPath(string sessionId)
        {
            return Path.Combine(_sessionsDir, sessionId + ".json");
        }

        private void WriteSession(ChatSession session)
        {
            EnsureDirectory();
            string json = JsonConvert.SerializeObject(session, Formatting.Indented);
            File.WriteAllText(GetSessionPath(session.Id), json, System.Text.Encoding.UTF8);
        }
    }
}
