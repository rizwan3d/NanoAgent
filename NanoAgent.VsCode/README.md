# NanoAgent for VS Code

NanoAgent for VS Code brings NanoAgent chat, code review, planning, and editor context into Visual Studio Code.

The extension does not bundle the NanoAgent engine. It starts the local terminal command `nanoai --acp`, so the NanoAgent CLI must be installed and configured before the VS Code extension can work.

## Requirements

- Visual Studio Code 1.80.0 or newer.
- NanoAgent CLI installed and available as `nanoai` in your terminal.
- A completed first run of `nanoai` so provider credentials, model selection, and onboarding are ready.

## Install NanoAgent CLI First

macOS / Linux:

```bash
curl -fsSL https://raw.githubusercontent.com/rizwan3d/NanoAgent/master/scripts/install.sh | bash
```

Windows PowerShell:

```powershell
irm https://raw.githubusercontent.com/rizwan3d/NanoAgent/master/scripts/install.ps1 | iex
```

After installation, restart your terminal if `nanoai` is not found, then run:

```bash
nanoai
```

Finish provider setup in the terminal before using the VS Code extension.

## Install The Extension

Install from the Visual Studio Marketplace:

```text
ext install rizwan3d.nanoagent
```

Then open the NanoAgent view in VS Code or run `NanoAgent: Open Chat` from the Command Palette.

## Features

- Open NanoAgent chat inside the VS Code auxiliary bar.
- Send the current selection or full file as context.
- Ask NanoAgent to explain selected code.
- Review the current file or current Git diff.
- Prefill planning prompts for code changes.
- Apply suggested changes from a previous NanoAgent response.
- Start, stop, and restart the local `nanoai --acp` process.
- Open extension logs and settings from VS Code commands.

## Settings

| Setting | Default | Description |
| --- | --- | --- |
| `nanoagent.command` | `nanoai` | Command used to start NanoAgent. |
| `nanoagent.args` | `["--acp"]` | Arguments passed to the NanoAgent CLI. |
| `nanoagent.workingDirectory` | workspace root | Working directory for the NanoAgent process. |
| `nanoagent.autoStart` | `false` | Start NanoAgent automatically when VS Code starts. |
| `nanoagent.logLevel` | `info` | Extension log level. |

## Troubleshooting

### `nanoai` is not found

Install the NanoAgent CLI, restart your terminal, and make sure `nanoai` is available on `PATH`.

### The extension starts but cannot connect

Run `nanoai` once in a terminal and finish provider onboarding. The extension starts NanoAgent in ACP mode and expects local configuration to already exist.

## License

Apache License 2.0. See `LICENSE.txt`.
