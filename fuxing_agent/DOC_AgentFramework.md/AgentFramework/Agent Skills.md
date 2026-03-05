# Agent Skills

---

- Agent Skills
- [https://learn.microsoft.com/en-us/agent-framework/agents/skills?pivots=programming-language-csharp](https://learn.microsoft.com/en-us/agent-framework/agents/skills?pivots=programming-language-csharp)
- Learn how to extend agent capabilities with Agent Skills — portable packages of instructions, scripts, and resources that agents discover and load on demand.
- 2026-03-04 19:13

---

- [Edit](https://github.com/MicrosoftDocs/semantic-kernel-docs/blob/main/agent-framework/agents/skills.md)

  ---

  #### Share via

  ---

## Agent Skills

Choose a programming languageC#Python

 [Agent Skills](https://agentskills.io/) are portable packages of instructions, scripts, and resources that give agents specialized capabilities and domain expertise. Skills follow an open specification and implement a progressive disclosure pattern so agents load only the context they need, when they need it.

Use Agent Skills when you want to:

- **Package domain expertise** — Capture specialized knowledge (expense policies, legal workflows, data analysis pipelines) as reusable, portable packages.
- **Extend agent capabilities** — Give agents new abilities without changing their core instructions.
- **Ensure consistency** — Turn multi-step tasks into repeatable, auditable workflows.
- **Enable interoperability** — Reuse the same skill across different Agent Skills-compatible products.

## Skill structure

A skill is a directory containing a `SKILL.md` file with optional subdirectories for resources:

```
expense-report/
├── SKILL.md                          # Required — frontmatter + instructions
├── scripts/
│   └── validate.py                   # Executable code agents can run
├── references/
│   └── POLICY_FAQ.md                 # Reference documents loaded on demand
└── assets/
    └── expense-report-template.md    # Templates and static resources
```

### SKILL.md format

The `SKILL.md` file must contain YAML frontmatter followed by markdown content:

YAML

```yaml
---
name: expense-report
description: File and validate employee expense reports according to company policy. Use when asked about expense submissions, reimbursement rules, or spending limits.
license: Apache-2.0
compatibility: Requires python3
metadata:
  author: contoso-finance
  version: "2.1"
---
```

|**Field**|**Required**|**Description**|
| ------| -----| ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
|​`name`|Yes|Max 64 characters. Lowercase letters, numbers, and hyphens only. Must not start or end with a hyphen or contain consecutive hyphens. Must match the parent directory name.|
|​`description`|Yes|What the skill does and when to use it. Max 1024 characters. Should include keywords that help agents identify relevant tasks.|
|​`license`|No|License name or reference to a bundled license file.|
|​`compatibility`|No|Max 500 characters. Indicates environment requirements (intended product, system packages, network access, etc.).|
|​`metadata`|No|Arbitrary key-value mapping for additional metadata.|
|​`allowed-tools`|No|Space-delimited list of pre-approved tools the skill may use. Experimental — support may vary between agent implementations.|

The markdown body after the frontmatter contains the skill instructions — step-by-step guidance, examples of inputs and outputs, common edge cases, or any content that helps the agent perform the task. Keep `SKILL.md` under 500 lines and move detailed reference material to separate files.

## Progressive disclosure

Agent Skills use a three-stage progressive disclosure pattern to minimize context usage:

1. **Advertise** (\~100 tokens per skill) — Skill names and descriptions are injected into the system prompt at the start of each run, so the agent knows what skills are available.
2. **Load** (\< 5000 tokens recommended) — When a task matches a skill's domain, the agent calls the `load_skill` tool to retrieve the full SKILL.md body with detailed instructions.
3. **Read resources** (as needed) — The agent calls the `read_skill_resource` tool to fetch supplementary files (references, templates, assets) only when required.

This pattern keeps the agent's context window lean while giving it access to deep domain knowledge on demand.

## Using FileAgentSkillsProvider

The `FileAgentSkillsProvider`​ discovers skills from filesystem directories and makes them available to agents as a context provider. It searches configured paths recursively (up to two levels deep) for `SKILL.md`​ files, validates their format and resources, and exposes two tools to the agent: `load_skill`​ and `read_skill_resource`.

Note

Script execution is not yet supported by `FileAgentSkillsProvider` and will be added in a future release.

### Basic setup

Create a `FileAgentSkillsProvider` pointing to a directory containing your skills, and add it to the agent's context providers:

C#

```csharp
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;

// Discover skills from the 'skills' directory
var skillsProvider = new FileAgentSkillsProvider(
    skillPath: Path.Combine(AppContext.BaseDirectory, "skills"));

// Create an agent with the skills provider
AIAgent agent = new AzureOpenAIClient(
    new Uri(endpoint), new DefaultAzureCredential())
    .GetResponsesClient(deploymentName)
    .AsAIAgent(new ChatClientAgentOptions
    {
        Name = "SkillsAgent",
        ChatOptions = new()
        {
            Instructions = "You are a helpful assistant.",
        },
        AIContextProviders = [skillsProvider],
    });
```

### Invoking the agent

Once configured, the agent automatically discovers available skills and uses them when a task matches:

C#

```csharp
// The agent loads the expense-report skill and reads the FAQ resource
AgentResponse response = await agent.RunAsync(
    "Are tips reimbursable? I left a 25% tip on a taxi ride.");
Console.WriteLine(response.Text);
```

### Basic setup

Create a `FileAgentSkillsProvider` pointing to a directory containing your skills, and add it to the agent's context providers:

Python

```python
from pathlib import Path
from agent_framework import FileAgentSkillsProvider
from agent_framework.azure import AzureOpenAIChatClient
from azure.identity.aio import AzureCliCredential

# Discover skills from the 'skills' directory
skills_provider = FileAgentSkillsProvider(
    skill_paths=Path(__file__).parent / "skills"
)

# Create an agent with the skills provider
agent = AzureOpenAIChatClient(credential=AzureCliCredential()).as_agent(
    name="SkillsAgent",
    instructions="You are a helpful assistant.",
    context_providers=[skills_provider],
)
```

### Invoking the agent

Once configured, the agent automatically discovers available skills and uses them when a task matches:

Python

```python
# The agent loads the expense-report skill and reads the FAQ resource
response = await agent.run(
    "Are tips reimbursable? I left a 25% tip on a taxi ride."
)
print(response.text)
```

## Multiple skill directories

You can search multiple directories by passing a list of paths:

C#

```csharp
var skillsProvider = new FileAgentSkillsProvider(
    skillPaths: [
        Path.Combine(AppContext.BaseDirectory, "company-skills"),
        Path.Combine(AppContext.BaseDirectory, "team-skills"),
    ]);
```

Python

```python
skills_provider = FileAgentSkillsProvider(
    skill_paths=[
        Path(__file__).parent / "company-skills",
        Path(__file__).parent / "team-skills",
    ]
)
```

Each path can point to an individual skill folder (containing a `SKILL.md`) or a parent folder with skill subdirectories. The provider searches up to two levels deep.

## Custom system prompt

By default, `FileAgentSkillsProvider`​ injects a system prompt that lists available skills and instructs the agent to use `load_skill`​ and `read_skill_resource`. You can customize this prompt:

C#

```csharp
var skillsProvider = new FileAgentSkillsProvider(
    skillPath: Path.Combine(AppContext.BaseDirectory, "skills"),
    options: new FileAgentSkillsProviderOptions
    {
        SkillsInstructionPrompt = """
            You have skills available. Here they are:
            {0}
            Use the `load_skill` function to get skill instructions.
            Use the `read_skill_resource` function to read skill files.
            """
    });
```

Note

The custom template must contain a `{0}`​ placeholder where the skill list is inserted. Literal braces must be escaped as `{{`​ and `}}`.

Python

```python
skills_provider = FileAgentSkillsProvider(
    skill_paths=Path(__file__).parent / "skills",
    skills_instruction_prompt=(
        "You have skills available. Here they are:\n{0}\n"
        "Use the `load_skill` function to get skill instructions.\n"
        "Use the `read_skill_resource` function to read skill files."
    ),
)
```

Note

The custom template must contain a `{0}` placeholder where the skill list is inserted.

## Security best practices

Agent Skills should be treated like any third-party code you bring into your project. Because skill instructions are injected into the agent's context — and skills can include scripts — applying the same level of review and governance you would to an open-source dependency is essential.

- **Review before use** — Read all skill content (`SKILL.md`, scripts, and resources) before deploying. Verify that a script's actual behavior matches its stated intent. Check for adversarial instructions that attempt to bypass safety guidelines, exfiltrate data, or modify agent configuration files.
- **Source trust** — Only install skills from trusted authors or vetted internal contributors. Prefer skills with clear provenance, version control, and active maintenance. Watch for typosquatted skill names that mimic popular packages.
- **Sandboxing** — Run skills that include executable scripts in isolated environments. Limit filesystem, network, and system-level access to only what the skill requires. Require explicit user confirmation before executing potentially sensitive operations.
- **Audit and logging** — Record which skills are loaded, which resources are read, and which scripts are executed. This gives you an audit trail to trace agent behavior back to specific skill content if something goes wrong.

## When to use skills vs. workflows

Agent Skills and [Agent Framework Workflows](https://learn.microsoft.com/en-us/agent-framework/workflows/) both extend what agents can do, but they work in fundamentally different ways. Choose the approach that best matches your requirements:

- **Control** — With a skill, the AI decides how to execute the instructions. This is ideal when you want the agent to be creative or adaptive. With a workflow, you explicitly define the execution path. Use workflows when you need deterministic, predictable behavior.
- **Resilience** — A skill runs within a single agent turn. If something fails, the entire operation must be retried. Workflows support [checkpointing](https://learn.microsoft.com/en-us/agent-framework/workflows/checkpoints), so they can resume from the last successful step after a failure. Choose workflows when the cost of re-executing the entire process is high.
- **Side effects** — Skills are suitable when operations are idempotent or low-risk. Prefer workflows when steps produce side effects (sending emails, charging payments) that should not be repeated on retry.
- **Complexity** — Skills are best for focused, single-domain tasks that one agent can handle. Workflows are better suited for multi-step business processes that coordinate multiple agents, human approvals, or external system integrations.

Tip

As a rule of thumb: if you want the AI to figure out *how* to accomplish a task, use a skill. If you need to guarantee *what* steps execute and in what order, use a workflow.

## Next steps

[Context Providers](https://learn.microsoft.com/en-us/agent-framework/agents/conversations/context-providers)

### Related content

- [Agent Skills specification](https://agentskills.io/)
- [Context Providers](https://learn.microsoft.com/en-us/agent-framework/agents/conversations/context-providers)
- [Tools Overview](https://learn.microsoft.com/en-us/agent-framework/agents/tools/)

## In this article

1. [Skill structure](https://learn.microsoft.com/en-us/agent-framework/agents/skills?pivots=programming-language-csharp#skill-structure)
2. [Progressive disclosure](https://learn.microsoft.com/en-us/agent-framework/agents/skills?pivots=programming-language-csharp#progressive-disclosure)
3. [Using FileAgentSkillsProvider](https://learn.microsoft.com/en-us/agent-framework/agents/skills?pivots=programming-language-csharp#using-fileagentskillsprovider)
4. [Multiple skill directories](https://learn.microsoft.com/en-us/agent-framework/agents/skills?pivots=programming-language-csharp#multiple-skill-directories)
5. [Custom system prompt](https://learn.microsoft.com/en-us/agent-framework/agents/skills?pivots=programming-language-csharp#custom-system-prompt)
6. [Security best practices](https://learn.microsoft.com/en-us/agent-framework/agents/skills?pivots=programming-language-csharp#security-best-practices)
7. [When to use skills vs. workflows](https://learn.microsoft.com/en-us/agent-framework/agents/skills?pivots=programming-language-csharp#when-to-use-skills-vs-workflows)
8. [Next steps](https://learn.microsoft.com/en-us/agent-framework/agents/skills?pivots=programming-language-csharp#next-steps)

Was this page helpful?

Ask Learn is an AI assistant that can answer questions, clarify concepts, and define terms using trusted Microsoft documentation.

Please sign in to use Ask Learn.

[Sign in](https://learn.microsoft.com/en-us/agent-framework/agents/skills?pivots=programming-language-csharp#)
