# Code Interpreter

---

- Code Interpreter
- [https://learn.microsoft.com/en-us/agent-framework/agents/tools/code-interpreter?pivots=programming-language-csharp](https://learn.microsoft.com/en-us/agent-framework/agents/tools/code-interpreter?pivots=programming-language-csharp)
- Learn how to use the Code Interpreter tool with Agent Framework agents.
- 2026-03-04 19:14

---

- [Edit](https://github.com/MicrosoftDocs/semantic-kernel-docs/blob/main/agent-framework/agents/tools/code-interpreter.md)

  ---

  #### Share via

  ---

## Code Interpreter

Choose a programming languageC#Python

 Code Interpreter allows agents to write and execute code in a sandboxed environment. This is useful for data analysis, mathematical computations, file processing, and other tasks that benefit from code execution.

Note

Code Interpreter availability depends on the underlying agent provider. See [Providers Overview](https://learn.microsoft.com/en-us/agent-framework/agents/providers/) for provider-specific support.

The following example shows how to create an agent with the Code Interpreter tool and read the generated output:

### Create an agent with Code Interpreter

C#

```csharp
using System;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

// Requires: dotnet add package Microsoft.Agents.AI.OpenAI --prerelease
var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
    ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-4o-mini";

// Create an agent with the code interpreter hosted tool
AIAgent agent = new AzureOpenAIClient(new Uri(endpoint), new AzureCliCredential())
    .GetChatClient(deploymentName)
    .AsAIAgent(
        instructions: "You are a helpful assistant that can write and execute Python code.",
        tools: [new CodeInterpreterToolDefinition()]);

var response = await agent.RunAsync("Calculate the factorial of 100 using code.");
Console.WriteLine(response);
```

### Read code output

C#

```csharp
// Inspect code interpreter output from the response
foreach (var message in response.Messages)
{
    foreach (var content in message.Contents)
    {
        if (content is CodeInterpreterContent codeContent)
        {
            Console.WriteLine($"Code:\n{codeContent.Code}");
            Console.WriteLine($"Output:\n{codeContent.Output}");
        }
    }
}
```

The following example shows how to create an agent with the Code Interpreter tool:

Python

```python
# Copyright (c) Microsoft. All rights reserved.

import asyncio

from agent_framework import (
    Agent,
    Content,
)
from agent_framework.openai import OpenAIResponsesClient

"""
OpenAI Responses Client with Code Interpreter Example

This sample demonstrates using get_code_interpreter_tool() with OpenAI Responses Client
for Python code execution and mathematical problem solving.
"""


async def main() -> None:
    """Example showing how to use the code interpreter tool with OpenAI Responses."""
    print("=== OpenAI Responses Agent with Code Interpreter Example ===")

    client = OpenAIResponsesClient()
    agent = Agent(
        client=client,
        instructions="You are a helpful assistant that can write and execute Python code to solve problems.",
        tools=client.get_code_interpreter_tool(),
    )

    query = "Use code to get the factorial of 100?"
    print(f"User: {query}")
    result = await agent.run(query)
    print(f"Result: {result}\n")

    for message in result.messages:
        code_blocks = [c for c in message.contents if c.type == "code_interpreter_tool_call"]
        outputs = [c for c in message.contents if c.type == "code_interpreter_tool_result"]

        if code_blocks:
            code_inputs = code_blocks[0].inputs or []
            for content in code_inputs:
                if isinstance(content, Content) and content.type == "text":
                    print(f"Generated code:\n{content.text}")
                    break
        if outputs:
            print("Execution outputs:")
            for out in outputs[0].outputs or []:
                if isinstance(out, Content) and out.type == "text":
                    print(out.text)


if __name__ == "__main__":
    asyncio.run(main())
```

### Streaming code-interpreter deltas

When using Assistants clients with streaming, code-interpreter input can arrive in incremental deltas before the final tool result. You can inspect streaming updates and aggregate code fragments as they arrive:

Python

```python
for Python code execution and mathematical problem solving.
"""


def get_code_interpreter_chunk(chunk: AgentResponseUpdate) -> str | None:
    """Helper method to access code interpreter data."""
    if (
        isinstance(chunk.raw_representation, ChatResponseUpdate)
        and isinstance(chunk.raw_representation.raw_representation, RunStepDeltaEvent)
        and isinstance(chunk.raw_representation.raw_representation.delta, RunStepDelta)
        and isinstance(chunk.raw_representation.raw_representation.delta.step_details, ToolCallDeltaObject)
        and chunk.raw_representation.raw_representation.delta.step_details.tool_calls
    ):
        for tool_call in chunk.raw_representation.raw_representation.delta.step_details.tool_calls:
            if (
                isinstance(tool_call, CodeInterpreterToolCallDelta)
                and isinstance(tool_call.code_interpreter, CodeInterpreter)
        query = "What is current datetime?"
        print(f"User: {query}")
        print("Agent: ", end="", flush=True)
        generated_code = ""
        async for chunk in agent.run(query, stream=True):
            if chunk.text:
                print(chunk.text, end="", flush=True)
```

## Next steps

[File Search](https://learn.microsoft.com/en-us/agent-framework/agents/tools/file-search)

Was this page helpful?

Ask Learn is an AI assistant that can answer questions, clarify concepts, and define terms using trusted Microsoft documentation.

Please sign in to use Ask Learn.

[Sign in](https://learn.microsoft.com/en-us/agent-framework/agents/tools/code-interpreter?pivots=programming-language-csharp#)
