# NanoAgent Documentation

NanoAgent is a AI coding agent for people who want an assistant that can work directly inside a repository while still respecting local permissions, approval prompts, and workspace policy. It runs as a desktop app and as the `nanoai` terminal command.

This guide is the product handbook for setup, daily use, safety controls, and advanced workspace customization.

## Contents

- [Install](#install)
- [First Run](#first-run)
- [Desktop Workflow](#desktop-workflow)
- [Terminal Workflow](#terminal-workflow)
- [Providers and Models](#providers-and-models)
- [Profiles and Subagents](#profiles-and-subagents)
- [Permissions and Sandboxing](#permissions-and-sandboxing)
- [Workspace Files](#workspace-files)
- [Skills and Custom Agents](#skills-and-custom-agents)
- [MCP Servers](#mcp-servers)
- [Memory, Audit, and Hooks](#memory-audit-and-hooks)
- [Privacy and Local Data](#privacy-and-local-data)
- [Troubleshooting](#troubleshooting)
- [Build From Source](#build-from-source)

## Install

### Desktop App

Download the latest release for your platform:

| Platform | Release asset |
| --- | --- |
| Windows x64 | `NanoAgent.Desktop-win-x64-setup.exe` |
| Linux x64 | `NanoAgent.Desktop-linux-x64.zip` |
| Linux arm64 | `NanoAgent.Desktop-linux-arm64.zip` |
| macOS x64 | `NanoAgent.Desktop-osx-x64.zip` |
| macOS arm64 | `NanoAgent.Desktop-osx-arm64.zip` |

Release downloads are published at:

```text
https://github.com/rizwan3d/NanoAgent/releases/latest
```

### CLI

macOS / Linux:

```bash
curl -fsSL https://raw.githubusercontent.com/rizwan3d/NanoAgent/master/scripts/install.sh | bash
```

Windows PowerShell:

```powershell
irm https://raw.githubusercontent.com/rizwan3d/NanoAgent/master/scripts/install.ps1 | iex
```

Restart your terminal if `nanoai` is not found immediately after installation.

## First Run

Start NanoAgent:

```bash
nanoai
```

NanoAgent will guide you through provider setup:

1. Choose a provider.
2. Enter an API key, sign in with ChatGPT Plus/Pro, or enter a custom compatible base URL.
3. Let NanoAgent discover available models.
4. Open a desktop workspace or use the current terminal directory.
5. Start a new section or resume an existing one.

In terminal runs, `--provider-auth-key <key>` can supply the provider API key when onboarding asks for it.

If NanoAgent detects incomplete local provider setup, it asks whether to reconfigure. Choose reconfigure when a previous setup was interrupted or credentials were not saved. If provider validation fails after setup, NanoAgent offers to run onboarding again.

Use `/onboard` in an active desktop or terminal session to re-run provider setup later. The command supports every provider listed below and switches the active session to the validated provider and selected default model.

When a newer NanoAgent release is available, startup can ask whether to update now or skip. One-shot prompt runs do not show the startup update prompt.

### Provider Options

| Provider | Credential method | Notes |
| --- | --- | --- |
| OpenAI | API key | Uses the OpenAI API. |
| OpenAI ChatGPT Plus/Pro | Browser sign-in | Uses OAuth with local callback port `1455`. |
| OpenRouter | API key | Uses the OpenRouter OpenAI-compatible endpoint. |
| Google AI Studio | API key | Uses the OpenAI-compatible Gemini endpoint. |
| Anthropic | API key | Uses the Anthropic OpenAI-compatible endpoint. |
| OpenAI-compatible provider | Base URL and API key | Use for local or third-party compatible APIs. |

Secrets are stored through platform credential storage where supported. ChatGPT Plus/Pro sign-in stores refreshable account credentials locally.

## Desktop Workflow

The desktop app is built around workspaces, sections, chat, and controls.

### Workspaces

Open a local folder to make it the active workspace. NanoAgent remembers recent workspaces so you can return later.

### Sections

A section is a saved local conversation thread tied to a workspace. Sections preserve conversation history, active model, profile, thinking mode, plan state, and session state when available.

Use sections for separate tasks:

- One section for a feature.
- One section for a bug fix.
- One section for a review.
- One section for planning.

### Conversation

Type a prompt and let NanoAgent inspect, plan, edit, run commands, or ask for approval depending on the active profile and permissions.

### Controls

The desktop controls expose common actions:

- Refresh session state.
- Switch model.
- Toggle thinking mode.
- Switch profile.
- View help, models, permissions, and rules.
- Add permission overrides.
- Undo or redo tracked file edits.

## Terminal Workflow

### Interactive Mode

```bash
nanoai
```

Interactive mode opens the terminal UI with conversation history, live activity, prompts, and status.

### One-Shot Prompt

```bash
nanoai "Find risky changes in this branch"
```

### Prompt From Standard Input

```bash
git diff --stat | nanoai --stdin --profile review
```

### Resume a Section

When you exit, NanoAgent prints a section resume command. You can also resume directly:

```bash
nanoai --section <section-guid>
```

### CLI Options

| Option | Description |
| --- | --- |
| `--interactive` | Start the terminal UI explicitly. |
| `--stdin` | Read one-shot prompt text from standard input. |
| `-p, --prompt <text>` | Run one prompt and print the response. |
| `--provider-auth-key <key>` | Use this key when provider API-key onboarding asks for a credential. |
| `--section <id>` | Resume an existing section. |
| `--session <id>` | Alias for `--section`. |
| `--profile <name>` | Start with a profile. |
| `--thinking <on\|off>` | Start with thinking on or off. |
| `-h, --help` | Show CLI help. |

## Terminal Commands

| Command | Description |
| --- | --- |
| `/help` | List commands and usage. |
| `/config` | Show provider, session, config path, profile, thinking mode, and model. |
| `/models` | Show available models. |
| `/onboard` | Re-run provider onboarding and switch the active session to the new provider. |
| `/use <model>` | Switch the active model. |
| `/profile <name>` | Switch the active profile. |
| `/thinking [on\|off]` | Show or set simple thinking mode. |
| `/permissions` | Show permission summary and override guidance. |
| `/rules` | Show effective permission rules in evaluation order. |
| `/allow <tool-or-tag> [pattern]` | Add a session allow override. |
| `/deny <tool-or-tag> [pattern]` | Add a session deny override. |
| `/mcp` | Show MCP servers, custom tool providers, and dynamic tools. |
| `/init` | Initialize workspace-local NanoAgent files. |
| `/update [now]` | Check for updates. Use `/update now` to install without another prompt. |
| `/undo` | Roll back the most recent tracked edit transaction. |
| `/redo` | Re-apply the most recently undone edit transaction. |
| `/exit` | Exit the interactive shell. |

Terminal utility commands also include `/clear`, `/ls`, and `/read <file>`.

## Providers and Models

NanoAgent stores a provider profile locally and discovers models from that provider when possible.

Use:

```text
/models
/use <model>
```

The active model is stored with the local configuration and section state. If a preferred model is unavailable, NanoAgent falls back to a discovered model when possible.

### Thinking Mode

NanoAgent supports simple thinking mode:

```text
/thinking on
/thinking off
```

When enabled, provider requests include the supported provider reasoning setting where applicable.

## Profiles and Subagents

Profiles shape how NanoAgent behaves.

| Profile | Mode | Edit behavior | Best for |
| --- | --- | --- | --- |
| `build` | Primary | Allows edits under permissions | Implementation, fixes, tests, build loops. |
| `plan` | Primary | Read-only | Investigation and implementation plans. |
| `review` | Primary | Read-only | Findings-first code review. |
| `general` | Subagent | Allows edits under permissions | Bounded delegated implementation work. |
| `explore` | Subagent | Read-only | Fast codebase discovery. |

Switch profiles:

```text
/profile build
/profile plan
/profile review
```

Invoke a subagent for one turn:

```text
@explore How does authentication work?
@general Update the parser tests for this narrow case.
```

Primary agents can also use `agent_delegate` for one focused handoff or `agent_orchestrate` for several coordinated subtasks. Orchestration is useful when multiple read-only investigations can run independently or when implementation tasks can be split into clear file scopes.

## Permissions and Sandboxing

NanoAgent evaluates every sensitive action through permission policy.

### Permission Modes

| Mode | Meaning |
| --- | --- |
| `Allow` | The action can proceed. |
| `Ask` | NanoAgent prompts for approval. |
| `Deny` | The action is blocked. |

### Sandbox Modes

| Mode | Meaning |
| --- | --- |
| `ReadOnly` | No file writes or unsafe shell mutation. |
| `WorkspaceWrite` | Workspace-scoped writes are allowed under policy. |
| `DangerFullAccess` | Unrestricted execution when explicitly configured or approved. |

Shell sandboxing depends on the operating system. Linux uses `bubblewrap` when available. macOS uses `sandbox-exec`. Platforms without a supported OS sandbox runner fail closed for restricted shell modes unless the user approves escalation or configures full access.

### Session Overrides

Use overrides for temporary decisions:

```text
/allow shell_safe "dotnet test"
/deny shell "rm -rf"
```

Overrides are session-scoped. For durable policy, edit configuration.

### Example Permission Policy

```json
{
  "Application": {
    "Permissions": {
      "auto_approve_all_tools": false,
      "file_read": "Allow",
      "file_write": "Ask",
      "file_delete": "Ask",
      "shell_default": "Ask",
      "shell_safe": "Allow",
      "network": "Ask",
      "memory_write": "Ask",
      "mcp_tools": "Ask",
      "shell": {
        "allow": {
          "commands": [
            "dotnet build",
            "dotnet test",
            "npm test",
            "pnpm test",
            "cargo test"
          ]
        },
        "deny": {
          "commands": [
            "rm -rf",
            "sudo",
            "curl | sh",
            "Invoke-WebRequest | iex"
          ]
        }
      }
    }
  }
}
```

The `network` shortcut applies to built-in `webfetch` tools, including `web_run` and `headless_browser`. `headless_browser` renders pages through an installed Chromium-family browser such as Microsoft Edge, Google Chrome, or Chromium.

### Auto-Approve All Tools

For trusted workspaces, you can disable approval prompts for all tools:

```json
{
  "Application": {
    "Permissions": {
      "auto_approve_all_tools": true
    }
  }
}
```

This keeps workspace path checks, profile restrictions, sandbox-mode restrictions, and built-in deny rules active. Use explicit `rules` or shortcut settings when you need to override a specific deny policy.

## Workspace Files

Run:

```text
/init
```

NanoAgent creates:

```text
.nanoagent/
  agent-profile.json
  README.md
  .gitignore
  .nanoignore
  agents/
  skills/
  memory/
  logs/
```

### `AGENTS.md`

Place `AGENTS.md` or `.agent/AGENTS.md` in the workspace for persistent project instructions. NanoAgent adds them to the model context after secret redaction.

### `.nanoagent/.nanoignore`

Use `.nanoignore` to exclude paths from NanoAgent file tools. It supports gitignore-style patterns including comments, negation, directory rules, `*`, `?`, `**`, and character classes.

Common exclusions:

```text
.env
.env.*
secrets.*
[Bb]in/
[Oo]bj/
node_modules/
.git/
.nanoagent/logs/
.nanoagent/memory/
```

## Skills and Custom Agents

### Workspace Skills

Skills are task-specific playbooks loaded only when relevant.

Supported layouts:

```text
.nanoagent/skills/dotnet/SKILL.md
.nanoagent/skills/code-review.md
```

Example:

```markdown
---
name: dotnet
description: Use for .NET build, test, package, and project-file work.
---
Prefer repo-native `dotnet build` and `dotnet test` commands.
Inspect the relevant `.csproj` before changing package references.
Keep package and target framework changes narrowly scoped.
```

### Custom Agents

Custom agents live in:

```text
.nanoagent/agents/*.md
```

Example:

```markdown
---
name: code-reviewer
mode: subagent
description: Read-only reviewer for bugs, regressions, edge cases, and missing tests.
editMode: readOnly
shellMode: safeInspectionOnly
tools:
  - code_intelligence
  - directory_list
  - file_read
  - search_files
  - shell_command
  - text_search
---
Review the requested code or change set with a findings-first posture.
```

If front matter is omitted, NanoAgent derives the name from the file name and uses conservative defaults.

## MCP Servers

NanoAgent can load MCP servers from user-level and workspace-level `agent-profile.json` files.

Example:

```json
{
  "mcpServers": {
    "context7": {
      "command": "npx",
      "args": ["-y", "@upstash/context7-mcp"],
      "startupTimeoutSeconds": 20,
      "toolTimeoutSeconds": 45,
      "defaultToolsApprovalMode": "prompt",
      "env": {
        "MY_ENV_VAR": "MY_ENV_VALUE"
      }
    }
  }
}
```

Supported transports:

- Stdio: `command`, `args`, `env`, `envVars`, `cwd`.
- Streamable HTTP: `url`, `bearerTokenEnvVar`, `httpHeaders`, `envHttpHeaders`.

Use `enabledTools` and `disabledTools` to filter exposed tools. Use `/mcp` to inspect loaded MCP servers, custom tool providers, and dynamic tools.

## Custom Tools

NanoAgent can expose user-defined process tools from `agent-profile.json`. A custom tool can be written in any language that can read JSON from stdin and write text or JSON to stdout. Configured tools are exposed to the model as `custom__<name>`.
`mcpServers` and `customTools` can be configured in the same profile; NanoAgent loads both sets together and exposes MCP tools as `mcp__*` plus custom tools as `custom__*`.

Example:

```json
{
  "customTools": {
    "word_count": {
      "description": "Count words in provided text.",
      "command": "python",
      "args": [".nanoagent/tools/word_count.py"],
      "cwd": ".",
      "approvalMode": "prompt",
      "timeoutSeconds": 15,
      "schema": {
        "type": "object",
        "properties": {
          "text": {
            "type": "string",
            "description": "Text to count."
          }
        },
        "required": ["text"],
        "additionalProperties": false
      }
    }
  }
}
```

NanoAgent sends this JSON to the process on stdin:

```json
{
  "toolName": "custom__word_count",
  "configuredName": "word_count",
  "arguments": {
    "text": "hello world"
  },
  "session": {
    "id": "session-id",
    "workspacePath": "/path/to/workspace",
    "workingDirectory": "."
  }
}
```

The process can print plain stdout, which is treated as a successful text result, or a structured response:

```json
{
  "status": "success",
  "message": "Counted words.",
  "data": {
    "words": 2
  },
  "renderText": "2 words"
}
```

Use `status: "error"` for execution errors or `status: "invalid_arguments"` for argument validation failures. Relative `cwd` and relative command paths are resolved against the workspace root. Custom tools default to approval prompts; use permission rules or `approvalMode: "auto"` only for tools you trust.

## Memory, Audit, and Hooks

### Lesson Memory

NanoAgent stores reusable workspace lessons in:

```text
.nanoagent/memory/lessons.jsonl
```

Lessons help NanoAgent avoid repeating local mistakes. Memory is local, redacted by default, and write operations require approval unless policy is changed.

### Tool Audit

Tool audit logging is disabled by default. When enabled, NanoAgent writes completed tool-call records to:

```text
.nanoagent/logs/tool-audit.jsonl
```

### Workspace Policy

Configure memory and audit behavior in `.nanoagent/agent-profile.json`:

```json
{
  "memory": {
    "requireApprovalForWrites": true,
    "allowAutoFailureObservation": true,
    "allowAutoManualLessons": false,
    "redactSecrets": true,
    "maxEntries": 500,
    "maxPromptChars": 12000,
    "disabled": false
  },
  "toolAudit": {
    "enabled": false,
    "redactSecrets": true,
    "maxArgumentsChars": 12000,
    "maxResultChars": 12000
  }
}
```

### Lifecycle Hooks

Hooks run local automation around NanoAgent actions. A hook receives JSON on standard input and selected `NANOAGENT_*` environment variables.

Example:

```json
{
  "Application": {
    "Hooks": {
      "enabled": true,
      "defaultTimeoutSeconds": 30,
      "maxOutputCharacters": 12000,
      "rules": [
        {
          "name": "check-write",
          "events": ["before_file_write", "after_file_write"],
          "command": "scripts/check-write.ps1",
          "pathPatterns": ["src/**", "NanoAgent/**"]
        },
        {
          "name": "shell-failure",
          "event": "after_shell_failure",
          "command": "scripts/on-shell-failure.ps1",
          "shellCommandPatterns": ["dotnet test*", "npm test*"]
        }
      ]
    }
  }
}
```

Supported hook events include task, tool, file, shell, web, memory, permission, and delegation lifecycle events.

## Privacy and Local Data

Local:

- Workspace files stay on your machine.
- Configuration is local.
- Sections are stored locally.
- Lesson memory is stored locally.
- Optional audit logs are stored locally.
- Secrets are stored through platform credential storage where supported.

Sent to the configured provider when needed:

- User prompts.
- System and workspace instructions.
- Relevant file excerpts.
- Tool outputs.
- Conversation context.
- Model and tool schemas.

NanoAgent redacts common secret patterns before storing or displaying tool output, memory, audit records, logs, conversation history, session state, workspace instructions, and errors. Redaction is pattern-based and should not be treated as a full data-loss-prevention system.

## Troubleshooting

### `nanoai` is not found

Restart the terminal after installation. If it still fails, verify that the install directory is on `PATH`.

### Provider setup is incomplete

Run `nanoai` and choose to reconfigure. This can happen when setup was cancelled after provider config was saved but before the secret was stored.

### Provider validation fails after onboarding

Choose to re-run onboarding when NanoAgent offers it. If the same provider still fails, check the credential, account access, selected provider base URL, and network connectivity.

### Updating NanoAgent

Run `/update` to check for a newer release. Run `/update now` to install the latest release immediately, then restart NanoAgent.

### ChatGPT Plus/Pro sign-in does not complete

Check that port `1455` is available and that the browser callback URL opens locally. Sign-in requires network access and a valid account with access to the selected model.

### No models are listed

Check the provider credential, provider account access, network connectivity, and custom provider base URL. For compatible providers, the base URL must be absolute and use HTTP or HTTPS.

### A command is denied

Run `/permissions` and `/rules` to see active policy. You can approve the prompt, add a session override with `/allow`, or update configuration.

### Shell sandboxing fails on Windows

Windows does not provide the same restricted shell runner used by Linux and macOS in this project. Restricted shell modes fail closed; approve an escalation request or use a less restrictive sandbox only when you trust the command.

### The agent cannot read a file

Check that the path is inside the workspace and not excluded by `.nanoagent/.nanoignore` or default secret-protection rules.

### Undo did not revert a shell side effect

Undo/redo only covers tracked file edit transactions. It does not revert arbitrary shell command side effects, package installs, generated files, external tools, or network actions.

## Build From Source

Requirements:

- .NET SDK compatible with `net10.0`.
- Platform toolchains needed by your target desktop/CLI build.

Commands:

```bash
dotnet restore NanoAgent.CLI/NanoAgent.CLI.sln
dotnet build NanoAgent.CLI/NanoAgent.CLI.sln
dotnet test NanoAgent.Tests/NanoAgent.Tests.csproj
```

The main projects are:

| Project | Purpose |
| --- | --- |
| `NanoAgent` | Core application, domain, infrastructure, tools, providers, storage. |
| `NanoAgent.CLI` | Terminal UI and one-shot CLI. |
| `NanoAgent.Desktop` | Desktop app. |
| `NanoAgent.Tests` | Test suite. |

## License

NanoAgent is licensed under the Apache License 2.0. See [../LICENSE](../LICENSE).
