<p align="center">
  <img src=".github/nano.gif" alt="NanoAgent" width="800">
</p>

<h1 align="center">NanoAgent</h1>

<p align="center">
  Your local AI coding agent for desktop, terminal, and editor workflows.
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
    <img src="https://img.shields.io/badge/View-Releases-0969da?style=for-the-badge" alt="View NanoAgent releases">
  </a>
  <a href="https://marketplace.visualstudio.com/items?itemName=rizwan3d.nanoagent">
    <img src="https://img.shields.io/badge/Install-VSCode-0969da?style=for-the-badge" alt="Install NanoAgent VSCode">
  </a>
  <a href="#cli-install">
    <img src="https://img.shields.io/badge/Install-CLI-0969da?style=for-the-badge" alt="Install NanoAgent CLI">
  </a>
  <a href="#desktop-app">
    <img src="https://img.shields.io/badge/Install-Desktop-0969da?style=for-the-badge" alt="Install NanoAgent Desktop">
  </a>
  <a href="https://marketplace.visualstudio.com/items?itemName=rizwan3d.nanoagent-vs">
    <img src="https://img.shields.io/badge/Install-VS-0969da?style=for-the-badge" alt="Install NanoAgent VS">
  </a>
  <a href="https://www.nuget.org/packages/NanoAgent/">
    <img src="https://img.shields.io/badge/Install-Nuget-0969da?style=for-the-badge" alt="Install NanoAgent Nuget">
  </a>
</p>

---

NanoAgent gives developers an AI teammate that can work inside a real repository while keeping the human in control. Ask it to understand a codebase, plan a change, edit files, run validation, review a diff, or automate a pull request review from the same toolchain you already use.

It is built for practical engineering work: local projects, real shells, version-controlled memory, reviewable changes, and explicit approval for sensitive actions.

## What You Can Do

- Understand unfamiliar code faster with repository-aware search, summaries, and focused file inspection.
- Turn feature requests and bug reports into planned, editable, testable changes.
- Run code review with a findings-first workflow for local diffs, files, pull requests, and merge requests.
- Switch between hands-on implementation, read-only planning, and read-only review profiles.
- Keep team knowledge in reviewable `.nanoagent/memory` files instead of hidden agent notes.
- Save repeatable slash prompts in `.nanoagent/commands` and run them from the terminal, desktop app, or editor.
- Use the provider that fits your budget and policy, from API-key providers to subscription sign-in and local Ollama.
- Bring the same agent into desktop, terminal, VS Code, ACP-compatible editors, and CI review automation.

## Product Surfaces

### Desktop App

Use the desktop app for a visual workspace with chat, model controls, profile switching, budget controls, permission prompts, activity output, and undo/redo for tracked file edits.

### Terminal

Use `nanoai` when you want a keyboard-first agent for interactive work, one-shot prompts, piped input, quick reviews, and automation-friendly output.

### VS Code

Use the VS Code extension to chat with NanoAgent, send selections, review files, review Git diffs, and apply suggested changes from inside the editor.

### Visual Studio

Use the Visual Studio extension to keep a NanoAgent tool window inside Visual Studio while driving the local NanoAgent CLI over ACP.

### CI Review Automation

Use the included GitHub Actions, GitLab CI, and Bitbucket Pipelines examples to run NanoAgent against pull request and merge request diffs, then post review comments back to your platform.

## Built For Control

NanoAgent is designed for useful automation without silent surprises.

- Profiles separate implementation, planning, review, exploration, and delegated work.
- Permission rules decide what can run automatically, what asks first, and what is denied.
- Sensitive actions can require approval, including file edits, shell commands, network access, MCP tools, memory writes, and elevated operations.
- Tracked file edits can be undone and redone.
- Secret-looking values are redacted before logs, memory, audit records, and displayed tool output.
- Your workspace stays local; prompts and selected context are sent only to the model provider you configure when needed for a request.

## Benchmarks

NanoAgent includes task-based benchmarks in [`benchmarks/`](benchmarks) so we can measure real coding-agent behavior, not just chat-style answers. The benchmark suites currently cover bug fixing, repository understanding, patch quality, security review, tool safety, and a regression bundle that reruns the same tasks over time.

Tasks are defined in [`benchmarks/tasks/`](benchmarks/tasks) and grouped by [`benchmarks/manifest.json`](benchmarks/manifest.json). Each task can score the agent with response checks, validation commands, and diff constraints against small local fixtures or this repository itself.

Run the full benchmark set locally with:

```bash
python benchmarks/scripts/run_benchmarks.py --all --system --skip-preflight
```

Run the regression-only suite with:

```bash
python benchmarks/scripts/run_benchmarks.py --suite regression --system --skip-preflight
```

Generated summaries are refreshed in [`benchmarks/results/latest.md`](benchmarks/results/latest.md) and [`benchmarks/results/latest.json`](benchmarks/results/latest.json).

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

Useful PostHog queries:
- Unique users: `unique persons` on `nanoagent app started` or `nanoagent feature used`.
- CI runs: event count for `nanoagent app started` filtered to `execution_environment = ci`, optionally broken down by `ci_provider` or `app_surface`.
- Active users: DAU/WAU/MAU on `nanoagent app started` or `nanoagent feature used`, optionally filtered by `app_surface`.

## Provider Choice

NanoAgent supports OpenAI, ChatGPT Plus/Pro sign-in, Anthropic Claude Pro/Max sign-in, GitHub Copilot sign-in, OpenRouter, Kilo Code, Cerebras, Groq, Anthropic, Google AI Studio, Ollama, LM Studio, Ollama Cloud, and OpenAI-compatible providers.

## Get Started

Download the latest desktop build from [GitHub Releases](https://github.com/rizwan3d/NanoAgent/releases/latest), or install the CLI:

Release assets also publish `SHA256SUMS` and GitHub artifact attestations so you can verify both checksums and build provenance.

### Desktop App

| Platform | Architecture | Download|
|---|---:|---|
| Windows | x64 | [Setup `.exe`](https://github.com/rizwan3d/NanoAgent/releases/latest/download/NanoAgent.Desktop-win-x64-setup.exe) |
| Windows | x64 | [Portable `.zip`](https://github.com/rizwan3d/NanoAgent/releases/latest/download/NanoAgent.Desktop-win-x64.zip) |
| macOS | Apple Silicon / arm64 | [Download `.zip`](https://github.com/rizwan3d/NanoAgent/releases/latest/download/NanoAgent.Desktop-osx-arm64.zip) |
| macOS | Intel / x64 | [Download `.zip`](https://github.com/rizwan3d/NanoAgent/releases/latest/download/NanoAgent.Desktop-osx-x64.zip) |
| Linux | x64 | [Download `.zip`](https://github.com/rizwan3d/NanoAgent/releases/latest/download/NanoAgent.Desktop-linux-x64.zip) |
| Linux | arm64 | [Download `.zip`](https://github.com/rizwan3d/NanoAgent/releases/latest/download/NanoAgent.Desktop-linux-arm64.zip) |

### CLI Install

NuGet / .NET tool:

```bash
dotnet tool install --global NanoAgent.CLI
```

Direct release installer:

macOS / Linux:

```bash
curl -fsSL https://raw.githubusercontent.com/rizwan3d/NanoAgent/master/scripts/install.sh | bash
```

Windows PowerShell:

```powershell
irm https://raw.githubusercontent.com/rizwan3d/NanoAgent/master/scripts/install.ps1 | iex
```

Then start NanoAgent:

```bash
nanoai
```

The tag-based release workflows publish only `NanoAgent` to NuGet.org.

## Documentation

The technical guide lives in [docs/documentation.md](docs/documentation.md). It covers installation details, first-run onboarding, desktop and terminal workflows, VS Code and Visual Studio setup, ACP integration, CI review automation, codebase indexing, providers, permissions, MCP, memory, hooks, troubleshooting, release automation, and source builds.

## License

Apache License 2.0. See [LICENSE.txt](LICENSE.txt).

---

<p align="center">
  Sponsored by<br>
  <a href="https://alfain.co/"><img src="https://alfain.co/assets/images/logo-alfain.png" width="100" alt="ALFAIN Technologies (PVT) Limited"></a>
</p>
