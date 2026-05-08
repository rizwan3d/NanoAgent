# NanoAgent for VS Code

NanoAgent brings the NanoAgent coding assistant into Visual Studio Code. The extension starts the local `nanoai --acp` server, opens a chat view in the auxiliary bar, and sends editor context to NanoAgent when you ask it to explain, review, or plan code changes.

## Requirements

- Visual Studio Code 1.80.0 or newer.
- NanoAgent CLI installed and available as `nanoai`.
- A completed first run of `nanoai` so provider credentials, model selection, and onboarding are ready before the extension starts ACP mode.

Install the CLI first if `nanoai` is not on your `PATH`:

```bash
curl -fsSL https://raw.githubusercontent.com/rizwan3d/NanoAgent/master/scripts/install.sh | bash
```

Windows PowerShell:

```powershell
irm https://raw.githubusercontent.com/rizwan3d/NanoAgent/master/scripts/install.ps1 | iex
```

## Features

- Open the NanoAgent chat view from the NanoAgent auxiliary bar container or with `NanoAgent: Open Chat`.
- Start, stop, and restart the local NanoAgent ACP process from the Command Palette.
- Send the current selection or full current file as chat context.
- Ask NanoAgent to explain a selection, review the current file, or review the current Git diff.
- Prefill a planning prompt for code changes.
- Apply suggested changes from the previous NanoAgent response.
- Open NanoAgent extension logs and settings from VS Code commands.

## Settings

| Setting | Default | Description |
| --- | --- | --- |
| `nanoagent.command` | `nanoai` | Command used to start NanoAgent. |
| `nanoagent.args` | `["--acp"]` | Arguments passed to the NanoAgent CLI. |
| `nanoagent.workingDirectory` | workspace root | Working directory for the NanoAgent process. |
| `nanoagent.autoStart` | `false` | Start NanoAgent automatically when VS Code starts. |
| `nanoagent.logLevel` | `info` | Extension log level. |

## Local Development

```bash
cd NanoAgent.VsCode
npm ci
npm run lint
npm run package
npm run package:vsix
```

The `package:vsix` script creates an installable `.vsix` package with `@vscode/vsce`.

## Publishing

Marketplace publishing is handled by the repository workflow `.github/workflows/vscode-extension-cd.yml`. Configure the repository secret `VSCE_PAT` with an Azure DevOps Marketplace Personal Access Token that has Marketplace Manage scope for the `rizwan3d` publisher.

The workflow packages a `.vsix` artifact and publishes it on `v*` tags or manual dispatch. For tag builds, the extension version is synchronized from the tag name after removing the leading `v`.

## License

Apache License 2.0. See `LICENSE.txt`.
