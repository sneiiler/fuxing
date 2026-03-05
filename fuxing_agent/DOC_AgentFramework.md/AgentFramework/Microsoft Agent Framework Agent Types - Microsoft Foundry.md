# Microsoft Agent Framework Agent Types - Microsoft Foundry

---

- Microsoft Agent Framework Agent Types - Microsoft Foundry
- [https://learn.microsoft.com/en-us/agent-framework/agents/?pivots=programming-language-csharp](https://learn.microsoft.com/en-us/agent-framework/agents/?pivots=programming-language-csharp)
- Learn different Agent Framework agent types.
- 2026-03-04 19:13

---

- [Edit](https://github.com/MicrosoftDocs/semantic-kernel-docs/blob/main/agent-framework/agents/index.md)

  ---

  #### Share via

  ---

## Microsoft Agent Framework agent types

Choose a programming languageC#Python

 The Microsoft Agent Framework provides support for several types of agents to accommodate different use cases and requirements.

All agents are derived from a common base class, `AIAgent`, which provides a consistent interface for all agent types. This allows for building common, agent agnostic, higher level functionality such as multi-agent orchestrations.

All agents are derived from a common base class, [`Agent`](https://learn.microsoft.com/en-us/python/api/agent-framework-core/agent_framework.agent?view=agent-framework-python-latest), which provides a consistent interface for all agent types. This allows for building common, agent agnostic, higher level functionality such as multi-agent orchestrations.

## Default Agent Runtime Execution Model

All agents in the Microsoft Agent Framework execute using a structured runtime model. This model coordinates user interaction, model inference, and tool execution in a deterministic loop.

![AI Agent Diagram](assets/agent-20260304191338-iaho4qq.svg)

Important

If you use Microsoft Agent Framework to build applications that operate with third-party servers or agents, you do so at your own risk. We recommend reviewing all data being shared with third-party servers or agents and being cognizant of third-party practices for retention and location of data. It is your responsibility to manage whether your data will flow outside of your organization's Azure compliance and geographic boundaries and any related implications.

## Simple agents based on inference services

Agent Framework makes it easy to create simple agents based on many different inference services. Any inference service that provides a [`Microsoft.Extensions.AI.IChatClient`](https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai#the-ichatclient-interface)​ implementation can be used to build these agents. The `Microsoft.Agents.AI.ChatClientAgent`​ is the agent class used to provide an agent for any [IChatClient](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.ichatclient) implementation.

These agents support a wide range of functionality out of the box:

1. Function calling.
2. Multi-turn conversations with local chat history management or service provided chat history management.
3. Custom service provided tools (for example, MCP, Code Execution).
4. Structured output.

To create one of these agents, simply construct a `ChatClientAgent`​ using the `IChatClient` implementation of your choice.

C#

```csharp
using Microsoft.Agents.AI;

var agent = new ChatClientAgent(chatClient, instructions: "You are a helpful assistant");
```

To make creating these agents even easier, Agent Framework provides helpers for many popular services. For more information, see the documentation for each service.

|**Underlying inference service**|**Description**|**Service chat history storage supported**|**InMemory/Custom chat history storage supported**|
| ----| ---------------------------------------------------------------------------------------------------------| --------| --------|
|[Microsoft Foundry Agent](https://learn.microsoft.com/en-us/agent-framework/agents/providers/azure-ai-foundry)|An agent that uses the Foundry Agent Service as its backend.|Yes|No|
|[Foundry Models ChatCompletion](https://learn.microsoft.com/en-us/agent-framework/agents/providers/azure-ai-foundry)|An agent that uses any of the models deployed in the Foundry Service as its backend via ChatCompletion.|No|Yes|
|[Foundry Models Responses](https://learn.microsoft.com/en-us/agent-framework/agents/providers/azure-ai-foundry)|An agent that uses any of the models deployed in the Foundry Service as its backend via Responses.|Yes|Yes|
|[Foundry Anthropic](https://learn.microsoft.com/en-us/agent-framework/agents/providers/anthropic)|An agent that uses a Claude model via the Foundry Anthropic Service as its backend.|No|Yes|
|[Azure OpenAI ChatCompletion](https://learn.microsoft.com/en-us/agent-framework/agents/providers/azure-openai)|An agent that uses the Azure OpenAI ChatCompletion service.|No|Yes|
|[Azure OpenAI Responses](https://learn.microsoft.com/en-us/agent-framework/agents/providers/azure-openai)|An agent that uses the Azure OpenAI Responses service.|Yes|Yes|
|[Anthropic](https://learn.microsoft.com/en-us/agent-framework/agents/providers/anthropic)|An agent that uses a Claude model via the Anthropic Service as its backend.|No|Yes|
|[OpenAI ChatCompletion](https://learn.microsoft.com/en-us/agent-framework/agents/providers/openai)|An agent that uses the OpenAI ChatCompletion service.|No|Yes|
|[OpenAI Responses](https://learn.microsoft.com/en-us/agent-framework/agents/providers/openai)|An agent that uses the OpenAI Responses service.|Yes|Yes|
|[OpenAI Assistants](https://learn.microsoft.com/en-us/agent-framework/agents/providers/openai)|An agent that uses the OpenAI Assistants service.|Yes|No|
|[Any other](https://learn.microsoft.com/en-us/agent-framework/agents/providers/custom)​[`IChatClient`](https://learn.microsoft.com/en-us/agent-framework/agents/providers/custom)|You can also use any other[`Microsoft.Extensions.AI.IChatClient`](https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai#the-ichatclient-interface)implementation to create an agent.|Varies|Varies|

## Complex custom agents

It's also possible to create fully custom agents that aren't just wrappers around an `IChatClient`​. The agent framework provides the `AIAgent` base type. This base type is the core abstraction for all agents, which, when subclassed, allows for complete control over the agent's behavior and capabilities.

For more information, see the documentation for [Custom Agents](https://learn.microsoft.com/en-us/agent-framework/agents/providers/custom).

## Proxies for remote agents

Agent Framework provides out of the box `AIAgent` implementations for common service hosted agent protocols, such as A2A. This way you can easily connect to and use remote agents from your application.

See the documentation for each agent type, for more information:

|**Protocol**|**Description**|
| --| -------------------------------------------------------------------------|
|[A2A](https://learn.microsoft.com/en-us/agent-framework/integrations/a2a)|An agent that serves as a proxy to a remote agent via the A2A protocol.|

## Azure and OpenAI SDK Options Reference

When using Foundry, Azure OpenAI, OpenAI services, or Anthropic services, you have various SDK options to connect to these services. In some cases, it is possible to use multiple SDKs to connect to the same service or to use the same SDK to connect to different services. Here is a list of the different options available with the url that you should use when connecting to each. Make sure to replace `<resource>`​ and `<project>` with your actual resource and project names.

|**AI service**|**SDK**|**Nuget**|**Url**|
| -----------| --------------------------------| --| -------------------------------------------------------------------------------------------------------|
|[Foundry Models](https://learn.microsoft.com/en-us/azure/ai-foundry/concepts/foundry-models-overview)|Azure OpenAI SDK<sup>2</sup>|[Azure.AI.OpenAI](https://www.nuget.org/packages/Azure.AI.OpenAI)|https://ai-foundry-\<resource\>.services.ai.azure.com/|
|[Foundry Models](https://learn.microsoft.com/en-us/azure/ai-foundry/concepts/foundry-models-overview)|OpenAI SDK<sup>3</sup>|[OpenAI](https://www.nuget.org/packages/OpenAI)|https://ai-foundry-\<resource\>.services.ai.azure.com/openai/v1/|
|[Foundry Models](https://learn.microsoft.com/en-us/azure/ai-foundry/concepts/foundry-models-overview)|Azure AI Inference SDK<sup>2</sup>|[Azure.AI.Inference](https://www.nuget.org/packages/Azure.AI.Inference)|https://ai-foundry-\<resource\>.services.ai.azure.com/models|
|[Foundry Agents](https://learn.microsoft.com/en-us/azure/ai-foundry/agents/overview)|Azure AI Persistent Agents SDK|[Azure.AI.Agents.Persistent](https://www.nuget.org/packages/Azure.AI.Agents.Persistent)|https://ai-foundry-\<resource\>.services.ai.azure.com/api/projects/ai-project-\<project\>|
|[Azure OpenAI](https://learn.microsoft.com/en-us/azure/ai-foundry/openai/overview) <sup>1</sup>|Azure OpenAI SDK<sup>2</sup>|[Azure.AI.OpenAI](https://www.nuget.org/packages/Azure.AI.OpenAI)|https://\<resource\>.openai.azure.com/|
|[Azure OpenAI](https://learn.microsoft.com/en-us/azure/ai-foundry/openai/overview) <sup>1</sup>|OpenAI SDK|[OpenAI](https://www.nuget.org/packages/OpenAI)|https://\<resource\>.openai.azure.com/openai/v1/|
|OpenAI|OpenAI SDK|[OpenAI](https://www.nuget.org/packages/OpenAI)|No url required|
|[Azure AI Foundry Anthropic](https://learn.microsoft.com/en-us/azure/ai-foundry/foundry-models/how-to/use-foundry-models-claude?view=foundry-classic)|Anthropic Foundry SDK|[Anthropic.Foundry](https://www.nuget.org/packages/Anthropic.Foundry)|Resource name required|
|Anthropic|Anthropic SDK|[Anthropic](https://www.nuget.org/packages/Anthropic)|No url or resource name required|

1. [Upgrading from Azure OpenAI to Foundry](https://learn.microsoft.com/en-us/azure/ai-foundry/how-to/upgrade-azure-openai)
2. We recommend using the OpenAI SDK.
3. While we recommend using the OpenAI SDK to access Foundry models, Foundry Models support models from many different vendors, not just OpenAI. All these models are supported via the OpenAI SDK.

### Using the OpenAI SDK

As shown in the table above, the OpenAI SDK can be used to connect to multiple services. Depending on the service you are connecting to, you may need to set a custom URL when creating the `OpenAIClient`. You can also use different authentication mechanisms depending on the service.

If a custom URL is required (see table above), you can set it via the OpenAIClientOptions.

C#

```csharp
var clientOptions = new OpenAIClientOptions() { Endpoint = new Uri(serviceUrl) };
```

It's possible to use an API key when creating the client.

C#

```csharp
OpenAIClient client = new OpenAIClient(new ApiKeyCredential(apiKey), clientOptions);
```

When using an Azure Service, it's also possible to use Azure credentials instead of an API key.

C#

```csharp
OpenAIClient client = new OpenAIClient(new BearerTokenPolicy(new DefaultAzureCredential(), "https://ai.azure.com/.default"), clientOptions)
```

Warning

​`DefaultAzureCredential`​ is convenient for development but requires careful consideration in production. In production, consider using a specific credential (e.g., `ManagedIdentityCredential`) to avoid latency issues, unintended credential probing, and potential security risks from fallback mechanisms.

Once you have created the OpenAIClient, you can get a sub client for the specific service you want to use and then create an `AIAgent` from that.

C#

```csharp
AIAgent agent = client
    .GetChatClient(model)
    .AsAIAgent(instructions: "You are good at telling jokes.", name: "Joker");
```

### Using the Azure OpenAI SDK

This SDK can be used to connect to both Azure OpenAI and Foundry Models services. Either way, you will need to supply the correct service URL when creating the `AzureOpenAIClient`. See the table above for the correct URL to use.

C#

```csharp
AIAgent agent = new AzureOpenAIClient(
    new Uri(serviceUrl),
    new DefaultAzureCredential())
     .GetChatClient(deploymentName)
     .AsAIAgent(instructions: "You are good at telling jokes.", name: "Joker");
```

### Using the Azure AI Persistent Agents SDK

This SDK is only supported with the Agent Service. See the table above for the correct URL to use.

C#

```csharp
var persistentAgentsClient = new PersistentAgentsClient(serviceUrl, new DefaultAzureCredential());
AIAgent agent = await persistentAgentsClient.CreateAIAgentAsync(
    model: deploymentName,
    instructions: "You are good at telling jokes.",
    name: "Joker");
```

### Using the Azure AI Foundry Anthropic SDK

The resource is the subdomain name / first name coming before '.services.ai.azure.com' in the endpoint Uri.

For example: `https://(resource name).services.ai.azure.com/anthropic/v1/chat/completions`

C#

```csharp
var client = new AnthropicFoundryClient(new AnthropicFoundryApiKeyCredentials(apiKey, resource));
AIAgent agent = client.AsAIAgent(
    model: deploymentName,
    instructions: "Joker",
    name: "You are good at telling jokes.");
```

### Using the Anthropic SDK

C#

```csharp
var client = new AnthropicClient() { ApiKey = apiKey };
AIAgent agent = client.AsAIAgent(
    model: deploymentName,
    instructions: "Joker",
    name: "You are good at telling jokes.");
```

## Simple agents based on inference services

Agent Framework makes it easy to create simple agents based on many different inference services. Any inference service that provides a chat client implementation can be used to build these agents. This can be done using the [`SupportsChatGetResponse`](https://learn.microsoft.com/en-us/python/api/agent-framework-core/agent_framework.supportschatgetresponse?view=agent-framework-python-latest)​, which defines a standard for the methods that a client needs to support to be used with the standard [`Agent`](https://learn.microsoft.com/en-us/python/api/agent-framework-core/agent_framework.agent?view=agent-framework-python-latest) class.

These agents support a wide range of functionality out of the box:

1. Function calling
2. Multi-turn conversations with local chat history management or service provided chat history management
3. Custom service provided tools (for example, MCP, Code Execution)
4. Structured output
5. Streaming responses

To create one of these agents, simply construct an [`Agent`](https://learn.microsoft.com/en-us/python/api/agent-framework-core/agent_framework.agent?view=agent-framework-python-latest) using the chat client implementation of your choice.

Python

```python
import os
from agent_framework import Agent
from agent_framework.azure import AzureOpenAIResponsesClient
from azure.identity.aio import DefaultAzureCredential

Agent(
    client=AzureOpenAIResponsesClient(credential=DefaultAzureCredential(), project_endpoint=os.getenv("AZURE_AI_PROJECT_ENDPOINT"), deployment_name=os.getenv("AZURE_OPENAI_RESPONSES_DEPLOYMENT_NAME")),
    instructions="You are a helpful assistant"
) as agent
response = await agent.run("Hello!")
```

Alternatively, you can use the convenience method on the chat client:

Python

```python
from agent_framework.azure import AzureOpenAIResponsesClient
from azure.identity.aio import DefaultAzureCredential

agent = AzureOpenAIResponsesClient(credential=DefaultAzureCredential(), project_endpoint=os.getenv("AZURE_AI_PROJECT_ENDPOINT"), deployment_name=os.getenv("AZURE_OPENAI_RESPONSES_DEPLOYMENT_NAME")).as_agent(
    instructions="You are a helpful assistant"
)
```

Note

This example shows using the AzureOpenAIResponsesClient, but the same pattern applies to any chat client that implements [`SupportsChatGetResponse`](https://learn.microsoft.com/en-us/python/api/agent-framework-core/agent_framework.supportschatgetresponse?view=agent-framework-python-latest)​, see [providers overview](https://learn.microsoft.com/en-us/agent-framework/agents/providers/) for more details on other clients.

For detailed examples, see the agent-specific documentation sections below.

### Supported Chat Providers

|**Underlying Inference Service**|**Description**|**Service Chat History storage supported**|
| --| -------------------------------------------------------------------------------------------| --------|
|[Foundry Agent](https://learn.microsoft.com/en-us/agent-framework/agents/providers/azure-ai-foundry)|An agent that uses the Agent Service as its backend.|Yes|
|[Azure OpenAI Chat Completion](https://learn.microsoft.com/en-us/agent-framework/agents/providers/azure-openai)|An agent that uses the Azure OpenAI Chat Completion service.|No|
|[Azure OpenAI Responses](https://learn.microsoft.com/en-us/agent-framework/agents/providers/azure-openai)|An agent that uses the Azure OpenAI Responses service.|Yes|
|[Azure OpenAI Assistants](https://learn.microsoft.com/en-us/agent-framework/agents/providers/azure-openai)|An agent that uses the Azure OpenAI Assistants service.|Yes|
|[OpenAI Chat Completion](https://learn.microsoft.com/en-us/agent-framework/agents/providers/openai)|An agent that uses the OpenAI Chat Completion service.|No|
|[OpenAI Responses](https://learn.microsoft.com/en-us/agent-framework/agents/providers/openai)|An agent that uses the OpenAI Responses service.|Yes|
|[OpenAI Assistants](https://learn.microsoft.com/en-us/agent-framework/agents/providers/openai)|An agent that uses the OpenAI Assistants service.|Yes|
|[Anthropic Claude](https://learn.microsoft.com/en-us/agent-framework/agents/providers/anthropic)|An agent that uses Anthropic Claude models.|No|
|[Amazon Bedrock](https://github.com/microsoft/agent-framework/tree/main/python/packages/bedrock)|An agent that uses Amazon Bedrock models through the Agent Framework Bedrock chat client.|No|
|[GitHub Copilot](https://learn.microsoft.com/en-us/agent-framework/agents/providers/github-copilot)|An agent that uses the GitHub Copilot SDK backend.|No|
|[Ollama (OpenAI-compatible)](https://learn.microsoft.com/en-us/agent-framework/agents/providers/ollama)|An agent that uses locally hosted Ollama models via OpenAI-compatible APIs.|No|
|[Any other ChatClient](https://learn.microsoft.com/en-us/agent-framework/agents/providers/custom)|You can also use any other implementation of[`SupportsChatGetResponse`](https://learn.microsoft.com/en-us/python/api/agent-framework-core/agent_framework.supportschatgetresponse?view=agent-framework-python-latest)to create an agent.|Varies|

Custom chat history storage is supported whenever session-based conversation state is supported.

### Streaming Responses

Agents support both regular and streaming responses:

Python

```python
# Regular response (wait for complete result)
response = await agent.run("What's the weather like in Seattle?")
print(response.text)

# Streaming response (get results as they are generated)
async for chunk in agent.run("What's the weather like in Portland?", stream=True):
    if chunk.text:
        print(chunk.text, end="", flush=True)
```

For streaming examples, see:

- [Azure AI streaming examples](https://github.com/microsoft/agent-framework/blob/main/python/samples/02-agents/providers/azure_ai/azure_ai_basic.py)
- [Azure OpenAI streaming examples](https://github.com/microsoft/agent-framework/blob/main/python/samples/02-agents/providers/azure_openai/azure_chat_client_basic.py)
- [OpenAI streaming examples](https://github.com/microsoft/agent-framework/blob/main/python/samples/02-agents/providers/openai/openai_chat_client_basic.py)

For more invocation patterns, see [Running Agents](https://learn.microsoft.com/en-us/agent-framework/agents/running-agents).

### Function Tools

You can provide function tools to agents for enhanced capabilities:

Python

```python
import os
from typing import Annotated
from azure.identity.aio import DefaultAzureCredential
from agent_framework.azure import AzureOpenAIResponsesClient

def get_weather(location: Annotated[str, "The location to get the weather for."]) -> str:
    """Get the weather for a given location."""
    return f"The weather in {location} is sunny with a high of 25°C."

async with DefaultAzureCredential() as credential:
    agent = AzureOpenAIResponsesClient(
        credential=credential,
        project_endpoint=os.getenv("AZURE_AI_PROJECT_ENDPOINT"),
        deployment_name=os.getenv("AZURE_OPENAI_RESPONSES_DEPLOYMENT_NAME"),
    ).as_agent(
        instructions="You are a helpful weather assistant.",
        tools=get_weather,
    )
    response = await agent.run("What's the weather in Seattle?")
```

For tools and tool patterns, see [Tools overview](https://learn.microsoft.com/en-us/agent-framework/agents/tools/).

## Custom agents

For fully custom implementations (for example deterministic agents or API-backed agents), see [Custom Agents](https://learn.microsoft.com/en-us/agent-framework/agents/providers/custom). That page covers implementing [`SupportsAgentRun`](https://learn.microsoft.com/en-us/python/api/agent-framework-core/agent_framework.supportsagentrun?view=agent-framework-python-latest)​ or extending [`BaseAgent`](https://learn.microsoft.com/en-us/python/api/agent-framework-core/agent_framework.baseagent?view=agent-framework-python-latest)​, including streaming updates with [`AgentResponseUpdate`](https://learn.microsoft.com/en-us/python/api/agent-framework-core/agent_framework.agentresponseupdate?view=agent-framework-python-latest).

## Other agent types

Agent Framework also includes protocol-backed agents, such as:

|**Agent Type**|**Description**|
| --| -------------------------------------------------------------------------|
|[A2A](https://learn.microsoft.com/en-us/agent-framework/integrations/a2a)|A proxy agent that connects to and invokes remote A2A-compliant agents.|

## Next steps

[Running Agents](https://learn.microsoft.com/en-us/agent-framework/agents/running-agents)

Ask Learn is an AI assistant that can answer questions, clarify concepts, and define terms using trusted Microsoft documentation.

Please sign in to use Ask Learn.

[Sign in](https://learn.microsoft.com/en-us/agent-framework/agents/?pivots=programming-language-csharp#)
