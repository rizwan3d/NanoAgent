<p align="center">
  <img src=".github/nano.gif" alt="NanoAgent" width="800">
</p>

<h1 align="center">NanoAgent</h1>

<p align="center">
  Local AI coding agent for desktop, terminal, editor, and CI workflows.
</p>

<p align="center">
  NanoAgent helps you understand a repository, plan a change, edit files, run validation, review diffs, and automate pull request feedback without giving up local control.
</p>

<p align="center">
  <a href="https://github.com/rizwan3d/NanoAgent/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/rizwan3d/NanoAgent/ci.yml?branch=master&amp;label=build" alt="Build"></a>
  <a href="https://github.com/rizwan3d/NanoAgent/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/rizwan3d/NanoAgent/ci.yml?branch=master&amp;label=tests" alt="Tests"></a>
  <a href="https://github.com/rizwan3d/NanoAgent/actions/workflows/release.yml"><img src="https://img.shields.io/github/actions/workflow/status/rizwan3d/NanoAgent/release.yml?label=release" alt="Release"></a>
  <a href="https://github.com/rizwan3d/NanoAgent/blob/master/LICENSE.txt"><img src="https://img.shields.io/github/license/rizwan3d/NanoAgent" alt="License"></a>
  <a href="https://github.com/rizwan3d/NanoAgent"><img src="https://img.shields.io/github/v/release/rizwan3d/NanoAgent" alt="Version"></a>
  <a href="https://github.com/rizwan3d/NanoAgent/stargazers"><img src="https://img.shields.io/github/stars/rizwan3d/NanoAgent" alt="Stars"></a>
  <a href="https://github.com/rizwan3d/NanoAgent/issues"><img src="https://img.shields.io/github/issues/rizwan3d/NanoAgent" alt="Issues"></a>
  <a href="https://github.com/rizwan3d/NanoAgent/releases"><img src="https://img.shields.io/github/downloads/rizwan3d/NanoAgent/total?label=downloads" alt="Downloads"></a>
  <a href="https://github.com/rizwan3d/NanoAgent/forks"><img src="https://img.shields.io/github/forks/rizwan3d/NanoAgent" alt="Forks"></a>
</p>

<p align="center">
  <a href="https://github.com/rizwan3d/NanoAgent/releases/latest">
    <img src="https://img.shields.io/badge/Get-Releases-0969da?style=for-the-badge" alt="Get NanoAgent releases">
  </a>
  <a href="#cli-install">
    <img src="https://img.shields.io/badge/Install-CLI-0969da?style=for-the-badge" alt="Install NanoAgent CLI">
  </a>
   <a href="#desktop-app">
    <img src="https://img.shields.io/badge/Install-Desktop-0969da?style=for-the-badge" alt="Install NanoAgent Desktop">
  </a>
  <a href="https://marketplace.visualstudio.com/items?itemName=rizwan3d.nanoagent">
    <img src="https://img.shields.io/badge/Install-VS_Code-0969da?style=for-the-badge" alt="Install NanoAgent VS Code extension">
  </a>
  <a href="https://marketplace.visualstudio.com/items?itemName=rizwan3d.nanoagent-vs">
    <img src="https://img.shields.io/badge/Install-Visual_Studio-0969da?style=for-the-badge" alt="Install NanoAgent Visual Studio extension">
  </a>
  <a href="https://www.npmjs.com/package/nanoai-cli">
    <img src="https://img.shields.io/badge/Install-npm-0969da?style=for-the-badge" alt="Install NanoAgent from npm">
  </a>
  <a href="https://www.nuget.org/packages/NanoAgent/">
    <img src="https://img.shields.io/badge/Install-Nuget-0969da?style=for-the-badge" alt="Install NanoAgent Nuget">
    <a href="docs/documentation.md">
    <img src="https://img.shields.io/badge/Read-Docs-0969da?style=for-the-badge" alt="Read NanoAgent documentation">
  </a>
</p>

---

NanoAgent is built for practical engineering work. It runs against a real local repository, uses real shells and tools, keeps workspace memory in versionable files, and asks for approval when an action should stay under human control.

Use it when you want one agent experience across:

- interactive implementation in a terminal
- desktop chat with activity, controls, and undo/redo
- VS Code and Visual Studio editor workflows
- ACP-compatible editor integrations
- CI review automation for pull requests and merge requests

## Table of Contents

- [Why NanoAgent](#why-nanoagent)
- [What You Can Do](#what-you-can-do)
- [Choose Your Surface](#choose-your-surface)
- [Get Started](#get-started)
- [Quick Start](#quick-start)
- [Providers](#providers)
- [Built For Control](#built-for-control)
- [Benchmarks](#benchmarks)
- [Telemetry](#telemetry)
- [Documentation](#documentation)
- [Contributing](#contributing)
- [Support](#support)
- [License](#license)

## Why NanoAgent

- Works inside a real repository instead of a detached chat sandbox.
- Keeps the human in control with approval prompts, permissions, and profiles.
- Reuses the same agent across desktop, CLI, IDE, and CI workflows.
- Stores reusable commands and team memory in `.nanoagent/` files you can review and commit.
- Supports both subscription-style sign-in and API-key or local-model setups.

## What You Can Do

- Understand unfamiliar code with repository-aware search, file inspection, and focused summaries.
- Use built-in LSP-powered code intelligence for symbols, definitions, references, diagnostics, and rename previews.
- Turn feature requests and bug reports into concrete implementation plans.
- Edit files, run checks, and iterate on a change without leaving your working tree.
- Review local diffs, files, pull requests, and merge requests with a findings-first workflow.
- Switch between implementation, planning, review, exploration, and delegated work profiles.
- Save repeatable prompts as slash commands in `.nanoagent/commands`.
- Keep long-lived project knowledge in `.nanoagent/memory` instead of hidden agent state.

## Choose Your Surface

| Surface | Best for |
| --- | --- |
| Desktop app | Visual workspace with chat, model controls, profile switching, activity output, permission prompts, and undo/redo for tracked edits. |
| `nanoai` CLI | Keyboard-first work, one-shot prompts, piped input, quick reviews, and automation-friendly output. |
| VS Code extension | Chat, selected-context prompts, file review, diff review, and applying suggestions without leaving the editor. |
| Visual Studio extension | Docked NanoAgent tool window powered by the local CLI over ACP. |
| CI automation | Running NanoAgent in GitHub Actions, GitLab CI, and Bitbucket Pipelines to review proposed changes automatically. |

## Get Started

Download the latest desktop build from [GitHub Releases](https://github.com/rizwan3d/NanoAgent/releases/latest), or install the CLI with the method that fits your environment.

Release assets publish `SHA256SUMS` and GitHub artifact attestations so you can verify both checksums and build provenance.

### Desktop App

| Platform | Architecture | Download |
| --- | --- | --- |
| Windows | x64 | [Setup `.exe`](https://github.com/rizwan3d/NanoAgent/releases/latest/download/NanoAgent.Desktop-win-x64-setup.exe) |
| Windows | x64 | [Portable `.zip`](https://github.com/rizwan3d/NanoAgent/releases/latest/download/NanoAgent.Desktop-win-x64.zip) |
| macOS | arm64 | [Download `.zip`](https://github.com/rizwan3d/NanoAgent/releases/latest/download/NanoAgent.Desktop-osx-arm64.zip) |
| macOS | x64 | [Download `.zip`](https://github.com/rizwan3d/NanoAgent/releases/latest/download/NanoAgent.Desktop-osx-x64.zip) |
| Linux | x64 | [Download `.zip`](https://github.com/rizwan3d/NanoAgent/releases/latest/download/NanoAgent.Desktop-linux-x64.zip) |
| Linux | arm64 | [Download `.zip`](https://github.com/rizwan3d/NanoAgent/releases/latest/download/NanoAgent.Desktop-linux-arm64.zip) |

### CLI Install

Every installer exposes the same `nanoai` command and downloads the same self-contained release binary.

#### Install script
Curl:

```bash
curl -fsSL https://raw.githubusercontent.com/rizwan3d/NanoAgent/master/scripts/install.sh | bash
```

Windows PowerShell:

```powershell
irm https://raw.githubusercontent.com/rizwan3d/NanoAgent/master/scripts/install.ps1 | iex
```

On Windows, both scripts install the same `nanoai` CLI. The Bash installer downloads the `win-x64` release, installs `nanoai.exe` into `%LOCALAPPDATA%\Programs\NanoAgent\bin` by default, and adds that directory to your user `PATH`.

#### npm / pnpm / bun

```bash
npm install -g nanoai-cli
# or
pnpm add -g nanoai-cli
# or
bun add -g nanoai-cli
```

The npm package downloads the matching release binary and verifies it against published `SHA256SUMS`. If `postinstall` is skipped or the download fails, the binary is fetched automatically the first time you run `nanoai`.

Start NanoAgent:

```bash
nanoai
```

The release workflow also publishes the `NanoAgent` library to [NuGet.org](https://www.nuget.org/packages/NanoAgent/).

## Quick Start

On first launch, NanoAgent walks you through provider setup. Choose a subscription account, an API-key provider, an OpenAI-compatible endpoint, or a local provider, then let NanoAgent discover the models that are available to that setup.

If you already know the provider configuration you want, you can preseed it before first run and skip interactive onboarding.

PowerShell:

```powershell
$env:NANOAGENT_PROVIDER="openrouter"
$env:NANOAGENT_MODEL="poolside/laguna-m.1:free"
$env:NANOAGENT_THINKING="on"
$env:NANOAGENT_API_KEY="PASTE_NEW_ROTATED_KEY_HERE"

nanoai -p "Say hello in one short line"
```

Bash:

```bash
export NANOAGENT_PROVIDER="openrouter"
export NANOAGENT_MODEL="poolside/laguna-m.1:free"
export NANOAGENT_THINKING="on"
export NANOAGENT_API_KEY="PASTE_NEW_ROTATED_KEY_HERE"

nanoai -p "Say hello in one short line"
```

Common ways to use NanoAgent:

```bash
# Start an interactive session in the current repository
nanoai

# Ask one question and print the result
nanoai "Find risky changes in this branch"

# Review piped input with the review profile
git diff --stat | nanoai --stdin --profile review

# Resume a previous session
nanoai --session <session-guid>
```

Inside a session, a few useful commands are:

| Command | What it does |
| --- | --- |
| `/help` | List commands and usage. |
| `/models` | Pick the active model. |
| `/profile <name>` | Switch profiles such as implementation, planning, and review. |
| `/permissions` | Review what runs automatically, asks first, or is denied. |
| `/init` | Scaffold workspace-local `.nanoagent` files. |
| `/undo` / `/redo` | Roll back or re-apply the most recent tracked edit. |

The interactive terminal also keeps you moving while NanoAgent works:

- Queue the next prompt or slash command with Enter while a turn is running; queued items run in order as soon as it finishes (F4 removes the newest).
- Press Esc to interrupt the current turn, or Esc again to abandon a stuck turn locally.
- Use Ctrl+A to select all input, and Tab to complete file and directory paths after a `!` or `!!` shell command.
- Scrolling up to read history pauses auto-scroll until you return to the bottom.

DeepSeek models get an automatic tool-argument repair pass so malformed tool calls still run. See the [documentation](docs/documentation.md#terminal-input-and-keys) for details.

## Providers

NanoAgent supports:

- OpenAI
- ChatGPT Plus/Pro sign-in
- Anthropic Claude Pro/Max sign-in
- GitHub Copilot sign-in
- OpenRouter
- Kilo Code
- Cerebras
- Groq
- DeepSeek
- Anthropic
- Google AI Studio
- Ollama
- LM Studio
- Ollama Cloud
- OpenAI-compatible providers

## Built For Control

NanoAgent is designed for useful automation without silent surprises.

- Profiles separate implementation, planning, review, exploration, and delegated work.
- Permission rules control what runs automatically, what asks first, and what is denied.
- Sensitive actions can require approval, including file edits, shell commands, network access, MCP tools, memory writes, and elevated operations.
- Tracked file edits can be undone and redone.
- Secret redaction is off by default; when enabled, secret-looking values are redacted before logs, memory, audit records, and displayed tool output.
- Your workspace stays local. Only the prompt and selected context needed for a request are sent to the provider you configure.

## Benchmarks

NanoAgent includes task-based benchmarks in [`benchmarks/`](benchmarks) so we can measure real coding-agent behavior, not just chat-style answers.

- Benchmark tasks live in [`benchmarks/tasks/`](benchmarks/tasks).
- Suites are grouped in [`benchmarks/manifest.json`](benchmarks/manifest.json).
- Results are refreshed in [`benchmarks/results/latest.md`](benchmarks/results/latest.md) and [`benchmarks/results/latest.json`](benchmarks/results/latest.json).

Run the full benchmark set:

```bash
python benchmarks/scripts/run_benchmarks.py --all --system --skip-preflight
```

Run the regression-only suite:

```bash
python benchmarks/scripts/run_benchmarks.py --suite regression --system --skip-preflight
```

## Telemetry

NanoAgent sends anonymous product analytics to PostHog using built-in US Cloud defaults in code. You can still override `Application:Telemetry:*` settings, and `/disableanalytics` writes `Application.Telemetry.Enabled=false` to `.nanoagent/agent-profile.json` for the current workspace.

Collected:

- NanoAgent version
- OS family
- app surface such as CLI, Desktop, VS Code, Visual Studio, JetBrains, GitHub Actions, GitLab CI, or Bitbucket Pipelines
- execution environment (`local` or `ci`)
- CI provider when detected (`github_actions`, `gitlab_ci`, `bitbucket_pipelines`, or generic CI)
- feature names used
- success and failure counts
- token and duration buckets
- daily runs and usage-time buckets

Never collected:

- prompts
- source code
- file paths
- repository names or URLs
- API keys
- terminal output

## Documentation

The technical guide lives in [docs/documentation.md](docs/documentation.md). It covers installation details, first-run onboarding, desktop and terminal workflows, VS Code and Visual Studio setup, ACP integration, CI review automation, LSP-powered code intelligence, codebase indexing, providers, permissions, MCP, memory, hooks, troubleshooting, release automation, and source builds.

## Contributing

Contributions are welcome. To work on NanoAgent from source:

1. Fork and clone the repository.
2. Restore, build, and run the CLI:

   ```bash
   dotnet restore NanoAgent.CrossPlatform.slnx
   dotnet build NanoAgent.CrossPlatform.slnx
   dotnet run --project NanoAgent.CLI/NanoAgent.CLI.csproj
   ```

3. Run the test suite before opening a pull request:

   ```bash
   dotnet test NanoAgent.Tests/NanoAgent.Tests.csproj
   ```

Open an [issue](https://github.com/rizwan3d/NanoAgent/issues) to report bugs or propose features, and keep pull requests focused with a clear description of the change. See [docs/documentation.md](docs/documentation.md#build-from-source) for full source-build details.

## Support

- Browse the [documentation](docs/documentation.md) for setup, workflows, and troubleshooting.
- Report bugs or request features via [GitHub Issues](https://github.com/rizwan3d/NanoAgent/issues).
- Find the latest builds on the [Releases](https://github.com/rizwan3d/NanoAgent/releases/latest) page.

## License

Apache License 2.0. See [LICENSE.txt](LICENSE.txt).

---

<p align="center">
  Sponsored by<br>
  <a href="https://alfain.co/"><img src="https://alfain.co/assets/images/logo-alfain.png" width="100" alt="ALFAIN Technologies (PVT) Limited"></a>
</p>
