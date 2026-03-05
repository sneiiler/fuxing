# Agent Background Responses

---

- Agent Background Responses
- [https://learn.microsoft.com/en-us/agent-framework/agents/background-responses?pivots=programming-language-csharp](https://learn.microsoft.com/en-us/agent-framework/agents/background-responses?pivots=programming-language-csharp)
- Learn how to handle long-running operations with background responses in Agent Framework
- 2026-03-04 19:14

---

- [Edit](https://github.com/MicrosoftDocs/semantic-kernel-docs/blob/main/agent-framework/agents/background-responses.md)

  ---

  #### Share via

  ---

## Agent Background Responses

Choose a programming languageC#Python

 The Microsoft Agent Framework supports background responses for handling long-running operations that may take time to complete. This feature enables agents to start processing a request and return a continuation token that can be used to poll for results or resume interrupted streams.

Tip

For a complete working example, see the [Background Responses sample](https://github.com/microsoft/agent-framework/blob/main/dotnet/samples/02-agents/Agents/Agent_Step14_BackgroundResponses/Program.cs).

## When to Use Background Responses

Background responses are particularly useful for:

- Complex reasoning tasks that require significant processing time
- Operations that may be interrupted by network issues or client timeouts
- Scenarios where you want to start a long-running task and check back later for results

## How Background Responses Work

Background responses use a **continuation token** mechanism to handle long-running operations. When you send a request to an agent with background responses enabled, one of two things happens:

1. ​**Immediate completion**: The agent completes the task quickly and returns the final response without a continuation token
2. ​**Background processing**: The agent starts processing in the background and returns a continuation token instead of the final result

The continuation token contains all necessary information to either poll for completion using the non-streaming agent API or resume an interrupted stream with streaming agent API. When the continuation token is `null`, the operation is complete - this happens when a background response has completed, failed, or cannot proceed further (for example, when user input is required).

## Enabling Background Responses

To enable background responses, set the `AllowBackgroundResponses`​ property to `true`​ in the `AgentRunOptions`:

C#

```csharp
AgentRunOptions options = new()
{
    AllowBackgroundResponses = true
};
```

Note

Currently, only agents that use the OpenAI Responses API support background responses: [OpenAI Responses Agent](https://learn.microsoft.com/en-us/agent-framework/agents/providers/openai) and [Azure OpenAI Responses Agent](https://learn.microsoft.com/en-us/agent-framework/agents/providers/azure-openai).

Some agents may not allow explicit control over background responses. These agents can decide autonomously whether to initiate a background response based on the complexity of the operation, regardless of the `AllowBackgroundResponses` setting.

## Non-Streaming Background Responses

For non-streaming scenarios, when you initially run an agent, it may or may not return a continuation token. If no continuation token is returned, it means the operation has completed. If a continuation token is returned, it indicates that the agent has initiated a background response that is still processing and will require polling to retrieve the final result:

C#

```csharp
AIAgent agent = new AzureOpenAIClient(
    new Uri("https://<myresource>.openai.azure.com"),
    new DefaultAzureCredential())
    .GetOpenAIResponseClient("<deployment-name>")
    .AsAIAgent();

AgentRunOptions options = new()
{
    AllowBackgroundResponses = true
};

AgentSession session = await agent.CreateSessionAsync();

// Get initial response - may return with or without a continuation token
AgentResponse response = await agent.RunAsync("Write a very long novel about otters in space.", session, options);

// Continue to poll until the final response is received
while (response.ContinuationToken is not null)
{
    // Wait before polling again.
    await Task.Delay(TimeSpan.FromSeconds(2));

    options.ContinuationToken = response.ContinuationToken;
    response = await agent.RunAsync(session, options);
}

Console.WriteLine(response.Text);
```

Warning

​`DefaultAzureCredential`​ is convenient for development but requires careful consideration in production. In production, consider using a specific credential (e.g., `ManagedIdentityCredential`) to avoid latency issues, unintended credential probing, and potential security risks from fallback mechanisms.

### Key Points:

- The initial call may complete immediately (no continuation token) or start a background operation (with continuation token)
- If no continuation token is returned, the operation is complete and the response contains the final result
- If a continuation token is returned, the agent has started a background process that requires polling
- Use the continuation token from the previous response in subsequent polling calls
- When `ContinuationToken`​ is `null`, the operation is complete

## Streaming Background Responses

In streaming scenarios, background responses work much like regular streaming responses - the agent streams all updates back to consumers in real-time. However, the key difference is that if the original stream gets interrupted, agents support stream resumption through continuation tokens. Each update includes a continuation token that captures the current state, allowing the stream to be resumed from exactly where it left off by passing this token to subsequent streaming API calls:

C#

```csharp
AIAgent agent = new AzureOpenAIClient(
    new Uri("https://<myresource>.openai.azure.com"),
    new DefaultAzureCredential())
    .GetOpenAIResponseClient("<deployment-name>")
    .AsAIAgent();

AgentRunOptions options = new()
{
    AllowBackgroundResponses = true
};

AgentSession session = await agent.CreateSessionAsync();

AgentResponseUpdate? latestReceivedUpdate = null;

await foreach (var update in agent.RunStreamingAsync("Write a very long novel about otters in space.", session, options))
{
    Console.Write(update.Text);

    latestReceivedUpdate = update;

    // Simulate an interruption
    break;
}

// Resume from interruption point captured by the continuation token
options.ContinuationToken = latestReceivedUpdate?.ContinuationToken;
await foreach (var update in agent.RunStreamingAsync(session, options))
{
    Console.Write(update.Text);
}
```

### Key Points:

- Each `AgentResponseUpdate` contains a continuation token that can be used for resumption
- Store the continuation token from the last received update before interruption
- Use the stored continuation token to resume the stream from the interruption point

Tip

See the [.NET samples](https://github.com/microsoft/agent-framework/tree/main/dotnet/samples) for complete runnable examples.

Tip

For a complete working example, see the [Background Responses sample](https://github.com/microsoft/agent-framework/blob/main/python/samples/02-agents/background_responses.py).

## Enabling Background Responses

To enable background responses, pass the `background`​ option when calling `agent.run()`:

Python

```python
session = agent.create_session()
response = await agent.run(
    messages="Your prompt here",
    session=session,
    options={"background": True},
)
```

Note

Currently, only agents that use the OpenAI Responses API support background responses: [OpenAI Responses Agent](https://learn.microsoft.com/en-us/agent-framework/agents/providers/openai) and [Azure OpenAI Responses Agent](https://learn.microsoft.com/en-us/agent-framework/agents/providers/azure-openai).

## Non-Streaming Background Responses

For non-streaming scenarios, when you initially run an agent with `background=True`​, it may return immediately with a `continuation_token`​. If `continuation_token`​ is `None`, the operation has completed. Otherwise, poll by passing the token back in subsequent calls:

Python

```python
import asyncio
from agent_framework import Agent
from agent_framework.openai import OpenAIResponsesClient

agent = Agent(
    name="researcher",
    instructions="You are a helpful research assistant.",
    client=OpenAIResponsesClient(model_id="o3"),
)

session = await agent.create_session()

# Start a background run — returns immediately
response = await agent.run(
    messages="Briefly explain the theory of relativity in two sentences.",
    session=session,
    options={"background": True},
)

# Poll until the operation completes
while response.continuation_token is not None:
    await asyncio.sleep(2)
    response = await agent.run(
        session=session,
        options={"continuation_token": response.continuation_token},
    )

# Done — response.text contains the final result
print(response.text)
```

### Key Points

- The initial call may complete immediately (no continuation token) or start a background operation (with continuation token)
- Use the `continuation_token` from the previous response in subsequent polling calls
- When `continuation_token`​ is `None`, the operation is complete

## Streaming Background Responses

In streaming scenarios, background responses work like regular streaming — the agent streams updates back in real time. The key difference is that each update includes a `continuation_token`, enabling stream resumption if the connection is interrupted:

Python

```python
session = await agent.create_session()

# Start a streaming background run
last_token = None
stream = agent.run(
    messages="Briefly list three benefits of exercise.",
    stream=True,
    session=session,
    options={"background": True},
)

# Read chunks — each update carries a continuation_token
async for update in stream:
    last_token = update.continuation_token
    if update.text:
        print(update.text, end="", flush=True)
    # If interrupted (e.g., network issue), break and resume later
```

### Resuming an Interrupted Stream

If the stream is interrupted, use the last `continuation_token` to resume from where it left off:

Python

```python
if last_token is not None:
    stream = agent.run(
        stream=True,
        session=session,
        options={"continuation_token": last_token},
    )
    async for update in stream:
        if update.text:
            print(update.text, end="", flush=True)
```

### Key Points

- Each `AgentResponseUpdate`​ contains a `continuation_token` for resumption
- Store the token from the last received update before interruption
- Pass the stored token via `options={"continuation_token": token}` to resume

## Best Practices

When working with background responses, consider the following best practices:

- **Implement appropriate polling intervals** to avoid overwhelming the service
- **Use exponential backoff** for polling intervals if the operation is taking longer than expected
- **Always check for** **​`null`​**​ **continuation tokens** to determine when processing is complete
- **Consider storing continuation tokens persistently** for operations that may span user sessions

## Limitations and Considerations

- Background responses are dependent on the underlying AI service supporting long-running operations
- Not all agent types may support background responses
- Network interruptions or client restarts may require special handling to persist continuation tokens

## Next steps

[RAG](https://learn.microsoft.com/en-us/agent-framework/agents/rag)

## In this article

1. [When to Use Background Responses](https://learn.microsoft.com/en-us/agent-framework/agents/background-responses?pivots=programming-language-csharp#when-to-use-background-responses)
2. [How Background Responses Work](https://learn.microsoft.com/en-us/agent-framework/agents/background-responses?pivots=programming-language-csharp#how-background-responses-work)
3. [Enabling Background Responses](https://learn.microsoft.com/en-us/agent-framework/agents/background-responses?pivots=programming-language-csharp#enabling-background-responses)
4. [Non-Streaming Background Responses](https://learn.microsoft.com/en-us/agent-framework/agents/background-responses?pivots=programming-language-csharp#non-streaming-background-responses)
5. [Streaming Background Responses](https://learn.microsoft.com/en-us/agent-framework/agents/background-responses?pivots=programming-language-csharp#streaming-background-responses)
6. [Best Practices](https://learn.microsoft.com/en-us/agent-framework/agents/background-responses?pivots=programming-language-csharp#best-practices)
7. [Limitations and Considerations](https://learn.microsoft.com/en-us/agent-framework/agents/background-responses?pivots=programming-language-csharp#limitations-and-considerations)
8. [Next steps](https://learn.microsoft.com/en-us/agent-framework/agents/background-responses?pivots=programming-language-csharp#next-steps)

Was this page helpful?

Ask Learn is an AI assistant that can answer questions, clarify concepts, and define terms using trusted Microsoft documentation.

Please sign in to use Ask Learn.

[Sign in](https://learn.microsoft.com/en-us/agent-framework/agents/background-responses?pivots=programming-language-csharp#)
