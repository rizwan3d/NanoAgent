<p align="center">
  <img src=".github/nano.jpg" alt="NanoAgent" width="800">
</p>

<h1 align="center">NanoAgent</h1>

<p align="center">
  A local coding agent for desktop and terminal workflows
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

## What is NanoAgent?

NanoAgent is a local coding agent that helps with day-to-day software engineering tasks from a desktop UI or terminal workflow. It can search and read files, apply focused patches, run build and test commands, manage model/provider configuration, and preserve local workspace sections.

---

## Features

- **Sandboxed Tool Calls** - Use read-only, workspace-write, or danger-full-access sandbox modes with shell escalation requests
- **Workspace Instructions** - Load persistent repo guidance from `AGENTS.md` or `.agent/AGENTS.md`
- **MCP Servers** - Load MCP servers from NanoAgent user and workspace configuration files
- **VS Code Extension** - Use the companion extension from `NanoAgent.VSCode`
- **File Operations** — Search, read, and edit files with full regex support
- **Shell Execution** — Run build/test commands directly from your terminal
- **Multi-Agent Profiles** — Switch between `build`, `plan`, and `review` profiles for different workflows
- **Thinking Effort** — Configure thinking effort: none, minimal, low, medium, high, or xhigh
- **Subagent Delegation** — Delegate focused tasks to `general` or `explore` subagents
- **Provider Flexibility** — OpenAI, Anthropic, Google AI Studio, or any OpenAI-compatible API
- **Desktop UI** — Use workspace sections, colorful tool output, and permission prompts in a native app
- **Session History** — Preserve conversation context across workspace sections
- **Local-First** — All your code stays on your machine

---

## Installation

### Desktop

Download the latest desktop app:
|    OS    |           |
|----------|-----------|
| Win x64  | [![Download ](https://img.shields.io/badge/Download-2ea44f?style=for-the-badge)](https://github.com/rizwan3d/NanoAgent/releases/latest/download/NanoAgent.Desktop-win-x64-setup.exe) |
| Linux x64| [![Download ](https://img.shields.io/badge/Download-2ea44f?style=for-the-badge)](https://github.com/rizwan3d/NanoAgent/releases/latest/download/NanoAgent.Desktop-linux-x64.zip) |
| Linux arm64| [![Download ](https://img.shields.io/badge/Download-2ea44f?style=for-the-badge)](https://github.com/rizwan3d/NanoAgent/releases/latest/download/NanoAgent.Desktop-linux-arm64.zip) |
| Osx x64 | [![Download ](https://img.shields.io/badge/Download-2ea44f?style=for-the-badge)](https://github.com/rizwan3d/NanoAgent/releases/latest/download/NanoAgent.Desktop-osx-x64.zip) |
| Osx arm64 | [![Download ](https://img.shields.io/badge/Download-2ea44f?style=for-the-badge)](https://github.com/rizwan3d/NanoAgent/releases/latest/download/NanoAgent.Desktop-osx-arm64.zip) |

### macOS / Linux

```bash
curl -fsSL https://raw.githubusercontent.com/rizwan3d/NanoAgent/master/scripts/install.sh | bash
```

### Windows (PowerShell)

```powershell
irm https://raw.githubusercontent.com/rizwan3d/NanoAgent/master/scripts/install.ps1 | iex
```

Restart your shell after installation if the `nano` command is not immediately available.

---

## VS Code Extension

The VS Code companion extension lives in [`NanoAgent.VSCode`](NanoAgent.VSCode). It starts `nanoai` in the integrated terminal and can send prompts from the command palette, editor selections, and Explorer files.

```bash
cd NanoAgent.VSCode
npm install
npm run compile
```

Open the extension folder in VS Code and press `F5` to launch an Extension Development Host.

---

## Quick Start

```bash
# Start the agent
nanoai
```

---

## Supported Providers

| Provider |
|----------|
| OpenAI |
| Anthropic |
| Google AI |
| Custom (OpenAI-compatible) |

---

## Usage

### Switch Profiles

| Command | Description |
|---------|-------------|
| `/profile build` | Switch to build profile |
| `/profile plan` | Switch to planning profile |
| `/profile review` | Switch to review profile |

### Delegate to Subagents

| Syntax | Description |
|--------|-------------|
| `@general` | Hand one turn to general-purpose subagent |
| `@explore` | Hand one turn to read-only explorer |

### Shell Commands

| Command | Description |
|---------|-------------|
| `/help` | List available commands |
| `/config` | Show current provider, model, profile |
| `/mcp` | Show configured MCP servers and discovered MCP tools |
| `/models` | Show available models |
| `/use <model>` | Switch active model |
| `/profile <name>` | Switch active profile (build, plan, review) |
| `/thinking` | Set thinking effort (none, minimal, low, medium, high, xhigh) |
| `/permissions` | Show permission summary |
| `/allow <tool>` | Allow a tool override |
| `/deny <tool>` | Deny a tool override |
| `/rules` | List effective permission rules |
| `/undo` | Roll back last file edit |
| `/redo` | Re-apply undone edit |
| `/exit` | Exit the shell |

### Tool Sandboxing

NanoAgent defaults to `WorkspaceWrite` sandbox mode for tool calls. Configure `Application:Permissions:SandboxMode` as `ReadOnly`, `WorkspaceWrite`, or `DangerFullAccess`.

Shell tool calls can request `sandbox_permissions: "require_escalated"` with a `justification`; escalation goes through the normal permission approval flow.

### Workspace Instructions

NanoAgent automatically loads `AGENTS.md` and `.agent/AGENTS.md` from the workspace root and adds them to the model's system prompt as persistent project instructions.

### MCP Servers

NanoAgent can connect to MCP servers configured with the `[mcp_servers]` TOML shape. It reads the user-level NanoAgent `mcp.toml` file shown by `/config` and the workspace-local `.nanoagent/config.toml`, then exposes server tools to the model as `mcp__server__tool`.

```toml
[mcp_servers.context7]
command = "npx"
args = ["-y", "@upstash/context7-mcp"]
startup_timeout_sec = 20
tool_timeout_sec = 45
default_tools_approval_mode = "prompt"

[mcp_servers.context7.env]
MY_ENV_VAR = "MY_ENV_VALUE"
```

Supported transports are stdio (`command`, `args`, `env`, `env_vars`, `cwd`) and basic streamable HTTP (`url`, `bearer_token_env_var`, `http_headers`, `env_http_headers`). Use `enabled_tools` or `disabled_tools` to filter server tools, and `/mcp` to inspect what loaded.

---

## Examples

### Fix a Bug

```
$ nanoai
> Find and fix the memory leak in src/cache.c
```

### Explore Codebase

```
$ nanoai
> @explore How does authentication work in this project?
```

### Review Changes

```
$ nanoai
/profile review
> Review the changes in this branch for security issues
```

---

## License

Apache License 2.0 — See [LICENSE](LICENSE) for details.

---

<p align="center">
  Sponsored by  <br /> <a href="https://alfain.co/"><img src="https://alfain.co/assets/images/logo-alfain.png" width="100" alt="ALFAIN Technologies (PVT) Limited"></a>
</p>
