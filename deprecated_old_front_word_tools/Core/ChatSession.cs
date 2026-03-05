using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;

namespace FuXing
{
    /// <summary>
    /// 一个会话的完整快照，用于持久化到磁盘。
    /// 存储路径: ~/.fuxing/sessions/{Id}.json
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

        [JsonProperty("messages")]
        public List<SessionMessage> Messages { get; set; } = new List<SessionMessage>();
    }

    /// <summary>
    /// 持久化用的消息结构，与 MemoryMessage 一一对应。
    /// 将 JObject 转为 string 存储，避免序列化问题。
    /// </summary>
    public class SessionMessage
    {
        [JsonProperty("role")]
        [JsonConverter(typeof(StringEnumConverter))]
        public ChatMessageRole Role { get; set; }

        [JsonProperty("content")]
        public string Content { get; set; }

        [JsonProperty("tool_calls", NullValueHandling = NullValueHandling.Ignore)]
        public List<SessionToolCall> ToolCalls { get; set; }

        [JsonProperty("tool_call_id", NullValueHandling = NullValueHandling.Ignore)]
        public string ToolCallId { get; set; }

        [JsonProperty("tool_name", NullValueHandling = NullValueHandling.Ignore)]
        public string ToolName { get; set; }
    }

    /// <summary>工具调用的持久化结构</summary>
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
