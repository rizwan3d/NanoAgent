# NanoAgent Documentation

NanoAgent is an AI coding agent for people who want an assistant that can work directly inside a repository while still respecting local permissions, approval prompts, and workspace policy. It runs as a desktop app, the `nanoai` terminal command, a VS Code extension, a Visual Studio extension, and an ACP-compatible editor server.

This guide contains the setup, reference, and technical material for NanoAgent. The root README is the product overview; this document is the handbook for installation, daily use, safety controls, integration, automation, and advanced workspace customization.

## Contents

- [Install](#install)
- [First Run](#first-run)
- [Desktop Workflow](#desktop-workflow)
- [Terminal Workflow](#terminal-workflow)
- [VS Code Extension](#vs-code-extension)
- [Visual Studio Extension](#visual-studio-extension)
- [ACP Editor Integration](#acp-editor-integration)
- [Review Automation](#review-automation)
- [Codebase Indexing](#codebase-indexing)
- [Providers and Models](#providers-and-models)
- [Profiles and Subagents](#profiles-and-subagents)
- [Permissions and Sandboxing](#permissions-and-sandboxing)
- [Workspace Files](#workspace-files)
- [Team Memory](#team-memory)
- [Skills and Custom Agents](#skills-and-custom-agents)
- [MCP Servers](#mcp-servers)
- [Memory, Audit, and Hooks](#memory-audit-and-hooks)
- [Privacy and Local Data](#privacy-and-local-data)
- [Troubleshooting](#troubleshooting)
- [Build From Source](#build-from-source)

## Install

### Desktop App

Download the latest release for your platform:

| Platform | Download |
| --- | --- |
| Windows x64 | [Installer](https://github.com/rizwan3d/NanoAgent/releases/latest/download/NanoAgent.Desktop-win-x64-setup.exe) |
| Linux x64 | [Zip](https://github.com/rizwan3d/NanoAgent/releases/latest/download/NanoAgent.Desktop-linux-x64.zip) |
| Linux arm64 | [Zip](https://github.com/rizwan3d/NanoAgent/releases/latest/download/NanoAgent.Desktop-linux-arm64.zip) |
| macOS x64 | [Zip](https://github.com/rizwan3d/NanoAgent/releases/latest/download/NanoAgent.Desktop-osx-x64.zip) |
| macOS arm64 | [Zip](https://github.com/rizwan3d/NanoAgent/releases/latest/download/NanoAgent.Desktop-osx-arm64.zip) |

Release downloads are published at:

```text
https://github.com/rizwan3d/NanoAgent/releases/latest
```

New release assets include `SHA256SUMS` beside the downloads. The release pipeline verifies every checksum matches its asset before publishing, and GitHub release workflows also generate artifact attestations that establish SLSA build provenance for the checksummed assets. For manual downloads, compare the published SHA256 hash with your downloaded file before running it. To verify provenance with GitHub CLI, run `gh attestation verify path/to/asset -R rizwan3d/NanoAgent`.

### CLI

Install with the release installer.

macOS / Linux:

```bash
curl -fsSL https://raw.githubusercontent.com/rizwan3d/NanoAgent/master/scripts/install.sh | bash
```

Windows PowerShell:

```powershell
irm https://raw.githubusercontent.com/rizwan3d/NanoAgent/master/scripts/install.ps1 | iex
```

The installers show step status and download progress when run in an interactive terminal. Set `NANOAGENT_NO_PROGRESS=1` to keep output compact in CI logs.

Restart your terminal if `nanoai` is not found immediately after installation.

The release workflows also pack the `NanoAgent` library and publish it to NuGet.org for every `v*` tag release. The CLI is distributed only as a release installer, not as a NuGet package.

The CLI install scripts verify the archive checksum against `SHA256SUMS`, or the SHA256 digest from GitHub release metadata, before extraction. Checksum verification is mandatory â€” installation fails if the checksum cannot be validated.

## First Run

Start NanoAgent:

```bash
nanoai
```

NanoAgent will guide you through provider setup:

1. Choose a setup type: subscription account, API key provider, OpenAI-compatible provider, or local provider.
2. Choose a provider from the matching submenu when needed.
3. Enter an API key, sign in with ChatGPT Plus/Pro, Claude Pro/Max, or GitHub Copilot, enter a custom compatible base URL, or use a local provider default.
4. Let NanoAgent discover available models.
5. Open a desktop workspace or use the current terminal directory.
6. Start a new section or resume an existing one.

In terminal runs, `--provider-auth-key <key>` can supply the provider API key when onboarding asks for it.

If you already know the provider settings you want, you can skip the interactive onboarding prompts by setting `NANOAGENT_PROVIDER`, `NANOAGENT_MODEL`, `NANOAGENT_THINKING`, optional `NANOAGENT_REASONING`, and `NANOAGENT_API_KEY` before the first run. NanoAgent treats that as a complete headless setup and saves it as the active provider profile.

PowerShell example:

```powershell
$env:NANOAGENT_PROVIDER="openrouter"
$env:NANOAGENT_MODEL="poolside/laguna-m.1:free"
$env:NANOAGENT_THINKING="on"
$env:NANOAGENT_REASONING="high"
$env:NANOAGENT_API_KEY="PASTE_NEW_ROTATED_KEY_HERE"

nanoai -p "Say hello in one short line"
```

Bash example:

```bash
export NANOAGENT_PROVIDER="openrouter"
export NANOAGENT_MODEL="poolside/laguna-m.1:free"
export NANOAGENT_THINKING="on"
export NANOAGENT_REASONING="high"
export NANOAGENT_API_KEY="PASTE_NEW_ROTATED_KEY_HERE"

nanoai -p "Say hello in one short line"
```

If NanoAgent detects incomplete local provider setup, it asks whether to reconfigure. Choose reconfigure when a previous setup was interrupted or credentials were not saved. If provider validation fails after setup, NanoAgent offers to run onboarding again.

Use `/onboard` in an active desktop or terminal session to re-run provider setup later. You can also use `/setting provider` or the `/setting` picker. The command opens setup-type and provider submenus, supports every provider listed below, and switches the active session to the validated provider and selected default model.

When a newer NanoAgent release is available, startup can ask whether to update now or skip. One-shot prompt runs do not show the startup update prompt.

### Provider Options

| Group | Provider | Credential method | Notes |
| --- | --- | --- | --- |
| Subscription based | OpenAI ChatGPT Plus/Pro | Browser sign-in | Uses OAuth with local callback port `1455`. |
| Subscription based | Anthropic Claude Pro/Max | Browser sign-in | Uses OAuth with local callback port `53692`. |
| Subscription based | GitHub Copilot | Browser device sign-in | Uses GitHub device-code login. Leave the Enterprise URL/domain prompt blank for `github.com`. |
| API key | OpenAI | API key | Uses the OpenAI API. |
| API key | Anthropic | API key | Uses the Anthropic OpenAI-compatible endpoint. |
| API key | Google AI Studio | API key | Uses the OpenAI-compatible Gemini endpoint. |
| API key | OpenRouter | API key | Uses the OpenRouter OpenAI-compatible endpoint. |
| API key | Kilo Code | API key | Uses Kilo's OpenRouter-compatible gateway. |
| API key | Cerebras | API key | Uses the Cerebras OpenAI-compatible endpoint. |
| API key | Groq | API key | Uses the Groq OpenAI-compatible endpoint. |
| API key | DeepSeek | API key | Uses the DeepSeek OpenAI-compatible endpoint at `https://api.deepseek.com/`. |
| API key | Ollama Cloud | API key | Uses Ollama's hosted native chat and tags APIs. |
| OpenAI-compatible provider | OpenAI-compatible provider | Base URL and API key | Use for local or third-party compatible APIs. |
| Local provider | Ollama | None | Uses Ollama's local OpenAI-compatible endpoint at `http://127.0.0.1:11434/v1`. |
| Local provider | LM Studio | Base URL and API key | Uses LM Studio's local OpenAI-compatible endpoint. Leave the base URL empty to use `http://127.0.0.1:1234/v1`. |

Secrets are stored through platform credential storage where supported. ChatGPT Plus/Pro, Claude Pro/Max, and GitHub Copilot sign-in store refreshable account credentials locally.

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
Type `/` in the desktop prompt to open command suggestions. Use Up/Down and Enter to choose a command, or Shift+Enter for multiline input.
Start input with `!` to run the rest as a local shell command directly, for example `!dotnet test`. Direct shell input is treated as user-entered terminal work and does not ask the agent for a tool approval.
Start input with `!!` to run the rest as a background terminal whose output streams live, for example `!!dotnet watch`. Manage these background terminals with `/terminals`.

### Controls

The desktop controls expose common actions:

- Refresh session state.
- Switch model.
- Configure budget controls from a local workspace file or a cloud API.
- Toggle thinking mode.
- Switch profile.
- View help, model picker, permissions, and rules.
- Add permission overrides.
- Undo or redo tracked file edits.

Budget controls are disabled by default. They become active only after you enable them with `/budget local` or `/budget cloud`, or when a `.nanoagent/budget-controls.*.json` file already exists in the active workspace. While disabled, no usage is recorded, no tracking file is created, and provider requests are never blocked; `/budget status` reports `Disabled`.

Budget controls can run in local mode or cloud mode. Local mode asks for the monthly budget USD, alert threshold percent, and input, cached-input, and output prices per 1M tokens, then creates and updates `.nanoagent/budget-controls.local.json` in the active workspace. Cloud mode asks for the budget API URL and auth key; the URL is saved with user settings and the key is stored through the platform credential store. In the terminal, use `/budget`, `/budget local`, `/budget cloud`, or `/budget status`.

Cloud budget APIs use `Authorization: Bearer <auth-key>`.

GET returns the current budget state:

```json
{
  "monthlyBudgetUsd": 100,
  "spentUsd": 25.5,
  "alertThresholdPercent": 80
}
```

GET response JSON Schema:

```json
{
  "type": "object",
  "additionalProperties": false,
  "required": ["monthlyBudgetUsd", "spentUsd", "alertThresholdPercent"],
  "properties": {
    "monthlyBudgetUsd": {
      "type": ["number", "null"],
      "minimum": 0
    },
    "spentUsd": {
      "type": "number",
      "minimum": 0
    },
    "alertThresholdPercent": {
      "type": "integer",
      "minimum": 1,
      "maximum": 100
    }
  }
}
```

POST receives only the tokens consumed by the last LLM call:

```json
{
  "inputTokens": 1234,
  "cachedInputTokens": 250,
  "outputTokens": 600
}
```

POST request JSON Schema:

```json
{
  "type": "object",
  "additionalProperties": false,
  "required": ["inputTokens", "cachedInputTokens", "outputTokens"],
  "properties": {
    "inputTokens": {
      "type": "integer",
      "minimum": 0
    },
    "cachedInputTokens": {
      "type": "integer",
      "minimum": 0
    },
    "outputTokens": {
      "type": "integer",
      "minimum": 0
    }
  }
}
```

POST should add that delta to the backend usage database and return the updated budget state with the same JSON shape as GET.

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

### Resume a Session

When you exit, NanoAgent prints a session resume command. You can also resume directly:

```bash
nanoai --session <session-guid>
```

### CLI Options

| Option | Description |
| --- | --- |
| `--acp` | Run an Agent Client Protocol server over stdin/stdout for compatible editors and tools. |
| `--interactive` | Start the terminal UI explicitly. |
| `--stdin` | Read one-shot prompt text from standard input. |
| `--json` | Write one-shot prompt or command output as a JSON object. |
| `-y, --yes` | Approve promptable tool requests for this run while preserving explicit deny rules. |
| `-p, --prompt <text>` | Run one prompt and print the response. |
| `--provider-auth-key <key>` | Use this key when provider API-key onboarding asks for a credential. |
| `--session <id>` | Resume an existing session. |
| `--section <id>` | Compatibility alias for `--session`. |
| `--profile <name>` | Start with a profile. |
| `--thinking <on\|off>` | Start with thinking on or off. |
| `-h, --help` | Show CLI help. |

## Terminal Commands

| Command | Description |
| --- | --- |
| `/help` | List commands and usage. |
| `/budget [status\|local\|cloud]` | Show or configure budget controls. |
| `/config` | Show provider, model, profile, thinking mode, reasoning effort, and reasoning-output behavior. |
| `/models` | Choose the active model with the arrow-key picker. |
| `/use <model>` | Switch directly to a model id. |
| `/onboard` | Re-run provider onboarding through setup-type and provider submenus, then switch the active session. |
| `/profile <name>` | Switch the active profile. |
| `/thinking [on\|off]` | Show or set simple thinking mode. |
| `/reasoning [show\|<none\|minimal\|low\|medium\|high\|xhigh\|max>]` | Show or set provider reasoning effort. |
| `/tooloutput [compact\|full\|auto]` | Show or toggle whether tool results print their complete output or a compact preview. `auto` follows the active agent profile. |
| `/permissions` | Show permission summary and override guidance. |
| `/rules` | Show effective permission rules in evaluation order. |
| `/setting [model\|profile\|thinking\|provider\|budget\|workspace\|permissions\|tools\|summary]` | Open the settings picker or jump directly to a settings area. |
| `/allow <tool-or-tag> [pattern]` | Add a session allow override. |
| `/deny <tool-or-tag> [pattern]` | Add a session deny override. |
| `/mcp` | Show MCP servers, custom tool providers, and dynamic tools. |
| `/terminals [stop <id>\|stop all]` | List or stop background terminals for the current session. |
| `/init [recommended\|minimal\|custom]` | Choose and initialize workspace-local NanoAgent files. |
| `/update [now]` | Check for updates. Use `/update now` to install without another prompt. |
| `/undo` | Roll back the most recent tracked edit transaction. |
| `/redo` | Re-apply the most recently undone edit transaction. |
| `/exit` | Exit the interactive shell. |

Terminal utility commands also include `/clear`, `/ls`, and `/read <file>`.

### Custom Slash Commands

Project commands live in `.nanoagent/commands/*.md`. User commands live in `~/.nanoagent/commands/*.md`. Subdirectories create namespaces with `:`, so `.nanoagent/commands/review/security.md` is available as `/review:security`.

Each command file can include front matter:

```markdown
---
name: security-review
description: Review changed files for security risks
args: ["scope"]
---

Review $scope for authentication, injection, secrets, unsafe deserialization, and permission bypasses.
Return findings by severity.
```

Run commands with any arguments after the name:

```text
/security-review latest diff
/fix-tests NanoAgent.Tests
/release-check v0.0.16
```

Use `$ARGUMENTS` for the full argument string, or name positional arguments in `args` and reference them as `$scope` or `${scope}`. Project commands override user commands with the same name. Built-in command names are reserved.

`/setting` is a keyboard-friendly settings hub. Use it with no arguments to pick a settings area, or jump directly with commands such as `/setting model`, `/setting profile`, `/setting thinking`, `/setting budget status`, `/setting workspace custom`, `/setting permissions`, `/setting tools`, and `/setting summary`. Setting submenus use picker-style rows; Esc returns to the settings menu. `/setting permissions` writes default and sandbox changes to `.nanoagent/agent-profile.json`; direct commands like `/permissions` and `/rules` still keep their original text output.

Press F2 in the terminal UI to choose the active model with the same arrow-key picker.
Type `/` in the terminal input to open command suggestions, then use Up/Down and Enter to choose a command.
Start input with `!` to run the rest as a local shell command directly, for example `!git status --short`.

### Tool Runtime Settings

Workspace `agent-profile.json` can tune tool timeouts and background terminal retention:

```json
{
  "Application": {
    "Tools": {
      "httpClientTimeoutSeconds": 0,
      "mcpRequestTimeoutSeconds": 0,
      "acpRequestTimeoutSeconds": 0,
      "agentOrchestrationTimeoutSeconds": 0,
      "defaultTimeoutSeconds": 180,
      "maxConcurrentBackgroundTerminalsPerSession": 4,
      "completedBackgroundTerminalTtlSeconds": 300,
      "toolOutput": "compact"
    }
  }
}
```

Set `httpClientTimeoutSeconds` to override the default timeout used by NanoAgent-managed `HttpClient` instances. Set `mcpRequestTimeoutSeconds` to cap individual MCP request/response cycles for both stdio and HTTP MCP servers. Set `acpRequestTimeoutSeconds` to cap ACP editor prompt requests such as permission or text-entry requests. Set `agentOrchestrationTimeoutSeconds` to add an orchestration-wide timeout for `agent_orchestrate`. A value of `0` keeps the existing default behavior for each setting.

Set `toolOutput` to choose how tool results render in session output: `full` (or `complete`) prints the complete output and `compact` (or `preview`) prints the capped preview. Omit it or leave it unrecognized to keep the compact default. This is the lowest-priority source — a per-agent markdown profile's `toolOutput` front-matter key overrides it for that profile, and the `/tooloutput` command overrides both for the current session (`/tooloutput auto` reverts to the profile/configured default).

Completed background terminals remain readable until `completedBackgroundTerminalTtlSeconds` expires. Running background terminals are stopped when the NanoAgent process exits.

## VS Code Extension

NanoAgent includes a VS Code extension in `NanoAgent.VsCode`. It opens a NanoAgent chat view in the auxiliary bar and starts the local NanoAgent ACP process with:

```bash
nanoai --acp
```

Run `nanoai` once before using the extension so provider onboarding, credentials, and the default model are already configured.

Install from the Visual Studio Marketplace:

```text
ext install rizwan3d.nanoagent
```

The Marketplace item is:

```text
https://marketplace.visualstudio.com/items?itemName=rizwan3d.nanoagent
```

GitHub releases also publish an installable VSIX asset:

```text
NanoAgent.VsCode-<version>.vsix
```

### Extension Commands

| Command | Purpose |
| --- | --- |
| `NanoAgent: Open Chat` | Open the NanoAgent chat view. |
| `NanoAgent: New Chat` | Focus the chat view for a new prompt. |
| `NanoAgent: Start` | Start the local NanoAgent ACP process. |
| `NanoAgent: Stop` | Stop the local NanoAgent ACP process. |
| `NanoAgent: Restart` | Restart the local NanoAgent ACP process. |
| `NanoAgent: Send Selection` | Send the active editor selection as context. |
| `NanoAgent: Explain Selection` | Ask NanoAgent to explain the active selection. |
| `NanoAgent: Send Current File` | Send the full current editor file as context. |
| `NanoAgent: Review Current File` | Ask for a review of the current file. |
| `NanoAgent: Review Git Diff` | Ask for a review of the current workspace Git diff. |
| `NanoAgent: Plan Changes` | Prefill a planning prompt. |
| `NanoAgent: Apply Suggested Changes` | Ask NanoAgent to apply the previous suggested change. |
| `NanoAgent: Open Logs` | Show extension logs. |
| `NanoAgent: Open Settings` | Open the extension settings surface. |

### Extension Settings

| Setting | Default | Purpose |
| --- | --- | --- |
| `nanoagent.command` | `nanoai` | Command used to start NanoAgent. |
| `nanoagent.args` | `["--acp"]` | Arguments passed to the NanoAgent CLI. |
| `nanoagent.workingDirectory` | workspace root | Working directory for the NanoAgent process. |
| `nanoagent.autoStart` | `false` | Start NanoAgent automatically when VS Code starts. |
| `nanoagent.logLevel` | `info` | Extension log level. |

### Extension Development

Build and package locally:

```bash
cd NanoAgent.VsCode
npm ci
npm run lint
npm run package
npm run package:vsix
```

The package command creates an installable `.vsix`. Install a local package with:

```bash
code --install-extension nanoagent-<version>.vsix
```

### Extension Publishing

The release workflow `.github/workflows/release.yml` packages the extension as `NanoAgent.VsCode-<version>.vsix` and publishes it to GitHub Releases with the CLI, desktop, and NuGet assets. The signed release variant `.github/workflows/release-signing.yml` does the same when that workflow is used. Both workflows publish `SHA256SUMS`, push the `NanoAgent` library package to NuGet.org, and generate GitHub artifact attestations for the generated release assets.

The Marketplace CD workflow `.github/workflows/vscode-extension-cd.yml` publishes the extension to the Visual Studio Marketplace. It runs for `v*` tags and manual dispatch. For tag builds, the workflow removes the leading `v` and applies that value to `NanoAgent.VsCode/package.json` with `npm version --no-git-tag-version` before packaging.

Required repository secret:

```text
NUGET_API_KEY
VSCE_PAT
```

Create `NUGET_API_KEY` in NuGet.org with push permission for the target packages. Create `VSCE_PAT` in Azure DevOps with Marketplace Manage scope and access to the `rizwan3d` Visual Studio Marketplace publisher. The release workflow publishes NuGet packages with `dotnet nuget push`, and the Marketplace workflow publishes through `@vscode/vsce`, uploads the generated `.vsix` artifact, and uses the `vscode-marketplace` GitHub environment for deployment approval or environment-level protection rules if configured.

## Visual Studio Extension

NanoAgent includes a Visual Studio extension in `NanoAgent.VS`. It opens a tool window inside Visual Studio and starts the local NanoAgent CLI over ACP.

Before first use:

- Install the NanoAgent CLI so `nanoai.exe` is available on `PATH`, or set an explicit CLI path in the NanoAgent Visual Studio options page.
- Run `nanoai` once and complete provider onboarding.

### Local Build

Build the VSIX from a Developer PowerShell for Visual Studio:

```powershell
msbuild NanoAgent.VS/NanoAgent.VS.csproj /restore /p:Configuration=Release /p:DeployExtension=false
```

The package is written to:

```text
NanoAgent.VS/bin/Release/NanoAgent.VS.vsix
```

### CI and CD

The CI workflow `.github/workflows/visual-studio-extension-ci.yml` builds the extension on `windows-2022` with MSBuild from the installed Visual Studio toolchain, disables experimental-instance deployment with `/p:DeployExtension=false`, and uploads the built `.vsix` as a workflow artifact.

The CD workflow `.github/workflows/visual-studio-extension-cd.yml` packages and publishes the extension for `v*` tags and manual dispatch. It resolves the version from the tag or workflow input, updates `NanoAgent.VS/source.extension.vsixmanifest`, builds `NanoAgent.VS-<version>.vsix`, uploads that artifact, and publishes through `VsixPublisher.exe` to the Visual Studio Marketplace.

Required repository secret:

```text
VS_MARKETPLACE_PAT
```

Optional repository variables:

```text
VS_MARKETPLACE_PUBLISHER
VS_MARKETPLACE_EXTENSION_NAME
```

If the optional variables are unset, the workflow defaults to publisher `rizwan3d` and extension internal name `nanoagent-vs`. The publish job uses the `visual-studio-marketplace` GitHub environment so approval or environment protection rules can be applied separately from the VS Code marketplace flow.

## ACP Editor Integration

NanoAgent can run as an Agent Client Protocol server:

```bash
nanoai --acp
```

ACP mode speaks line-delimited JSON-RPC on stdin/stdout, so compatible editors and tools can create NanoAgent sessions, send prompts, cancel active turns, and receive assistant message, plan, and tool progress updates.

ACP does not open a network listener. It communicates only over the local child process stdin/stdout streams created by the host editor or tool.

Example editor server configuration:

```json
{
  "agent_servers": {
    "NanoAgent": {
      "command": "nanoai",
      "args": ["--acp"]
    }
  }
}
```

To require ACP authentication, set a process-level token with either `NANOAGENT_ACP_AUTH_TOKEN` or a workspace profile:

```json
{
  "Application": {
    "Acp": {
      "authenticationToken": "replace-with-a-long-random-token"
    }
  }
}
```

When an ACP authentication token is configured, `initialize` returns `"authMethods": ["token"]`. The client must then call `authenticate` with `{"token":"..."}` before sending `session/new`, `session/load`, `session/prompt`, or `session/close`. If no token is configured, `authMethods` is empty and `authenticate` is rejected instead of returning a misleading success response.

Run `nanoai` once before ACP use so provider onboarding, credentials, and the default model are already configured. ACP mode currently supports one active NanoAgent session per process. It merges ACP client `mcpServers` with NanoAgent's user and workspace MCP configuration for that ACP session only, so editor-provided MCP tools do not become global configuration.

## Review Automation

NanoAgent includes copy-paste CI examples for GitHub, GitLab, and Bitbucket. Each example installs NanoAI from the latest release using the same curl installer command shown in the CLI install section, computes the pull request or merge request diff, runs the workspace `pr-reviewer` profile in read-only mode, stores review artifacts, and posts a top-level review comment when platform credentials are configured.

- Always copy `.nanoagent/agents/pr-reviewer.md` with the CI files so the review profile is available.
- GitHub: copy `.github/workflows/nanoai-review.yml` and `.github/nanoai-github-review.sh`.
- GitLab: copy `.gitlab-ci.yml` and `.gitlab/nanoai-gitlab-review.sh`.
- Bitbucket: copy `bitbucket-pipelines.yml` and `.bitbucket/nanoai-bitbucket-review.sh`.
- GitHub and GitLab draft pull requests are skipped.
- Review artifacts are uploaded from `artifacts/nanoai-review` and retained for 14 days.

Required repository secret:

```text
NANOAGENT_API_KEY
```

Platform posting credentials:

| Platform | Variable |
| --- | --- |
| GitHub Actions | Uses the built-in `GITHUB_TOKEN` through `GH_TOKEN`. |
| GitLab CI | `GITLAB_TOKEN` or `NANOAI_GITLAB_TOKEN` with permission to create merge request notes. |
| Bitbucket Pipelines | `BITBUCKET_ACCESS_TOKEN`, or `BITBUCKET_USERNAME` plus `BITBUCKET_APP_PASSWORD`. |

Optional repository variables:

| Variable | Default | Purpose |
| --- | --- | --- |
| `NANOAGENT_PROVIDER` | `openai` | `openai`, `openai-compatible`, `google-ai-studio`, `anthropic`, `anthropic-claude-account`, `github-copilot`, `openrouter`, `kilo-code`, `cerebras`, `groq`, `ollama`, or `ollama-cloud`. |
| `NANOAGENT_MODEL` | `gpt-5.4` | Preferred model id for the review run. |
| `NANOAGENT_BASE_URL` | empty | Required only when `NANOAGENT_PROVIDER` is `openai-compatible`. |
| `NANOAGENT_THINKING` | `off` | `on` or `off`. |
| `NANOAGENT_REASONING` | empty | Reasoning effort: `none`, `minimal`, `low`, `medium`, `high`, `xhigh`, or `max`. |

The GitHub workflow uses `pull_request_target` so it can comment with the repository token. It checks out the trusted base branch version of NanoAgent, fetches the PR head only to compute a diff, and runs the CLI from trusted code. GitLab and Bitbucket examples run in their native merge request or pull request pipeline contexts and post comments through their REST APIs.

## Codebase Indexing

NanoAgent includes a local codebase index for repository-wide discovery. The `codebase_index` tool can:

- `status`: show whether the index exists, when it was built, and whether files are new, changed, or deleted.
- `build`: refresh the index, reusing unchanged files and updating changed files incrementally.
- `search`: rank likely relevant files for a natural-language, symbol, path, or behavior query.
- `list`: show indexed file paths.

NanoAgent implements codebase indexing by computing and caching lightweight local embeddings for files, refreshing incrementally when the index is searched or rebuilt, and respecting ignore files such as `.gitignore`.

```text
.nanoagent/cache/codebase-index.json
```

The cache does not store full file contents. It stores per-file metadata such as path, length, hash, language, line count, symbols, and the local embedding vector used for ranking. Search snippets are read from current workspace files when results are returned.

Indexing respects `.gitignore`, `.nanoagent/.nanoignore`, and built-in exclusions for generated or local runtime directories such as `.git/`, `node_modules/`, `bin/`, `obj/`, `.nanoagent/cache/`, `.nanoagent/logs/`, and `.nanoagent/sessions/`.

Use `codebase_index` for broad discovery first, then use `file_read`, `text_search`, or `code_intelligence` to verify exact behavior before editing.

### Manual Index Updates

Use the `/index` REPL command to refresh the local codebase index from a terminal or desktop session:

```text
/index
/index update
/index status
/index rebuild
/index list
/index list 50
```

### Automatic Index Updates

NanoAgent can refresh the local codebase index automatically after each conversation turn completes, so the next prompt sees an up-to-date index. This runs after the assistant response and all tool calls finish, reuses unchanged files, and updates changed files incrementally. A failed refresh is logged and never fails the completed turn.

This is **disabled by default**. Enable it in user-level or workspace-level `.nanoagent/agent-profile.json`:

```json
{
  "codebaseIndex": {
    "autoUpdateAfterTask": true
  }
}
```

## Providers and Models

NanoAgent stores a provider profile locally and discovers models from that provider when possible.

Use the terminal F2 or `/models` picker, `/use <model>`, or the desktop model control to switch models. The active model is stored with the local configuration and section state. If a preferred model is unavailable, NanoAgent falls back to a discovered model when possible.

### Thinking Mode

NanoAgent supports simple thinking mode:

```text
/thinking on
/thinking off
```

### Reasoning Effort vs Thinking Mode

`thinking` controls whether NanoAgent enables provider reasoning behavior where supported and whether provider-approved reasoning summaries are shown in the UI.

`reasoningEffort` controls how much reasoning work NanoAgent asks the model to spend.

Examples:

- `/thinking on` shows supported reasoning output.
- `/thinking off` hides reasoning output.
- `/reasoning high` asks the provider for deeper reasoning.
- `/reasoning none` disables reasoning where the provider supports disabling it.

Supported normalized reasoning effort values:

```text
none
minimal
low
medium
high
xhigh
max
```

If thinking is on and no explicit reasoning effort is set, NanoAgent asks supported providers for their default reasoning depth, which is usually mapped to `medium`.

### Reasoning Controls

Use these commands from the terminal:

```text
/thinking on
/thinking off
/reasoning
/reasoning show
/reasoning low
/reasoning high
/reasoning none
```

The desktop app keeps the existing thinking toggle and also exposes a reasoning effort picker. Providers that do not honor explicit effort settings may continue with provider defaults.

### Provider Reasoning Mapping

| Provider | Request shape | Notes |
| --- | --- | --- |
| OpenCode Zen | `reasoning.effort` for Responses, `reasoningEffort` in OpenCode config | CamelCase in OpenCode config files; NanoAgent uses Responses-style payloads for Zen Responses models. |
| OpenAI | `reasoning_effort` for chat-completions style, `reasoning: { effort, summary }` for Responses-style | Raw reasoning stays hidden; NanoAgent only shows provider-approved summaries. |
| Anthropic Claude | `thinking` plus `output_config.effort`, or manual `budget_tokens` fallback | Adaptive thinking for Claude 4 families, manual budget fallback for older Claude models. |
| DeepSeek | `reasoning_content` in responses; optional `reasoning_effort` where supported | NanoAgent never replays prior `reasoning_content` back to DeepSeek. |
| Gemini | `thinkingConfig.thinkingLevel` or `thinkingConfig.thinkingBudget` | Gemini 3-style and Gemini 2.5-style models differ. |
| xAI Grok and other OpenAI-compatible providers | `reasoning_effort` or Responses-compatible `reasoning.effort` when supported | Capability depends on the upstream provider and model. |
| OpenRouter | Unified `reasoning` object | Supports effort mapping and safe replay of provider-approved reasoning metadata. |

NanoAgent keeps final answers separate from reasoning output. When thinking output is disabled, NanoAgent still shows the final answer and suppresses reasoning blocks.

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
Primary profiles (`build`, `plan`, and `review`) can use the `ask_question` tool to ask you a question and wait for your answer before continuing. It supports multiple-choice options, multi-select, and free-form text, and it works in the interactive terminal, one-shot CLI runs, and ACP editors that support permission or text prompts. In `plan` mode the agent uses it to clarify genuinely ambiguous requirements or choose between approaches before finalizing a plan. In a non-interactive run with no available user, the tool returns gracefully so the agent continues with its best judgment instead of failing the turn.

### Built-in Profile Prompt Overrides

Create one of these files to replace only that built-in profile's prompt for a workspace:

```text
.nanoagent/agents/build.md
.nanoagent/agents/plan.md
.nanoagent/agents/review.md
.nanoagent/agents/general.md
.nanoagent/agents/explore.md
```

NanoAgent reads the markdown body as the active profile prompt, redacts secret-looking values, and reloads it for conversation turns like `.nanoagent/SystemPrompt.md`. For built-in profile names, NanoAgent keeps the built-in mode, enabled tools, and permission behavior, so a custom `plan.md` prompt still stays read-only.

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
/allow bash "<command-pattern>"
/deny bash "<command-pattern>"
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
            "your-build-command",
            "your-test-command"
          ]
        },
        "deny": {
          "commands": [
            "dangerous-command-pattern",
            "network-installer-pattern"
          ]
        }
      }
    }
  }
}
```

`shell_safe` controls the mode applied to the command patterns you list under `shell.allow.commands`; NanoAgent does not ship a built-in shell command allow catalog.

The `network` shortcut applies to built-in `webfetch` tools, including `web_search` and `headless_browser`. `headless_browser` renders pages through an installed Chromium-family browser such as Microsoft Edge, Google Chrome, or Chromium.

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

Memory writes still require approval by default through the memory policy, even in workspaces that auto-approve general tools.

## Workspace Files

Run:

```text
/init
```

NanoAgent asks which starter files to add:

- `Recommended`: core config, ignores, repo memory templates, runtime folders, and inactive agent/skill templates.
- `Minimal`: core config, README, and ignore files only.
- `Custom`: asks for each optional group, including the advanced inactive `SystemPrompt.md.template`.

You can skip the picker with `/init recommended`, `/init minimal`, or `/init custom`.

The recommended preset creates:

```text
.nanoagent/
  agent-profile.json
  README.md
  .gitignore
  .nanoignore
  agents/
  skills/
  cache/
  memory/
    architecture.md
    conventions.md
    decisions.md
    known-issues.md
    test-strategy.md
    lessons.jsonl
  logs/
```

### `AGENTS.md`

Place `AGENTS.md` or `.agent/AGENTS.md` in the workspace for persistent project instructions. NanoAgent adds them to the model context after secret redaction.

### `.nanoagent/SystemPrompt-Append.md`

Create `.nanoagent/SystemPrompt-Append.md` when you want to append workspace-specific base rules to NanoAgent's configured default system prompt. This keeps the normal base behavior intact and adds your extra instructions before the active profile prompt, workspace instructions, skills, memory, and session state.

Use `AGENTS.md` for ordinary repository instructions. Use `SystemPrompt-Append.md` when you only need to layer a few durable workspace rules onto the default base behavior.

If both `SystemPrompt.md` and `SystemPrompt-Append.md` exist, `SystemPrompt.md` wins and the append file is ignored.

### `.nanoagent/SystemPrompt.md`

Create `.nanoagent/SystemPrompt.md` to replace NanoAgent's base system prompt for that workspace. NanoAgent always prepends its identity header before the custom file content, then appends the active profile prompt, workspace instructions, skills, memory, and session state as usual.

Use `AGENTS.md` for ordinary repository instructions. Use `SystemPrompt.md` only when the workspace needs a different base behavior than both the default prompt and the append-only option.

`/init custom` can create `.nanoagent/SystemPrompt.md.template` as an inactive starter. Edit and rename it to `SystemPrompt.md` only when you intentionally want the override.

Use `.nanoagent/agents/<profile>.md` when you want to replace the active profile prompt while keeping the same base system prompt. Built-in profile names are `build`, `plan`, `review`, `general`, and `explore`.

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
.nanoagent/cache/
.nanoagent/logs/
.nanoagent/memory/*.jsonl
```

## Team Memory

NanoAgent stores structured team memory as ordinary markdown files:

```text
.nanoagent/memory/
  architecture.md
  conventions.md
  decisions.md
  known-issues.md
  test-strategy.md
```

These files are repo-scoped memory that your team can inspect, diff, and version-control. That is much safer than hidden memory because every durable note can go through normal code review and repository history.

NanoAgent loads non-empty team memory files into the model context as durable project context, skipping untouched scaffold templates. Treat them as starting context, then verify against current files and fresh tool output when correctness matters.

Use the `repo_memory` tool to list, read, or update these documents. Writes require memory approval by default and are blocked in read-only profiles, planning phase, and read-only sandbox mode. Direct writes to `.nanoagent/memory/*` through file editing tools also receive the `memory_write` permission tag so they cannot silently bypass memory approval.

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
toolOutput: full
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

The optional `toolOutput` key sets the default rendering for tool results while the profile is active: `full`/`complete` prints the complete output and `compact`/`preview` prints the capped preview. Omit it to fall back to the `Application.Tools.toolOutput` default in `agent-profile.json` (or the compact default if that is also unset). `/tooloutput` overrides this for the current session, and `/tooloutput auto` reverts to the profile or configured default. (The legacy `fileOutput` key is still accepted as an alias.)

If front matter is omitted, NanoAgent derives the name from the file name and uses conservative defaults.

If a workspace agent file uses a built-in profile name such as `build` or `review`, NanoAgent treats it as a prompt override for that built-in profile rather than adding a duplicate profile. The markdown body is customizable, but the built-in profile's mode, tool set, and permission behavior are preserved.

## MCP Servers

NanoAgent can load MCP servers from user-level and workspace-level `agent-profile.json` files. ACP clients can also supply session-scoped `mcpServers`; those entries are merged after user and workspace config and are visible in `/mcp` only for that ACP session.

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

## Code Intelligence

`code_intelligence` now discovers language servers from built-in definitions, the current workspace, and optional user or workspace profile overrides.

- Run `code_intelligence` with `action: "servers_status"` to inspect supported languages, detected servers, missing servers, cached health, and install hints.
- In the interactive CLI, use `/lsp` for the same registry view. Use `/lsp refresh` to bypass cached detection, or `/lsp file <path>` to inspect candidates for one file.
- Built-in detection checks workspace-local bins such as `node_modules/.bin` and common Python virtualenv script folders before falling back to `PATH`.
- Server selection is deterministic: higher `priority` wins, then NanoAgent falls back through remaining detected servers in stable key order.
- Rename stays preview-only. Code-intelligence actions remain read-only.

Example status request:

```json
{
  "action": "servers_status",
  "refresh": true
}
```

Profile overrides live in user-level or workspace-level `.nanoagent/agent-profile.json` under `languageServers`.

Example override:

```json
{
  "languageServers": {
    "python-pyright": {
      "language": "Python",
      "name": "Pyright",
      "command": ".nanoagent/tools/pyright-langserver.cmd",
      "args": ["--stdio"],
      "languageId": "python",
      "fileExtensions": [".py"],
      "priority": 250
    }
  }
}
```

Supported `languageServers` fields:

- `command`
- `args`
- `enabled`
- `fileExtensions`
- `initializationOptions`
- `installHint`
- `language`
- `languageId`
- `name`
- `priority`

Setup examples:

- TypeScript/JavaScript:
  Install `vtsls` or `typescript-language-server`.
  Example: `npm install -g @vtsls/language-server typescript`
- Python:
  Install `basedpyright-langserver`, `pyright-langserver`, or `pylsp`.
  Examples: `pip install basedpyright` or `pip install python-lsp-server`
- C#:
  Install `csharp-ls`.
  Example: `dotnet tool install --global csharp-ls`
- Rust:
  Install `rust-analyzer`.
  Example: `rustup component add rust-analyzer`
- Go:
  Install `gopls`.
  Example: `go install golang.org/x/tools/gopls@latest`
- C/C++:
  Install `clangd`.
  Example: use your platform package manager or LLVM distribution so `clangd` is on `PATH`

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

### Team Memory Files

Team memory is stored in reviewable markdown files under `.nanoagent/memory/`:

- `architecture.md`: major components, boundaries, data flow, and integration points.
- `conventions.md`: coding, naming, formatting, review, and workflow conventions.
- `decisions.md`: durable technical decisions, context, and consequences.
- `known-issues.md`: known bugs, limitations, risky areas, and workarounds.
- `test-strategy.md`: expected test layers, important commands, and validation guidance.

These files are intended to be committed with the repository when the team wants shared agent context. Memory writes require approval by default.

### Lesson Memory

NanoAgent stores reusable workspace lessons in:

```text
.nanoagent/memory/lessons.jsonl
```

Lessons help NanoAgent avoid repeating local mistakes. When lesson memory is enabled for a workspace, NanoAgent can inject relevant lessons into prompts automatically, and automatic tool-failure observation can turn repeated failures and their later fixes into reusable lessons. Memory is local, redacted by default, and write operations require approval unless policy is changed.

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
- Codebase index cache is stored locally.
- Team memory and lesson memory are stored locally.
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

### Claude Pro/Max sign-in does not complete

Check that port `53692` is available and that the browser callback URL opens locally. Sign-in requires network access and a valid Claude Pro or Max account.

### GitHub Copilot sign-in does not complete

Check that the device-code page opened, enter the displayed code, and verify that your GitHub account has Copilot access. For GitHub Enterprise, enter only the Enterprise URL or domain when prompted; leave it blank for `github.com`.

### No models are listed

Check the provider credential, provider account access, network connectivity, and custom provider base URL. For compatible providers, the base URL must be absolute and use HTTP or HTTPS.

For Ollama, make sure `ollama serve` is running and at least one model is installed. For LM Studio, make sure the local server is started, at least one model is loaded, and the API key matches your LM Studio server settings. For Ollama Cloud, check that the API key has access to the hosted models you expect to use.

### A command is denied

Run `/permissions` and `/rules` to see active policy. You can approve the prompt, add a session override with `/allow`, or update configuration.

### Shell sandboxing fails on Windows

Foreground shell commands in `read-only` and `workspace-write` modes use the Windows sandbox runner. If a restricted command still fails, inspect `%APPDATA%\NanoAgent\.sandbox\sandbox.log`, rerun the Windows sandbox setup if prompted, and verify the working directory still exists.

Restricted pseudo-terminal sessions and restricted background terminals are not wired to the Windows sandbox runner yet. Those requests fail closed; rerun without `pty`, use a foreground command, or approve sandbox escalation only when you trust the command.

### The agent cannot read a file

Check that the path is inside the workspace and not excluded by `.nanoagent/.nanoignore` or default secret-protection rules.

### Undo did not revert a shell side effect

Undo/redo only covers tracked file edit transactions. It does not revert arbitrary shell command side effects, package installs, generated files, external tools, or network actions.

## Build From Source

Requirements:

- .NET SDK compatible with `net10.0`.
- Node.js 20 or newer for the VS Code extension.
- Visual Studio 2022 or newer on Windows for `NanoAgent.VS`.
- Platform toolchains needed by your target desktop/CLI build.

Commands:

```bash
dotnet restore NanoAgent.CrossPlatform.slnx
dotnet build NanoAgent.CrossPlatform.slnx
dotnet test NanoAgent.Tests/NanoAgent.Tests.csproj
dotnet pack NanoAgent/NanoAgent.csproj -c Release
```

VS Code extension commands:

```bash
cd NanoAgent.VsCode
npm ci
npm run lint
npm run package
npm run package:vsix
```

Visual Studio extension command:

```powershell
msbuild NanoAgent.VS/NanoAgent.VS.csproj /restore /p:Configuration=Release /p:DeployExtension=false
```

The main projects are:

| Project | Purpose |
| --- | --- |
| `NanoAgent` | Core application, domain, infrastructure, tools, providers, storage. |
| `NanoAgent.CLI` | Terminal UI and one-shot CLI. |
| `NanoAgent.Desktop` | Desktop app. |
| `NanoAgent.VS` | Visual Studio extension that hosts NanoAgent inside a Visual Studio tool window. |
| `NanoAgent.VsCode` | VS Code extension that drives NanoAgent through ACP mode. |
| `NanoAgent.Tests` | Test suite. |

## License

NanoAgent is licensed under the Apache License 2.0. See [../LICENSE](../LICENSE).

