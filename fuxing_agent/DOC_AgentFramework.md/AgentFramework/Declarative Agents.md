# Declarative Agents

---

- Declarative Agents
- [https://learn.microsoft.com/en-us/agent-framework/agents/declarative?pivots=programming-language-csharp](https://learn.microsoft.com/en-us/agent-framework/agents/declarative?pivots=programming-language-csharp)
- Learn how to define agents declaratively using configuration files in Agent Framework.
- 2026-03-04 19:14

---

- [Edit](https://github.com/MicrosoftDocs/semantic-kernel-docs/blob/main/agent-framework/agents/declarative.md)

  ---

  #### Share via

  ---

## Declarative Agents

Choose a programming languageC#Python

 Declarative agents allow you to define agent configuration using YAML or JSON files instead of writing programmatic code. This approach makes agents easier to define, modify, and share across teams.

The following example shows how to create a declarative agent from a YAML configuration:

C#

```csharp
using System;
using System.IO;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

// Load agent configuration from a YAML file
var yamlContent = File.ReadAllText("agent-config.yaml");

// Create the agent from the YAML definition
AIAgent agent = AgentFactory.CreateFromYaml(
    yamlContent,
    new AzureOpenAIClient(
        new Uri("https://<myresource>.openai.azure.com"),
        new AzureCliCredential()));

// Run the declarative agent
Console.WriteLine(await agent.RunAsync("Why is the sky blue?"));
```

### Define an agent inline with YAML

You can define the full YAML specification as a string directly in your code:

Python

```python
import asyncio

from agent_framework.declarative import AgentFactory
from azure.identity.aio import AzureCliCredential


async def main():
    """Create an agent from an inline YAML definition and run it."""
    yaml_definition = """kind: Prompt
name: DiagnosticAgent
displayName: Diagnostic Assistant
instructions: Specialized diagnostic and issue detection agent for systems with critical error protocol and automatic handoff capabilities
description: An agent that performs diagnostics on systems and can escalate issues when critical errors are detected.

model:
  id: =Env.AZURE_OPENAI_MODEL
  connection:
    kind: remote
    endpoint: =Env.AZURE_AI_PROJECT_ENDPOINT
"""
    async with (
        AzureCliCredential() as credential,
        AgentFactory(client_kwargs={"credential": credential}).create_agent_from_yaml(yaml_definition) as agent,
    ):
        response = await agent.run("What can you do for me?")
        print("Agent response:", response.text)


if __name__ == "__main__":
    asyncio.run(main())
```

### Load an agent from a YAML file

You can also load the YAML definition from a file:

Python

```python
import asyncio
from pathlib import Path

from agent_framework.declarative import AgentFactory
from azure.identity import AzureCliCredential


async def main():
    """Create an agent from a declarative YAML file and run it."""
    yaml_path = Path(__file__).parent / "agent-config.yaml"

    with yaml_path.open("r") as f:
        yaml_str = f.read()

    agent = AgentFactory(client_kwargs={"credential": AzureCliCredential()}).create_agent_from_yaml(yaml_str)
    response = await agent.run("Why is the sky blue?")
    print("Agent response:", response.text)


if __name__ == "__main__":
    asyncio.run(main())
```

## Next steps

[Observability](https://learn.microsoft.com/en-us/agent-framework/agents/observability)

Was this page helpful?

Ask Learn is an AI assistant that can answer questions, clarify concepts, and define terms using trusted Microsoft documentation.

Please sign in to use Ask Learn.

[Sign in](https://learn.microsoft.com/en-us/agent-framework/agents/declarative?pivots=programming-language-csharp#)
