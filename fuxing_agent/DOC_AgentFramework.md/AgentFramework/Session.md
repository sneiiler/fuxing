# Session

---

- Session
- [https://learn.microsoft.com/en-us/agent-framework/agents/conversations/session?pivots=programming-language-csharp](https://learn.microsoft.com/en-us/agent-framework/agents/conversations/session?pivots=programming-language-csharp)
- Learn what AgentSession contains and how to create, restore, and serialize sessions.
- 2026-03-04 19:15

---

- [Edit](https://github.com/MicrosoftDocs/semantic-kernel-docs/blob/main/agent-framework/agents/conversations/session.md)

  ---

  #### Share via

  ---

## Session

Choose a programming languageC#Python

 `AgentSession` is the conversation state container used across agent runs.

## What `AgentSession` contains

|**Field**|**Purpose**|
| ------| --------------------------------------------|
|​`StateBag`|Arbitrary state container for this session|

The C# `AgentSession`​ is an abstract base class. Concrete implementations (created via `CreateSessionAsync()`) may add additional state e.g. an id for remote chat history storage, when service-managed history is used.

|**Field**|**Purpose**|
| ------| -------------------------------------------------------------------------------|
|​`session_id`|Local unique identifier for this session|
|​`service_session_id`|Remote service conversation identifier (when service-managed history is used)|
|​`state`|Mutable dictionary shared with context/history providers|

## Built-in usage pattern

C#

```csharp
AgentSession session = await agent.CreateSessionAsync();

var first = await agent.RunAsync("My name is Alice.", session);
var second = await agent.RunAsync("What is my name?", session);
```

Python

```python
session = agent.create_session()

first = await agent.run("My name is Alice.", session=session)
second = await agent.run("What is my name?", session=session)
```

## Creating a session from an existing service conversation ID

Create a new session from an existing conversation id varies by agent type. Here are some examples.

When using `ChatClientAgent`

C#

```csharp
AgentSession session = await chatClientAgent.CreateSessionAsync(conversationId);
```

When using an `A2AAgent`

C#

```csharp
AgentSession session = await a2aAgent.CreateSessionAsync(contextId, taskId);
```

Use this when the backing service already has conversation state.

Python

```python
session = agent.get_session(service_session_id="<service-conversation-id>")
response = await agent.run("Continue this conversation.", session=session)
```

## Serialization and restoration

C#

```csharp
var serialized = agent.SerializeSession(session);
AgentSession resumed = await agent.DeserializeSessionAsync(serialized);
```

Python

```python
serialized = session.to_dict()
resumed = AgentSession.from_dict(serialized)
```

Important

Sessions are agent/service-specific. Reusing a session with a different agent configuration or provider can lead to invalid context.

## Next steps

[Context Providers](https://learn.microsoft.com/en-us/agent-framework/agents/conversations/context-providers)

## In this article

1. [What AgentSession contains](https://learn.microsoft.com/en-us/agent-framework/agents/conversations/session?pivots=programming-language-csharp#what-agentsession-contains)
2. [Built-in usage pattern](https://learn.microsoft.com/en-us/agent-framework/agents/conversations/session?pivots=programming-language-csharp#built-in-usage-pattern)
3. [Creating a session from an existing service conversation ID](https://learn.microsoft.com/en-us/agent-framework/agents/conversations/session?pivots=programming-language-csharp#creating-a-session-from-an-existing-service-conversation-id)
4. [Serialization and restoration](https://learn.microsoft.com/en-us/agent-framework/agents/conversations/session?pivots=programming-language-csharp#serialization-and-restoration)
5. [Next steps](https://learn.microsoft.com/en-us/agent-framework/agents/conversations/session?pivots=programming-language-csharp#next-steps)

Was this page helpful?

Ask Learn is an AI assistant that can answer questions, clarify concepts, and define terms using trusted Microsoft documentation.

Please sign in to use Ask Learn.

[Sign in](https://learn.microsoft.com/en-us/agent-framework/agents/conversations/session?pivots=programming-language-csharp#)
