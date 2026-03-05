# Conversations & Memory overview in Agent Framework

---

- Conversations & Memory overview in Agent Framework
- [https://learn.microsoft.com/en-us/agent-framework/agents/conversations/?pivots=programming-language-csharp](https://learn.microsoft.com/en-us/agent-framework/agents/conversations/?pivots=programming-language-csharp)
- Learn the core AgentSession usage pattern and how to navigate sessions, context providers, and storage.
- 2026-03-04 19:15

---

- [Edit](https://github.com/MicrosoftDocs/semantic-kernel-docs/blob/main/agent-framework/agents/conversations/index.md)

  ---

  #### Share via

  ---

## Conversations & Memory overview

Choose a programming languageC#Python

 Use `AgentSession` to keep conversation context between invocations.

## Core usage pattern

Most applications follow the same flow:

1. Create a session (`CreateSessionAsync()`)
2. Pass that session to each `RunAsync(...)`
3. Rehydrate from serialized state (`DeserializeSessionAsync(...)`)
4. Continue with a service conversation ID (varies by agent, e.g. `myChatClientAgent.CreateSessionAsync("existing-id")`)
5. Create a session (`create_session()`)
6. Pass that session to each `run(...)`
7. Rehydrate by service conversation ID (`get_session(...)`) or from serialized state

C#

```csharp
// Create and reuse a session
AgentSession session = await agent.CreateSessionAsync();

var first = await agent.RunAsync("My name is Alice.", session);
var second = await agent.RunAsync("What is my name?", session);

// Persist and restore later
var serialized = agent.SerializeSession(session);
AgentSession resumed = await agent.DeserializeSessionAsync(serialized);
```

Python

```python
# Create and reuse a session
session = agent.create_session()

first = await agent.run("My name is Alice.", session=session)
second = await agent.run("What is my name?", session=session)

# Rehydrate by service conversation ID when needed
service_session = agent.get_session(service_session_id="<service-conversation-id>")

# Persist and restore later
serialized = session.to_dict()
resumed = AgentSession.from_dict(serialized)
```

## Guide map

|**Page**|**Focus**|
| --| ------------------------------------------------------------|
|[Session](https://learn.microsoft.com/en-us/agent-framework/agents/conversations/session)|​`AgentSession`structure and serialization|
|[Context Providers](https://learn.microsoft.com/en-us/agent-framework/agents/conversations/context-providers)|Built-in and custom context/history provider patterns|
|[Storage](https://learn.microsoft.com/en-us/agent-framework/agents/conversations/storage)|Built-in storage modes and external persistence strategies|

## Next steps

[Session](https://learn.microsoft.com/en-us/agent-framework/agents/conversations/session)

## In this article

1. [Core usage pattern](https://learn.microsoft.com/en-us/agent-framework/agents/conversations/?pivots=programming-language-csharp#core-usage-pattern)
2. [Guide map](https://learn.microsoft.com/en-us/agent-framework/agents/conversations/?pivots=programming-language-csharp#guide-map)
3. [Next steps](https://learn.microsoft.com/en-us/agent-framework/agents/conversations/?pivots=programming-language-csharp#next-steps)

Was this page helpful?

Ask Learn is an AI assistant that can answer questions, clarify concepts, and define terms using trusted Microsoft documentation.

Please sign in to use Ask Learn.

[Sign in](https://learn.microsoft.com/en-us/agent-framework/agents/conversations/?pivots=programming-language-csharp#)
