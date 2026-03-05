# Using images with an agent

---

- Using images with an agent
- [https://learn.microsoft.com/en-us/agent-framework/agents/multimodal?pivots=programming-language-csharp](https://learn.microsoft.com/en-us/agent-framework/agents/multimodal?pivots=programming-language-csharp)
- Learn how to use images with an agent
- 2026-03-04 19:13

---

- [Edit](https://github.com/MicrosoftDocs/semantic-kernel-docs/blob/main/agent-framework/agents/multimodal.md)

  ---

  #### Share via

  ---

## Using images with an agent

Choose a programming languageC#Python

 This tutorial shows you how to use images with an agent, allowing the agent to analyze and respond to image content.

## Passing images to the agent

You can send images to an agent by creating a `ChatMessage` that includes both text and image content. The agent can then analyze the image and respond accordingly.

First, create an `AIAgent` that is able to analyze images.

C#

```csharp
AIAgent agent = new AzureOpenAIClient(
    new Uri("https://<myresource>.openai.azure.com"),
    new DefaultAzureCredential())
    .GetChatClient("gpt-4o")
    .AsAIAgent(
        name: "VisionAgent",
        instructions: "You are a helpful agent that can analyze images");
```

Warning

​`DefaultAzureCredential`​ is convenient for development but requires careful consideration in production. In production, consider using a specific credential (e.g., `ManagedIdentityCredential`) to avoid latency issues, unintended credential probing, and potential security risks from fallback mechanisms.

Next, create a `ChatMessage`​ that contains both a text prompt and an image URL. Use `TextContent`​ for the text and `UriContent` for the image.

C#

```csharp
ChatMessage message = new(ChatRole.User, [
    new TextContent("What do you see in this image?"),
    new UriContent("https://upload.wikimedia.org/wikipedia/commons/thumb/d/dd/Gfp-wisconsin-madison-the-nature-boardwalk.jpg/2560px-Gfp-wisconsin-madison-the-nature-boardwalk.jpg", "image/jpeg")
]);
```

Run the agent with the message. You can use streaming to receive the response as it is generated.

C#

```csharp
Console.WriteLine(await agent.RunAsync(message));
```

This will print the agent's analysis of the image to the console.

## Passing images to the agent

You can send images to an agent by creating a `Message` that includes both text and image content. The agent can then analyze the image and respond accordingly.

First, create an agent that is able to analyze images.

Python

```python
import asyncio
from agent_framework.azure import AzureOpenAIChatClient
from azure.identity import AzureCliCredential

agent = AzureOpenAIChatClient(credential=AzureCliCredential()).as_agent(
    name="VisionAgent",
    instructions="You are a helpful agent that can analyze images"
)
```

Next, create a `Message`​ that contains both a text prompt and an image URL. Use `Content.from_text()`​ for the text and `Content.from_uri()` for the image.

Python

```python
from agent_framework import Message, Content

message = Message(
    role="user",
    contents=[
        Content.from_text(text="What do you see in this image?"),
        Content.from_uri(
            uri="https://upload.wikimedia.org/wikipedia/commons/thumb/d/dd/Gfp-wisconsin-madison-the-nature-boardwalk.jpg/2560px-Gfp-wisconsin-madison-the-nature-boardwalk.jpg",
            media_type="image/jpeg"
        )
    ]
)
```

You can also load an image from your local file system using `Content.from_data()`:

Python

```python
from agent_framework import Message, Content

# Load image from local file
with open("path/to/your/image.jpg", "rb") as f:
    image_bytes = f.read()

message = Message(
    role="user",
    contents=[
        Content.from_text(text="What do you see in this image?"),
        Content.from_data(
            data=image_bytes,
            media_type="image/jpeg"
        )
    ]
)
```

Run the agent with the message. You can use streaming to receive the response as it is generated.

Python

```python
async def main():
    result = await agent.run(message)
    print(result.text)

asyncio.run(main())
```

This will print the agent's analysis of the image to the console.

## Next steps

[Structured Output](https://learn.microsoft.com/en-us/agent-framework/agents/structured-output)

## In this article

1. [Passing images to the agent](https://learn.microsoft.com/en-us/agent-framework/agents/multimodal?pivots=programming-language-csharp#passing-images-to-the-agent)
2. [Next steps](https://learn.microsoft.com/en-us/agent-framework/agents/multimodal?pivots=programming-language-csharp#next-steps)

Was this page helpful?

Ask Learn is an AI assistant that can answer questions, clarify concepts, and define terms using trusted Microsoft documentation.

Please sign in to use Ask Learn.

[Sign in](https://learn.microsoft.com/en-us/agent-framework/agents/multimodal?pivots=programming-language-csharp#)
