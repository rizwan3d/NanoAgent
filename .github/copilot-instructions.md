# GitHub Copilot instructions for NanoAgent

Follow the repository-wide instructions in [`AGENTS.md`](../AGENTS.md).

## High-value context

NanoAgent is a local-first AI coding agent. Changes should preserve human control, explicit permission prompts, reviewable file edits, and safe defaults.

Key project areas:

- Core agent behavior: `NanoAgent/`
- CLI behavior: `NanoAgent.CLI/`
- Desktop app: `NanoAgent.Desktop/`
- VS Code extension: `NanoAgent.VsCode/`
- Visual Studio extension: `NanoAgent.VS/`
- Tests: `NanoAgent.Tests/`
- Full docs: `docs/documentation.md`

## Validation defaults

Use focused validation for the area touched. The common repository checks are:

```bash
dotnet restore NanoAgent.CrossPlatform.slnx
dotnet build NanoAgent.CrossPlatform.slnx
dotnet test NanoAgent.Tests/NanoAgent.Tests.csproj
```

For VS Code extension changes:

```bash
cd NanoAgent.VsCode
npm ci
npm run lint
npm run package
```

Mention any checks that were not run.

## Safety reminders

- Do not broaden file, shell, network, MCP, memory-write, or elevated permissions without explicit intent.
- Do not add logging for prompts, code, file paths, repository names, credentials, provider tokens, terminal output, or secrets.
- Keep provider credentials, OAuth flows, telemetry, release signing, installer scripts, and CI publishing changes especially narrow and tested.
- Prefer tests for permission, memory, patch, provider, and tool-execution changes.
