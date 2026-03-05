# Running Agents

---

- Running Agents
- [https://learn.microsoft.com/en-us/agent-framework/agents/running-agents?pivots=programming-language-csharp](https://learn.microsoft.com/en-us/agent-framework/agents/running-agents?pivots=programming-language-csharp)
- Learn how to run agents with Agent Framework
- 2026-03-04 19:13

---

- [Edit](https://github.com/MicrosoftDocs/semantic-kernel-docs/blob/main/agent-framework/agents/running-agents.md)

  ---

  #### Share via

  ---

## Running Agents

Choose a programming languageC#Python

 The base Agent abstraction exposes various options for running the agent. Callers can choose to supply zero, one, or many input messages. Callers can also choose between streaming and non-streaming. Let's dig into the different usage scenarios.

## Streaming and non-streaming

Microsoft Agent Framework supports both streaming and non-streaming methods for running an agent.

For non-streaming, use the `RunAsync` method.

C#

```csharp
Console.WriteLine(await agent.RunAsync("What is the weather like in Amsterdam?"));
```

For streaming, use the `RunStreamingAsync` method.

C#

```csharp
await foreach (var update in agent.RunStreamingAsync("What is the weather like in Amsterdam?"))
{
    Console.Write(update);
}
```

For non-streaming, use the `run` method.

Python

```python
result = await agent.run("What is the weather like in Amsterdam?")
print(result.text)
```

For streaming, use the `run`​ method with `stream=True`​. This returns a `ResponseStream` object that can be iterated asynchronously:

Python

```python
async for update in agent.run("What is the weather like in Amsterdam?", stream=True):
    if update.text:
        print(update.text, end="", flush=True)
```

### ResponseStream

The `ResponseStream`​ object returned by `run(..., stream=True)` supports two consumption patterns:

**Pattern 1: Async iteration** — process updates as they arrive for real-time display:

Python

```python
response_stream = agent.run("Tell me a story", stream=True)
async for update in response_stream:
    if update.text:
        print(update.text, end="", flush=True)
```

**Pattern 2: Direct finalization** — skip iteration and get the complete response:

Python

```python
response_stream = agent.run("Tell me a story", stream=True)
final = await response_stream.get_final_response()
print(final.text)
```

**Pattern 3: Combined** — iterate for real-time display, then get the aggregated result:

Python

```python
response_stream = agent.run("Tell me a story", stream=True)

# First, iterate to display streaming output
async for update in response_stream:
    if update.text:
        print(update.text, end="", flush=True)

# Then get the complete response (uses already-collected updates, does not re-iterate)
final = await response_stream.get_final_response()
print(f"\n\nFull response: {final.text}")
print(f"Messages: {len(final.messages)}")
```

## Agent run options

The base agent abstraction does allow passing an options object for each agent run, however the ability to customize a run at the abstraction level is quite limited. Agents can vary significantly and therefore there aren't really common customization options.

For cases where the caller knows the type of the agent they are working with, it is possible to pass type specific options to allow customizing the run.

For example, here the agent is a `ChatClientAgent`​ and it is possible to pass a `ChatClientAgentRunOptions`​ object that inherits from `AgentRunOptions`​. This allows the caller to provide custom [ChatOptions](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.chatoptions) that are merged with any agent level options before being passed to the `IChatClient`​ that the `ChatClientAgent` is built on.

C#

```csharp
var chatOptions = new ChatOptions() { Tools = [AIFunctionFactory.Create(GetWeather)] };
Console.WriteLine(await agent.RunAsync("What is the weather like in Amsterdam?", options: new ChatClientAgentRunOptions(chatOptions)));
```

Python agents support customizing each run via the `options`​ parameter. Options are passed as a TypedDict and can be set at both construction time (via `default_options`​) and per-run (via `options`). Each provider has its own TypedDict class that provides full IDE autocomplete and type checking for provider-specific settings.

Common options include:

- ​`max_tokens`: Maximum number of tokens to generate
- ​`temperature`: Controls randomness in response generation
- ​`model_id`: Override the model for this specific run
- ​`top_p`: Nucleus sampling parameter
- ​`response_format`: Specify the response format (e.g., structured output)

Note

The `tools`​ and `instructions`​ parameters remain as direct keyword arguments and are not passed via the `options` dictionary.

Python

```python
from agent_framework.openai import OpenAIChatClient, OpenAIChatOptions

# Set default options at construction time
agent = OpenAIChatClient().as_agent(
    instructions="You are a helpful assistant",
    default_options={
        "temperature": 0.7,
        "max_tokens": 500
    }
)

# Run with custom options (overrides defaults)
# OpenAIChatOptions provides IDE autocomplete for all OpenAI-specific settings
options: OpenAIChatOptions = {
    "temperature": 0.3,
    "max_tokens": 150,
    "model_id": "gpt-4o",
    "presence_penalty": 0.5,
    "frequency_penalty": 0.3
}

result = await agent.run(
    "What is the weather like in Amsterdam?",
    options=options
)

# Streaming with custom options
async for update in agent.run(
    "Tell me a detailed weather forecast",
    stream=True,
    options={"temperature": 0.7, "top_p": 0.9},
    tools=[additional_weather_tool]  # tools is still a keyword argument
):
    if update.text:
        print(update.text, end="", flush=True)
```

Each provider has its own TypedDict class (e.g., `OpenAIChatOptions`​, `AnthropicChatOptions`​, `OllamaChatOptions`) that exposes the full set of options supported by that provider.

When both `default_options`​ and per-run `options` are provided, the per-run options take precedence and are merged with the defaults.

## Response types

Both streaming and non-streaming responses from agents contain all content produced by the agent. Content might include data that is not the result (that is, the answer to the user question) from the agent. Examples of other data returned include function tool calls, results from function tool calls, reasoning text, status updates, and many more.

Since not all content returned is the result, it's important to look for specific content types when trying to isolate the result from the other content.

To extract the text result from a response, all `TextContent`​ items from all `ChatMessages`​ items need to be aggregated. To simplify this, a `Text`​ property is available on all response types that aggregates all `TextContent`.

For the non-streaming case, everything is returned in one `AgentResponse`​ object.`AgentResponse`​ allows access to the produced messages via the `Messages` property.

C#

```csharp
var response = await agent.RunAsync("What is the weather like in Amsterdam?");
Console.WriteLine(response.Text);
Console.WriteLine(response.Messages.Count);
```

For the streaming case, `AgentResponseUpdate`​ objects are streamed as they are produced. Each update might contain a part of the result from the agent, and also various other content items. Similar to the non-streaming case, it is possible to use the `Text`​ property to get the portion of the result contained in the update, and drill into the detail via the `Contents` property.

C#

```csharp
await foreach (var update in agent.RunStreamingAsync("What is the weather like in Amsterdam?"))
{
    Console.WriteLine(update.Text);
    Console.WriteLine(update.Contents.Count);
}
```

For the non-streaming case, everything is returned in one `AgentResponse`​ object.`AgentResponse`​ allows access to the produced messages via the `messages` property.

To extract the text result from a response, all `TextContent`​ items from all `Message`​ items need to be aggregated. To simplify this, a `Text`​ property is available on all response types that aggregates all `TextContent`.

Python

```python
response = await agent.run("What is the weather like in Amsterdam?")
print(response.text)
print(len(response.messages))

# Access individual messages
for message in response.messages:
    print(f"Role: {message.role}, Text: {message.text}")
```

For the streaming case, `AgentResponseUpdate`​ objects are streamed as they are produced via the `ResponseStream`​ returned by `run(..., stream=True)`​. Each update might contain a part of the result from the agent, and also various other content items. Similar to the non-streaming case, it is possible to use the `text`​ property to get the portion of the result contained in the update, and drill into the detail via the `contents` property.

Python

```python
response_stream = agent.run("What is the weather like in Amsterdam?", stream=True)
async for update in response_stream:
    print(f"Update text: {update.text}")
    print(f"Content count: {len(update.contents)}")

    # Access individual content items
    for content in update.contents:
        if hasattr(content, 'text'):
            print(f"Content: {content.text}")

# Get the aggregated final response after streaming
final = await response_stream.get_final_response()
print(f"Complete text: {final.text}")
```

## Message types

Input and output from agents are represented as messages. Messages are subdivided into content items.

The Microsoft Agent Framework uses the message and content types provided by the [Microsoft.Extensions.AI](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai) abstractions. Messages are represented by the `ChatMessage`​ class and all content classes inherit from the base `AIContent` class.

Various `AIContent`​ subclasses exist that are used to represent different types of content. Some are provided as part of the base [Microsoft.Extensions.AI](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai) abstractions, but providers can also add their own types, where needed.

Here are some popular types from [Microsoft.Extensions.AI](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai):

|**Type**|**Description**|
| --| -------------------------------------------------------------------------------------------------------------------------------------------------------------|
|[TextContent](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.textcontent)|Textual content that can be both input, for example, from a user or developer, and output from the agent. Typically contains the text result from an agent.|
|[DataContent](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.datacontent)|Binary content that can be both input and output. Can be used to pass image, audio or video data to and from the agent (where supported).|
|[UriContent](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.uricontent)|A URL that typically points at hosted content such as an image, audio or video.|
|[FunctionCallContent](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.functioncallcontent)|A request by an inference service to invoke a function tool.|
|[FunctionResultContent](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.functionresultcontent)|The result of a function tool invocation.|

The Python Agent Framework uses message and content types from the `agent_framework`​ package. Messages are represented by the `Message`​ class and all content classes inherit from the base `Content` class.

Various `Content` subclasses exist that are used to represent different types of content:

|**Type**|**Description**|
| ------| ------------------------------------------------------------------------------------------------------------------------|
|​`Content`|Unified content type with factory methods (`Content.from_text()`​,`Content.from_data()`​,`Content.from_uri()`​). Use the`type`property to check content type ("text", "data", "uri").|
|​`FunctionCallContent`|A request by an AI service to invoke a function tool.|
|​`FunctionResultContent`|The result of a function tool invocation.|
|​`ErrorContent`|Error information when processing fails.|
|​`UsageContent`|Token usage and billing information from the AI service.|

Here's how to work with different content types:

Python

```python
from agent_framework import Message, Content

# Create a text message
text_message = Message(role="user", contents=["Hello!"])

# Create a message with multiple content types
image_data = b"..."  # your image bytes
mixed_message = Message(
    role="user",
    contents=[
        Content.from_text("Analyze this image:"),
        Content.from_data(data=image_data, media_type="image/png"),
    ]
)

# Access content from responses
response = await agent.run("Describe the image")
for message in response.messages:
    for content in message.contents:
        if content.type == "text":
            print(f"Text: {content.text}")
        elif content.type == "data":
            print(f"Data URI: {content.uri}")
        elif content.type == "uri":
            print(f"External URI: {content.uri}")
```

## Next steps

[Multimodal](https://learn.microsoft.com/en-us/agent-framework/agents/multimodal)

Ask Learn is an AI assistant that can answer questions, clarify concepts, and define terms using trusted Microsoft documentation.

Please sign in to use Ask Learn.

[Sign in](https://learn.microsoft.com/en-us/agent-framework/agents/running-agents?pivots=programming-language-csharp#)
