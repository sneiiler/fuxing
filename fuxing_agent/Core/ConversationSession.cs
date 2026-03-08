using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FuXingAgent.Core
{
    /// <summary>
    /// 一个会话的完整快照，持久化到 ~/.fuxing/sessions/{Id}.json
    /// </summary>
    public class ChatSession
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; } = "新对话";

        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("updated_at")]
        public DateTime UpdatedAt { get; set; }

        [JsonProperty("agent_session_state", NullValueHandling = NullValueHandling.Ignore)]
        public string AgentSessionStateJson { get; set; }

        [JsonIgnore]
        public int MessageCount { get; set; }
    }

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

namespace FuXingAgent.Agents
{
    /// <summary>
    /// AgentSession 序列化用的消息结构。
    /// </summary>
    public class SessionMessage
    {
        [JsonProperty("role")]
        public string Role { get; set; }

        [JsonProperty("content")]
        public string Content { get; set; }

        [JsonProperty("tool_calls", NullValueHandling = NullValueHandling.Ignore)]
        public List<SessionToolCall> ToolCalls { get; set; }

        [JsonProperty("tool_call_id", NullValueHandling = NullValueHandling.Ignore)]
        public string ToolCallId { get; set; }

        [JsonProperty("tool_name", NullValueHandling = NullValueHandling.Ignore)]
        public string ToolName { get; set; }
    }

    /// <summary>
    /// AgentSession 序列化用的工具调用结构。
    /// </summary>
    public class SessionToolCall
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("function_name")]
        public string FunctionName { get; set; }

        [JsonProperty("arguments")]
        public string ArgumentsJson { get; set; }
    }
}
