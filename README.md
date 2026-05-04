<p align="center">
  <img src=".github/nano.gif" alt="NanoAgent" width="800">
</p>

<h1 align="center">NanoAgent</h1>

<p align="center">
  An AI coding agent for desktop and terminal workflows.
</p>

<p align="center">
  <a href="https://github.com/rizwan3d/NanoAgent/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/rizwan3d/NanoAgent/ci.yml?branch=master&amp;label=build" alt="Build"></a>
  <a href="https://github.com/rizwan3d/NanoAgent/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/rizwan3d/NanoAgent/ci.yml?branch=master&amp;label=tests" alt="Tests"></a>
  <a href="https://github.com/rizwan3d/NanoAgent/actions/workflows/release.yml"><img src="https://img.shields.io/github/actions/workflow/status/rizwan3d/NanoAgent/release.yml?label=release" alt="Release"></a>
  <a href="https://github.com/rizwan3d/NanoAgent/blob/master/LICENSE.txt"><img src="https://img.shields.io/github/license/rizwan3d/NanoAgent" alt="License"></a>
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

NanoAgent brings an AI teammate into your everyday engineering flow. Open a project, ask for help, and let the agent inspect code, explain behavior, make focused changes, run validation commands, and review work with clear permission boundaries.

It is designed for developers who want useful automation without giving up control. NanoAgent can move through real tasks, but sensitive actions such as edits, command execution, network access, memory writes, MCP tools, and elevated operations remain governed by profiles, permissions, and approval prompts.

## Why NanoAgent?

- Start where you already work, whether that is the desktop app, the `nanoai` terminal command, or an ACP-compatible editor.
- Turn everyday engineering requests into useful progress: feature work, bug fixes, planning, code review, and build/test loops.
- Keep control of the work with clear profiles for hands-on changes, read-only planning, and read-only review.
- Bring NanoAgent into team workflows with GitHub, GitLab, and Bitbucket PR/MR review automation.
- Use the model provider that fits your budget, policy, and performance needs, including OpenAI, ChatGPT Plus/Pro sign-in, Anthropic Claude Pro/Max sign-in, OpenRouter, Anthropic, Google AI Studio, and OpenAI-compatible providers.
- Search across the repository with a local codebase index that refreshes incrementally and respects ignore rules.
- Give your team reviewable repo memory in `.nanoagent/memory/*.md` instead of relying on hidden agent notes.
- Adapt the agent to your project with custom instructions, skills, agents, process tools, MCP tools, and reusable lessons.
- Reduce repetitive work by delegating focused tasks to built-in or project-defined subagents.
- Protect your codebase with permission prompts, policy rules, secret redaction, and undo/redo for tracked file edits.

## Product Experience

### Desktop

Use the desktop app when you want a visual workspace with sections, model controls, budget controls, slash-command suggestions, permission prompts, activity output, and undo/redo close at hand.

### Terminal

Use `nanoai` when you want a keyboard-first workflow, one-shot prompts, piped input, or quick review and automation from the command line.

```bash
nanoai
nanoai "Summarize this repository"
echo "Review the latest changes for regressions" | nanoai --profile review
```

### Agent Client Protocol

Use `nanoai --acp` when you want an ACP-compatible editor or tool to drive NanoAgent over JSON-RPC stdio.

```bash
nanoai --acp
```

Example Zed-style server configuration:

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

ACP mode is useful because it lets NanoAgent plug into editor and workflow clients without each integration needing a custom NanoAgent API. It supports initialization, session creation/loading, prompt turns, cancellation, session close, and session updates for assistant messages, plans, and tool progress.

Run `nanoai` once first to finish provider onboarding. ACP mode uses NanoAgent's configured MCP servers; client-supplied `mcpServers` are not imported yet.

### CI Review Automation

The included GitHub Actions, GitLab CI, and Bitbucket Pipelines examples install NanoAI from the latest release, run the workspace `pr-reviewer` profile against the PR/MR diff, and post a review comment.

Copy `.nanoagent/agents/pr-reviewer.md` plus the matching CI files for your platform: `.github/workflows/nanoai-review.yml` and `.github/nanoai-github-review.sh`, `.gitlab-ci.yml` and `.gitlab/nanoai-gitlab-review.sh`, or `bitbucket-pipelines.yml` and `.bitbucket/nanoai-bitbucket-review.sh`.

Configure `NANOAGENT_API_KEY`. GitLab posting needs `GITLAB_TOKEN` or `NANOAI_GITLAB_TOKEN`; Bitbucket posting needs `BITBUCKET_ACCESS_TOKEN` or `BITBUCKET_USERNAME` plus `BITBUCKET_APP_PASSWORD`. Optional variables are `NANOAGENT_PROVIDER`, `NANOAGENT_MODEL`, `NANOAGENT_BASE_URL`, and `NANOAGENT_THINKING`.

### Codebase Indexing

NanoAgent can build a local codebase index in `.nanoagent/cache/codebase-index.json` and use it to find relevant files by concept, symbol, path, or behavior before narrowing with file reads, text search, and code intelligence.

The index refreshes incrementally, respects `.gitignore` and `.nanoagent/.nanoignore`, and exposes status plus included files through the `codebase_index` tool.

### Team Memory

NanoAgent keeps durable project knowledge in structured markdown files: `.nanoagent/memory/architecture.md`, `conventions.md`, `decisions.md`, `known-issues.md`, and `test-strategy.md`.

This is repo-scoped memory that your team can inspect, diff, and version-control. That is much safer than hidden memory, and memory writes require approval by default.

### Profiles

| Profile | Best for |
| --- | --- |
| `build` | Implementation, fixes, tests, and validation. |
| `plan` | Read-only investigation and implementation plans. |
| `review` | Read-only code review focused on bugs, regressions, and missing tests. |
| `general` | Bounded delegated implementation work. |
| `explore` | Fast read-only project investigation. |

Override built-in profile prompts per workspace with `.nanoagent/agents/build.md`, `plan.md`, `review.md`, `general.md`, or `explore.md`. NanoAgent uses the file body as the prompt while keeping the built-in tools and permissions.

### Providers

| Provider | Setup |
| --- | --- |
| OpenAI | API key |
| OpenAI ChatGPT Plus/Pro | Browser sign-in |
| Anthropic Claude Pro/Max | Browser sign-in |
| OpenRouter | API key |
| Google AI Studio | API key |
| Anthropic | API key |
| OpenAI-compatible provider | Base URL and API key |

## Install

### Desktop Downloads

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

## First Run

Start NanoAgent:

```bash
nanoai
```

NanoAgent will guide you through provider setup, model discovery, and the first section. After setup, you can switch models with the terminal F2 or `/models` picker, or switch profiles and thinking mode from the desktop controls or terminal commands.

For terminal onboarding, you can pass an API key up front:

```bash
nanoai --provider-auth-key <key>
```

## Common Commands

| Command | Purpose |
| --- | --- |
| `/help` | Show available commands. |
| `/budget [status\|local\|cloud]` | Show or configure budget controls. |
| `/config` | Show provider, model, section, profile, thinking mode, and config path. |
| `/models` | Choose the active model with the arrow-key picker. |
| `/use <model>` | Switch directly to a model id. |
| `/onboard` | Re-run provider onboarding and switch the active session to the new provider. |
| `/profile <name>` | Switch profile. |
| `/thinking [on\|off]` | Show or set thinking mode. |
| `/permissions` | Show permission policy summary. |
| `/rules` | Show effective rules. |
| `/allow <tool-or-tag> [pattern]` | Add a session allow override. |
| `/deny <tool-or-tag> [pattern]` | Add a session deny override. |
| `/mcp` | Show MCP servers, custom tool providers, and dynamic tools. |
| `/init [recommended\|minimal\|custom]` | Choose and create `.nanoagent` starter files for a project. |
| `/update [now]` | Check for updates, or install immediately with `/update now`. |
| `/undo` | Roll back the most recent tracked file edit transaction. |
| `/redo` | Re-apply the most recently undone edit transaction. |
| `/exit` | Exit the terminal UI. |

Press F2 in the terminal UI to choose the active model with the same arrow-key picker.
Type `/` in the terminal input to open command suggestions, then use Up/Down and Enter to choose a command.
Start input with `!` to run a local shell command directly, for example `!dotnet test`.

`/budget local` asks for monthly budget USD, alert threshold percent, and input, cached-input, and output prices per 1M tokens, then tracks usage in `.nanoagent/budget-controls.local.json`. `/budget cloud` asks for an API URL and auth key, fetches budget status with GET, and posts the token delta from each LLM call.

## Safety and Control

NanoAgent is built around explicit control:

- `build`, `plan`, and `review` profiles shape what the agent is allowed to do.
- Permission rules decide whether actions are allowed, denied, or require approval.
- Sensitive actions can prompt before they run.
- Session overrides let you allow or deny a tool pattern temporarily.
- Tracked file edits can be undone and redone.
- Secret-looking values are redacted before logs, memory, audit records, and displayed tool output.

Your code stays on your machine. Prompts, relevant snippets, tool output, and conversation context are sent to the model provider you choose when they are needed for a request.

## Learn More

The detailed user guide lives in [docs/documentation.md](docs/documentation.md). It covers onboarding, desktop and terminal workflows, codebase indexing, providers, models, permissions, MCP, memory, hooks, custom agents, troubleshooting, and source builds.

## License

Apache License 2.0. See [LICENSE.txt](LICENSE.txt).

---

<p align="center">
  Sponsored by<br>
  <a href="https://alfain.co/"><img src="https://alfain.co/assets/images/logo-alfain.png" width="100" alt="ALFAIN Technologies (PVT) Limited"></a>
</p>
