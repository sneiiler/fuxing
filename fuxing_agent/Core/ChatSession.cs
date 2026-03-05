using Newtonsoft.Json;
using System;

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
}
