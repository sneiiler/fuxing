using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace FuXingAgent.Core
{
    /// <summary>
    /// 会话管理器 — 会话的 CRUD 操作。
    /// 存储目录: %USERPROFILE%\.fuxing\sessions\
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

        public void SaveSession(ChatSession session, string agentSessionStateJson)
        {
            if (session == null) return;
            session.AgentSessionStateJson = agentSessionStateJson;
            session.UpdatedAt = DateTime.Now;
            WriteSession(session);
        }

        public void UpdateTitle(string sessionId, string title)
        {
            var session = LoadSession(sessionId);
            if (session == null) return;
            session.Title = title;
            session.UpdatedAt = DateTime.Now;
            WriteSession(session);
        }

        public ChatSession LoadSession(string sessionId)
        {
            string path = GetSessionPath(sessionId);
            if (!File.Exists(path)) return null;
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                return JsonConvert.DeserializeObject<ChatSession>(json);
            }
            catch { return null; }
        }

        public void DeleteSession(string sessionId)
        {
            string path = GetSessionPath(sessionId);
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }

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
                        string json = File.ReadAllText(file, Encoding.UTF8);
                        var session = JsonConvert.DeserializeObject<ChatSession>(json);
                        if (session != null)
                        {
                            session.MessageCount = CountMessagesFromState(session.AgentSessionStateJson);
                            result.Add(session);
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return result.OrderByDescending(s => s.UpdatedAt).ToList();
        }

        private void EnsureDirectory()
        {
            if (!Directory.Exists(_sessionsDir))
                Directory.CreateDirectory(_sessionsDir);
        }

        private string GetSessionPath(string sessionId)
            => Path.Combine(_sessionsDir, sessionId + ".json");

        private void WriteSession(ChatSession session)
        {
            EnsureDirectory();
            string json = JsonConvert.SerializeObject(session, Formatting.Indented);
            File.WriteAllText(GetSessionPath(session.Id), json, Encoding.UTF8);
        }

        private static int CountMessagesFromState(string agentSessionStateJson)
        {
            if (string.IsNullOrWhiteSpace(agentSessionStateJson)) return 0;
            try
            {
                var root = JObject.Parse(agentSessionStateJson);
                var messages = root["messages"] as JArray;
                return messages?.Count ?? 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}
