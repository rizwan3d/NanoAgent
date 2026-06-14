# NanoAgent coding-agent guide

These instructions are for AI coding agents and automation tools working in this repository.

## Project orientation

NanoAgent is a .NET-based local coding agent with multiple user surfaces:

- `NanoAgent/`: core application, domain, infrastructure, tools, providers, storage, and workspace services.
- `NanoAgent.CLI/`: terminal UI, one-shot CLI, and bridge code.
- `NanoAgent.Desktop/`: desktop app.
- `NanoAgent.VsCode/`: VS Code extension that drives NanoAgent through ACP mode.
- `NanoAgent.VS/`: Visual Studio extension.
- `NanoAgent.Tests/`: xUnit test suite.
- `docs/documentation.md`: technical handbook for installation, workflows, permissions, providers, workspace files, and source builds.
- `benchmarks/`: task-based benchmark suites and result summaries.

Prefer reading the nearest existing implementation and tests before changing behavior. This project values explicit user control, local-first operation, reviewable file edits, and safe permission boundaries.

## Work style

1. Start by identifying the product surface affected: core library, CLI, desktop, VS Code, Visual Studio, docs, benchmarks, or release automation.
2. Keep changes narrowly scoped and easy to review.
3. Preserve existing public behavior unless the task explicitly asks for a behavior change.
4. When changing agent behavior, permission handling, secret redaction, memory, tool execution, patch application, onboarding, providers, or CI review automation, add or update tests.
5. Do not silently broaden shell, network, file-edit, MCP, memory-write, or elevated-operation permissions.
6. Avoid logging prompts, source code, file paths, repository names, API keys, terminal output, OAuth tokens, or other secrets.
7. Treat generated files, local runtime data, package output, coverage output, logs, and credentials as non-source artifacts unless the task explicitly targets them.

## Build and validation commands

Use the commands below from the repository root unless a task gives a more specific command.

```bash
dotnet restore NanoAgent.CrossPlatform.slnx
dotnet build NanoAgent.CrossPlatform.slnx
dotnet test NanoAgent.Tests/NanoAgent.Tests.csproj
```

For release-like local validation, use Release configuration:

```bash
dotnet restore NanoAgent.CrossPlatform.slnx --locked-mode
dotnet build NanoAgent.CrossPlatform.slnx --configuration Release --no-restore
dotnet test NanoAgent.Tests/NanoAgent.Tests.csproj --configuration Release --no-restore
```

For the VS Code extension:

```bash
cd NanoAgent.VsCode
npm ci
npm run lint
npm run package
```

Only run packaging, installer, signing, publishing, or release workflows when the task explicitly requires it.

## Test guidance

- Prefer focused tests first, then broader solution-level validation when practical.
- Match the existing xUnit, FluentAssertions, and Moq style used in `NanoAgent.Tests/`.
- For file-system behavior, use isolated temporary workspaces and assert final file content.
- For security-sensitive behavior, include negative tests that prove secrets, denied operations, or read-only constraints are not bypassed.
- For prompt, profile, custom agent, skill, or memory behavior, cover precedence and fallback behavior.

## Coding conventions

- Follow the style of neighboring files before introducing new patterns.
- Keep C# nullable-safety expectations intact.
- Prefer explicit, readable names over abbreviations.
- Use async APIs when touching I/O.
- Pass through `CancellationToken` where the surrounding code does.
- Keep UI strings concise and user-actionable.
- Do not add third-party dependencies unless there is a clear reason and the package lock files are updated intentionally.

## Documentation updates

Update docs when changing user-visible behavior, setup, commands, configuration, permissions, providers, workspace files, or release/install behavior.

Useful targets:

- `README.md` for product overview and quick-start changes.
- `docs/documentation.md` for detailed behavior and configuration.
- `.github/workflows/` docs or comments only when changing CI/release behavior.
- `benchmarks/results/latest.md` only when intentionally refreshing benchmark outputs.

## Pull request expectations

Before opening a PR, summarize:

- What changed.
- Why it changed.
- How it was validated.
- Any tests or checks that were not run.
- Any security, telemetry, permission, or compatibility impact.

Prefer a small PR over a broad rewrite. If the task uncovers unrelated bugs, mention them separately instead of fixing them opportunistically.
