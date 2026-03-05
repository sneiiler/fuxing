# Exception Handling

---

- Exception Handling
- [https://learn.microsoft.com/en-us/agent-framework/agents/middleware/exception-handling?pivots=programming-language-csharp](https://learn.microsoft.com/en-us/agent-framework/agents/middleware/exception-handling?pivots=programming-language-csharp)
- Learn how to handle exceptions in middleware.
- 2026-03-04 19:16

---

- [Edit](https://github.com/MicrosoftDocs/semantic-kernel-docs/blob/main/agent-framework/agents/middleware/exception-handling.md)

  ---

  #### Share via

  ---

## Exception Handling

Choose a programming languageC#Python

 Middleware provides a natural place to implement error handling, retry logic, and graceful degradation for agent interactions.

In C#, you can wrap agent execution in try-catch blocks within middleware to handle exceptions:

C#

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

// Middleware that catches exceptions and provides graceful fallback responses
async Task<AgentResponse> ExceptionHandlingMiddleware(
    IEnumerable<ChatMessage> messages,
    AgentSession? session,
    AgentRunOptions? options,
    AIAgent innerAgent,
    CancellationToken cancellationToken)
{
    try
    {
        Console.WriteLine("[ExceptionHandler] Executing agent run...");
        return await innerAgent.RunAsync(messages, session, options, cancellationToken);
    }
    catch (TimeoutException ex)
    {
        Console.WriteLine($"[ExceptionHandler] Caught timeout: {ex.Message}");
        return new AgentResponse([new ChatMessage(ChatRole.Assistant,
            "Sorry, the request timed out. Please try again later.")]);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ExceptionHandler] Caught error: {ex.Message}");
        return new AgentResponse([new ChatMessage(ChatRole.Assistant,
            "An error occurred while processing your request.")]);
    }
}

AIAgent agent = new AzureOpenAIClient(
    new Uri("https://<myresource>.openai.azure.com"),
    new AzureCliCredential())
        .GetChatClient("gpt-4o-mini")
        .AsAIAgent(instructions: "You are a helpful assistant.");

var safeAgent = agent
    .AsBuilder()
        .Use(runFunc: ExceptionHandlingMiddleware, runStreamingFunc: null)
    .Build();

Console.WriteLine(await safeAgent.RunAsync("Get user statistics"));
```

### Exception handling middleware

This example demonstrates how to catch and handle exceptions within middleware:

Python

```python
# Copyright (c) Microsoft. All rights reserved.

import asyncio
from collections.abc import Awaitable, Callable
from typing import Annotated

from agent_framework import FunctionInvocationContext, tool
from agent_framework.azure import AzureAIAgentClient
from azure.identity.aio import AzureCliCredential
from pydantic import Field

"""
Exception Handling with MiddlewareTypes

This sample demonstrates how to use middleware for centralized exception handling in function calls.
The example shows:

- How to catch exceptions thrown by functions and provide graceful error responses
- Overriding function results when errors occur to provide user-friendly messages
- Using middleware to implement retry logic, fallback mechanisms, or error reporting

The middleware catches TimeoutError from an unstable data service and replaces it with
a helpful message for the user, preventing raw exceptions from reaching the end user.
"""


# NOTE: approval_mode="never_require" is for sample brevity. Use "always_require" in production; see samples/02-agents/tools/function_tool_with_approval.py and samples/02-agents/tools/function_tool_with_approval_and_sessions.py.
@tool(approval_mode="never_require")
def unstable_data_service(
    query: Annotated[str, Field(description="The data query to execute.")],
) -> str:
    """A simulated data service that sometimes throws exceptions."""
    # Simulate failure
    raise TimeoutError("Data service request timed out")


async def exception_handling_middleware(
    context: FunctionInvocationContext, call_next: Callable[[], Awaitable[None]]
) -> None:
    function_name = context.function.name

    try:
        print(f"[ExceptionHandlingMiddleware] Executing function: {function_name}")
        await call_next()
        print(f"[ExceptionHandlingMiddleware] Function {function_name} completed successfully.")
    except TimeoutError as e:
        print(f"[ExceptionHandlingMiddleware] Caught TimeoutError: {e}")
        # Override function result to provide custom message in response.
        context.result = (
            "Request Timeout: The data service is taking longer than expected to respond.",
            "Respond with message - 'Sorry for the inconvenience, please try again later.'",
        )


async def main() -> None:
    """Example demonstrating exception handling with middleware."""
    print("=== Exception Handling MiddlewareTypes Example ===")

    # For authentication, run `az login` command in terminal or replace AzureCliCredential with preferred
    # authentication option.
    async with (
        AzureCliCredential() as credential,
        AzureAIAgentClient(credential=credential).as_agent(
            name="DataAgent",
            instructions="You are a helpful data assistant. Use the data service tool to fetch information for users.",
            tools=unstable_data_service,
            middleware=[exception_handling_middleware],
        ) as agent,
    ):
        query = "Get user statistics"
        print(f"User: {query}")
        result = await agent.run(query)
        print(f"Agent: {result}")


if __name__ == "__main__":
    asyncio.run(main())
```

### Example: Unstable tool

Here's a tool that may raise exceptions, which the middleware above can handle:

Python

```python
# Copyright (c) Microsoft. All rights reserved.

import asyncio
from collections.abc import Awaitable, Callable
from typing import Annotated

from agent_framework import FunctionInvocationContext, tool
from agent_framework.azure import AzureAIAgentClient
from azure.identity.aio import AzureCliCredential
from pydantic import Field

"""
Exception Handling with MiddlewareTypes

This sample demonstrates how to use middleware for centralized exception handling in function calls.
The example shows:

- How to catch exceptions thrown by functions and provide graceful error responses
- Overriding function results when errors occur to provide user-friendly messages
- Using middleware to implement retry logic, fallback mechanisms, or error reporting

The middleware catches TimeoutError from an unstable data service and replaces it with
a helpful message for the user, preventing raw exceptions from reaching the end user.
"""


# NOTE: approval_mode="never_require" is for sample brevity. Use "always_require" in production; see samples/02-agents/tools/function_tool_with_approval.py and samples/02-agents/tools/function_tool_with_approval_and_sessions.py.
@tool(approval_mode="never_require")
def unstable_data_service(
    query: Annotated[str, Field(description="The data query to execute.")],
) -> str:
    """A simulated data service that sometimes throws exceptions."""
    # Simulate failure
    raise TimeoutError("Data service request timed out")


async def exception_handling_middleware(
    context: FunctionInvocationContext, call_next: Callable[[], Awaitable[None]]
) -> None:
    function_name = context.function.name

    try:
        print(f"[ExceptionHandlingMiddleware] Executing function: {function_name}")
        await call_next()
        print(f"[ExceptionHandlingMiddleware] Function {function_name} completed successfully.")
    except TimeoutError as e:
        print(f"[ExceptionHandlingMiddleware] Caught TimeoutError: {e}")
        # Override function result to provide custom message in response.
        context.result = (
            "Request Timeout: The data service is taking longer than expected to respond.",
            "Respond with message - 'Sorry for the inconvenience, please try again later.'",
        )


async def main() -> None:
    """Example demonstrating exception handling with middleware."""
    print("=== Exception Handling MiddlewareTypes Example ===")

    # For authentication, run `az login` command in terminal or replace AzureCliCredential with preferred
    # authentication option.
    async with (
        AzureCliCredential() as credential,
        AzureAIAgentClient(credential=credential).as_agent(
            name="DataAgent",
            instructions="You are a helpful data assistant. Use the data service tool to fetch information for users.",
            tools=unstable_data_service,
            middleware=[exception_handling_middleware],
        ) as agent,
    ):
        query = "Get user statistics"
        print(f"User: {query}")
        result = await agent.run(query)
        print(f"Agent: {result}")


if __name__ == "__main__":
    asyncio.run(main())
```

## Next steps

[Shared State](https://learn.microsoft.com/en-us/agent-framework/agents/middleware/shared-state)

Was this page helpful?

Ask Learn is an AI assistant that can answer questions, clarify concepts, and define terms using trusted Microsoft documentation.

Please sign in to use Ask Learn.

[Sign in](https://learn.microsoft.com/en-us/agent-framework/agents/middleware/exception-handling?pivots=programming-language-csharp#)
