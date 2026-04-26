<p align="center">
  <img src=".github/nano.jpg" alt="NanoAgent" width="800">
</p>

<h1 align="center">NanoAgent</h1>

<p align="center">
  A local-first AI coding agent for desktop and terminal workflows.
</p>

<p align="center">
  <a href="https://github.com/rizwan3d/NanoAgent/blob/main/LICENSE"><img src="https://img.shields.io/badge/License-Apache--2.0-blue.svg" alt="License"></a>
  <a href="https://github.com/rizwan3d/NanoAgent"><img src="https://img.shields.io/github/v/release/rizwan3d/NanoAgent" alt="Version"></a>
  <a href="https://github.com/rizwan3d/NanoAgent/stargazers"><img src="https://img.shields.io/github/stars/rizwan3d/NanoAgent" alt="Stars"></a>
  <a href="https://github.com/rizwan3d/NanoAgent/issues"><img src="https://img.shields.io/github/issues/rizwan3d/NanoAgent" alt="Issues"></a>
</p>

<p align="center">
  <a href="https://github.com/rizwan3d/NanoAgent/releases/latest/download/NanoAgent.Desktop-win-x64-setup.exe">
    <img src="https://img.shields.io/badge/Download-Desktop%20Installer-2ea44f?style=for-the-badge" alt="Download NanoAgent Desktop">
  </a>
</p>

---

NanoAgent helps you work inside real local repositories with an AI agent that can inspect code, make focused edits, run commands, review changes, and remember useful workspace lessons. Use it from the native desktop app or the `nanoai` terminal command.

NanoAgent is built for controlled autonomy: the agent can act on your codebase, but file edits, shell commands, network access, memory writes, MCP tools, and escalation requests are governed by profiles, permissions, and local sandbox policy.

## Highlights

- Local workspace agent for implementation, planning, review, and build/test loops.
- Desktop app and terminal UI, with one-shot CLI prompts for automation.
- Providers: OpenAI, OpenAI ChatGPT Plus/Pro sign-in, Anthropic, Google AI Studio, and OpenAI-compatible APIs.
- Built-in `build`, `plan`, and `review` profiles, plus focused `general` and `explore` subagents.
- Workspace instructions from `AGENTS.md`.
- Workspace skills and custom agents in `.nanoagent/`.
- MCP server support for external tools.
- Lesson memory, optional tool audit logs, and secret redaction.
- Permission rules, approval prompts, sandbox modes, and undo/redo for tracked file edits.

## Install

### Desktop

| Platform | Download |
| --- | --- |
| Windows x64 | [Installer](https://github.com/rizwan3d/NanoAgent/releases/latest/download/NanoAgent.Desktop-win-x64-setup.exe) |
| Linux x64 | [Zip](https://github.com/rizwan3d/NanoAgent/releases/latest/download/NanoAgent.Desktop-linux-x64.zip) |
| Linux arm64 | [Zip](https://github.com/rizwan3d/NanoAgent/releases/latest/download/NanoAgent.Desktop-linux-arm64.zip) |
| macOS x64 | [Zip](https://github.com/rizwan3d/NanoAgent/releases/latest/download/NanoAgent.Desktop-osx-x64.zip) |
| macOS arm64 | [Zip](https://github.com/rizwan3d/NanoAgent/releases/latest/download/NanoAgent.Desktop-osx-arm64.zip) |

### CLI

macOS / Linux:

```bash
curl -fsSL https://raw.githubusercontent.com/rizwan3d/NanoAgent/master/scripts/install.sh | bash
```

Windows PowerShell:

```powershell
irm https://raw.githubusercontent.com/rizwan3d/NanoAgent/master/scripts/install.ps1 | iex
```

Restart your shell if `nanoai` is not immediately available.

## Quick Start

```bash
# Start the interactive terminal UI
nanoai

# Run one prompt and print the answer
nanoai "Summarize this repository"

# Pipe a prompt from another command
echo "Review the latest changes for regressions" | nanoai --profile review
```

On first run, NanoAgent asks you to choose a provider and model. API-key providers store the secret in your operating system credential store where supported. The OpenAI ChatGPT Plus/Pro option opens a browser sign-in flow and uses a local callback at `http://localhost:1455/auth/callback`.

## Everyday Commands

| Command | What it does |
| --- | --- |
| `/help` | Show available terminal commands. |
| `/config` | Show provider, model, section, profile, thinking mode, and config path. |
| `/models` | List available models for the current provider. |
| `/use <model>` | Switch the active model. |
| `/profile <name>` | Switch profile, such as `build`, `plan`, or `review`. |
| `/thinking [on\|off]` | Show or set simple thinking mode. |
| `/permissions` | Show current permission summary and override guidance. |
| `/rules` | Show effective permission rules. |
| `/allow <tool-or-tag> [pattern]` | Add a session-scoped allow override. |
| `/deny <tool-or-tag> [pattern]` | Add a session-scoped deny override. |
| `/mcp` | Show configured MCP servers and tools. |
| `/init` | Create workspace-local `.nanoagent` starter files. |
| `/undo` | Roll back the most recent tracked file edit transaction. |
| `/redo` | Re-apply the most recently undone file edit transaction. |
| `/exit` | Exit the terminal UI. |

## Profiles

| Profile | Purpose |
| --- | --- |
| `build` | Default hands-on coding profile for edits, toolchain work, and validation. |
| `plan` | Read-only investigation and implementation planning. |
| `review` | Read-only code review focused on bugs, regressions, edge cases, and tests. |
| `general` | Implementation-capable subagent for bounded delegated work. |
| `explore` | Read-only subagent for fast codebase investigation. |

Invoke a subagent for one turn with `@explore`, `@general`, or a workspace custom agent name.

## Workspace Setup

Run `/init` in a repository to create starter workspace files:

```text
.nanoagent/
  agent-profile.json
  .nanoignore
  agents/
  skills/
  memory/
  logs/
```

NanoAgent also reads `AGENTS.md` and `.agent/AGENTS.md` as persistent workspace instructions.

## Safety Model

NanoAgent keeps the user in the loop through:

- Permission modes: `Allow`, `Ask`, and `Deny`.
- Sandbox modes: `ReadOnly`, `WorkspaceWrite`, and `DangerFullAccess`.
- Read-only `plan` and `review` profiles.
- Approval prompts for sensitive tool calls.
- Session-scoped allow/deny overrides.
- Secret redaction before logs, memory, audit records, and displayed tool output.
- Undo/redo for tracked file edits.

Your project files remain local. Prompts, relevant code snippets, tool output, and conversation context are sent to the model provider you configure when they are needed to answer or act.

## Documentation

Read the full guide in [docs/documentation.md](docs/documentation.md). It covers installation, onboarding, desktop usage, CLI usage, provider setup, permissions, MCP, memory, hooks, custom agents, troubleshooting, and source builds.

## Build From Source

NanoAgent targets .NET `net10.0`.

```bash
dotnet restore NanoAgent.CLI/NanoAgent.CLI.sln
dotnet build NanoAgent.CLI/NanoAgent.CLI.sln
dotnet test NanoAgent.Tests/NanoAgent.Tests.csproj
```

## License

Apache License 2.0. See [LICENSE](LICENSE).

---

<p align="center">
  Sponsored by<br>
  <a href="https://alfain.co/"><img src="https://alfain.co/assets/images/logo-alfain.png" width="100" alt="ALFAIN Technologies (PVT) Limited"></a>
</p>
