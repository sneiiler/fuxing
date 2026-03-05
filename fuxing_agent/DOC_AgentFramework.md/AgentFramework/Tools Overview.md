# Tools Overview

---

- Tools Overview
- [https://learn.microsoft.com/en-us/agent-framework/agents/tools/?pivots=programming-language-csharp](https://learn.microsoft.com/en-us/agent-framework/agents/tools/?pivots=programming-language-csharp)
- Overview of tool types available in Agent Framework and provider support matrix.
- 2026-03-04 19:14

---

- [Edit](https://github.com/MicrosoftDocs/semantic-kernel-docs/blob/main/agent-framework/agents/tools/index.md)

  ---

  #### Share via

  ---

## Tools Overview

Choose a programming languageC#Python

 Agent Framework supports many different types of tools that extend agent capabilities. Tools allow agents to interact with external systems, execute code, search data, and more.

## Tool Types

|**Tool Type**|**Description**|
| --| -------------------------------------------------------|
|[Function Tools](https://learn.microsoft.com/en-us/agent-framework/agents/tools/function-tools)|Custom code that agents can call during conversations|
|[Tool Approval](https://learn.microsoft.com/en-us/agent-framework/agents/tools/tool-approval)|Human-in-the-loop approval for tool invocations|
|[Code Interpreter](https://learn.microsoft.com/en-us/agent-framework/agents/tools/code-interpreter)|Execute code in a sandboxed environment|
|[File Search](https://learn.microsoft.com/en-us/agent-framework/agents/tools/file-search)|Search through uploaded files|
|[Web Search](https://learn.microsoft.com/en-us/agent-framework/agents/tools/web-search)|Search the web for information|
|[Hosted MCP Tools](https://learn.microsoft.com/en-us/agent-framework/agents/tools/hosted-mcp-tools)|MCP tools hosted by Microsoft Foundry|
|[Local MCP Tools](https://learn.microsoft.com/en-us/agent-framework/agents/tools/local-mcp-tools)|MCP tools running locally or on custom servers|

## Provider Support Matrix

The OpenAI and Azure OpenAI providers each offer multiple client types with different tool capabilities. Azure OpenAI clients mirror their OpenAI equivalents.

|**Tool Type**|**[Chat Completion](https://learn.microsoft.com/en-us/agent-framework/agents/providers/azure-openai)**|**[Responses](https://learn.microsoft.com/en-us/agent-framework/agents/providers/azure-openai)**|**[Assistants](https://learn.microsoft.com/en-us/agent-framework/agents/providers/azure-openai)**|**[Foundry](https://learn.microsoft.com/en-us/agent-framework/agents/providers/azure-ai-foundry)**|**[Anthropic](https://learn.microsoft.com/en-us/agent-framework/agents/providers/anthropic)**|**[Ollama](https://learn.microsoft.com/en-us/agent-framework/agents/providers/ollama)**|**[GitHub Copilot](https://learn.microsoft.com/en-us/agent-framework/agents/providers/github-copilot)**|**[Copilot Studio](https://learn.microsoft.com/en-us/agent-framework/agents/providers/copilot-studio)**|
| --| ----| ----| ----| ----| ----| ----| ----| ----|
|[Function Tools](https://learn.microsoft.com/en-us/agent-framework/agents/tools/function-tools)|✅|✅|✅|✅|✅|✅|✅|✅|
|[Tool Approval](https://learn.microsoft.com/en-us/agent-framework/agents/tools/tool-approval)|❌|✅|❌|✅|❌|❌|❌|❌|
|[Code Interpreter](https://learn.microsoft.com/en-us/agent-framework/agents/tools/code-interpreter)|❌|✅|✅|✅|❌|❌|❌|❌|
|[File Search](https://learn.microsoft.com/en-us/agent-framework/agents/tools/file-search)|❌|✅|✅|✅|❌|❌|❌|❌|
|[Web Search](https://learn.microsoft.com/en-us/agent-framework/agents/tools/web-search)|✅|✅|❌|❌|❌|❌|❌|❌|
|[Hosted MCP Tools](https://learn.microsoft.com/en-us/agent-framework/agents/tools/hosted-mcp-tools)|❌|✅|❌|✅|✅|❌|❌|❌|
|[Local MCP Tools](https://learn.microsoft.com/en-us/agent-framework/agents/tools/local-mcp-tools)|✅|✅|✅|✅|✅|✅|✅|❌|

Note

The ​**Chat Completion**​, ​**Responses**​, and **Assistants** columns apply to both OpenAI and Azure OpenAI — the Azure variants mirror the same tool support as their OpenAI counterparts.

## Provider Support Matrix

The OpenAI and Azure OpenAI providers each offer multiple client types with different tool capabilities. Azure OpenAI clients mirror their OpenAI equivalents.

|**Tool Type**|**[Chat Completion](https://learn.microsoft.com/en-us/agent-framework/agents/providers/azure-openai)**|**[Responses](https://learn.microsoft.com/en-us/agent-framework/agents/providers/azure-openai)**|**[Assistants](https://learn.microsoft.com/en-us/agent-framework/agents/providers/azure-openai)**|**[Foundry](https://learn.microsoft.com/en-us/agent-framework/agents/providers/azure-ai-foundry)**|**[Anthropic](https://learn.microsoft.com/en-us/agent-framework/agents/providers/anthropic)**|**[Claude Agent](https://learn.microsoft.com/en-us/agent-framework/agents/providers/anthropic)**|**[Ollama](https://learn.microsoft.com/en-us/agent-framework/agents/providers/ollama)**|**[GitHub Copilot](https://learn.microsoft.com/en-us/agent-framework/agents/providers/github-copilot)**|
| --| ----| ----| ----| ----| ----| ----| ----| ----|
|[Function Tools](https://learn.microsoft.com/en-us/agent-framework/agents/tools/function-tools)|✅|✅|✅|✅|✅|✅|✅|✅|
|[Tool Approval](https://learn.microsoft.com/en-us/agent-framework/agents/tools/tool-approval)|❌|✅|❌|✅|❌|❌|❌|❌|
|[Code Interpreter](https://learn.microsoft.com/en-us/agent-framework/agents/tools/code-interpreter)|❌|✅|✅|✅|✅|❌|❌|❌|
|[File Search](https://learn.microsoft.com/en-us/agent-framework/agents/tools/file-search)|❌|✅|✅|✅|❌|❌|❌|❌|
|[Web Search](https://learn.microsoft.com/en-us/agent-framework/agents/tools/web-search)|✅|✅|❌|✅|✅|❌|❌|❌|
|[Image Generation](https://learn.microsoft.com/en-us/agent-framework/agents/tools/code-interpreter)|❌|❌|❌|✅|❌|❌|❌|❌|
|[Hosted MCP Tools](https://learn.microsoft.com/en-us/agent-framework/agents/tools/hosted-mcp-tools)|❌|✅|❌|✅|✅|❌|❌|❌|
|[Local MCP Tools](https://learn.microsoft.com/en-us/agent-framework/agents/tools/local-mcp-tools)|✅|✅|✅|✅|✅|✅|✅|✅|

Note

The ​**Chat Completion**​, ​**Responses**​, and **Assistants** columns apply to both OpenAI and Azure OpenAI — the Azure variants mirror the same tool support as their OpenAI counterparts. Local MCP Tools work with any provider that supports function tools.

## Using an Agent as a Function Tool

You can use an agent as a function tool for another agent, enabling agent composition and more advanced workflows. The inner agent is converted to a function tool and provided to the outer agent, which can then call it as needed.

Call `.AsAIFunction()`​ on an `AIAgent` to convert it to a function tool that can be provided to another agent:

C#

```csharp
// Create the inner agent with its own tools
AIAgent weatherAgent = new AzureOpenAIClient(
    new Uri("https://<myresource>.openai.azure.com"),
    new AzureCliCredential())
     .GetChatClient("gpt-4o-mini")
     .AsAIAgent(
        instructions: "You answer questions about the weather.",
        name: "WeatherAgent",
        description: "An agent that answers questions about the weather.",
        tools: [AIFunctionFactory.Create(GetWeather)]);

// Create the main agent and provide the inner agent as a function tool
AIAgent agent = new AzureOpenAIClient(
    new Uri("https://<myresource>.openai.azure.com"),
    new AzureCliCredential())
     .GetChatClient("gpt-4o-mini")
     .AsAIAgent(instructions: "You are a helpful assistant.", tools: [weatherAgent.AsAIFunction()]);

// The main agent can now call the weather agent as a tool
Console.WriteLine(await agent.RunAsync("What is the weather like in Amsterdam?"));
```

Call `.as_tool()` on an agent to convert it to a function tool that can be provided to another agent:

Python

```python
from agent_framework.azure import AzureOpenAIChatClient
from azure.identity import AzureCliCredential

# Create the inner agent with its own tools
weather_agent = AzureOpenAIChatClient(credential=AzureCliCredential()).as_agent(
    name="WeatherAgent",
    description="An agent that answers questions about the weather.",
    instructions="You answer questions about the weather.",
    tools=get_weather
)

# Create the main agent and provide the inner agent as a function tool
main_agent = AzureOpenAIChatClient(credential=AzureCliCredential()).as_agent(
    instructions="You are a helpful assistant.",
    tools=weather_agent.as_tool()
)

# The main agent can now call the weather agent as a tool
result = await main_agent.run("What is the weather like in Amsterdam?")
print(result.text)
```

You can also customize the tool name, description, and argument name:

Python

```python
weather_tool = weather_agent.as_tool(
    name="WeatherLookup",
    description="Look up weather information for any location",
    arg_name="query",
    arg_description="The weather query or location"
)
```

## Next steps

[Function Tools](https://learn.microsoft.com/en-us/agent-framework/agents/tools/function-tools)

## In this article

1. [Tool Types](https://learn.microsoft.com/en-us/agent-framework/agents/tools/?pivots=programming-language-csharp#tool-types)
2. [Provider Support Matrix](https://learn.microsoft.com/en-us/agent-framework/agents/tools/?pivots=programming-language-csharp#provider-support-matrix)
3. [Using an Agent as a Function Tool](https://learn.microsoft.com/en-us/agent-framework/agents/tools/?pivots=programming-language-csharp#using-an-agent-as-a-function-tool)
4. [Next steps](https://learn.microsoft.com/en-us/agent-framework/agents/tools/?pivots=programming-language-csharp#next-steps)

Was this page helpful?

Ask Learn is an AI assistant that can answer questions, clarify concepts, and define terms using trusted Microsoft documentation.

Please sign in to use Ask Learn.

[Sign in](https://learn.microsoft.com/en-us/agent-framework/agents/tools/?pivots=programming-language-csharp#)
