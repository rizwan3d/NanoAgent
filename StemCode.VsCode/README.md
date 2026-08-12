# StemCode for VS Code

StemCode for VS Code brings StemCode chat, code review, planning, and editor context into Visual Studio Code.

The extension does not bundle the StemCode engine. It starts the local terminal command `stemcode --acp`, so the StemCode CLI must be installed and configured before the VS Code extension can work.

## Requirements

- Visual Studio Code 1.80.0 or newer.
- StemCode CLI installed and available as `stemcode` in your terminal.
- A completed first run of `stemcode` so provider credentials, model selection, and onboarding are ready.

## Install StemCode CLI First

macOS / Linux:

```bash
curl -fsSL https://raw.githubusercontent.com/rizwan3d/StemCode/master/scripts/install.sh | bash
```

Windows PowerShell:

```powershell
irm https://raw.githubusercontent.com/rizwan3d/StemCode/master/scripts/install.ps1 | iex
```

After installation, restart your terminal if `stemcode` is not found, then run:

```bash
stemcode
```

Finish provider setup in the terminal before using the VS Code extension.

## Install The Extension

Install from the Visual Studio Marketplace:

```text
ext install growbitlab.stemcode
```

Then open the StemCode view in VS Code or run `StemCode: Open Chat` from the Command Palette.

## Features

- Open StemCode chat inside the VS Code auxiliary bar.
- Send the current selection or full file as context.
- Ask StemCode to explain selected code.
- Review the current file or current Git diff.
- Prefill planning prompts for code changes.
- Apply suggested changes from a previous StemCode response.
- Browse, install, and remove data-only skills from a marketplace panel.
- Start, stop, and restart the local `stemcode --acp` process.
- Open extension logs and settings from VS Code commands.

## Skills

Open **Settings (gear) → Workspace → Skills** in the StemCode chat view to manage data-only skills without leaving VS Code:

- **Browse** a marketplace to list the skills it offers (read from the repo's `stemcode-marketplace.json` index; output shows in chat).
- **Install** a skill by id from a configured marketplace, optionally overwriting existing files.
- **Uninstall** an installed skill, removing only its tracked files.
- **Add** a marketplace by `owner/repo`, or **Remove** a configured one.

The panel reads state from `.stemcode/skills/marketplaces.json` and `.stemcode/skills/installed.json`, and runs the same `/skill` commands available in the CLI.

## Settings

| Setting | Default | Description |
| --- | --- | --- |
| `stemcode.command` | `stemcode` | Command used to start StemCode. |
| `stemcode.args` | `["--acp"]` | Arguments passed to the StemCode CLI. |
| `stemcode.workingDirectory` | workspace root | Working directory for the StemCode process. |
| `stemcode.acpAuthenticationToken` | empty | Optional ACP auth token. Used only when the server advertises `authMethods: ["token"]`. |
| `stemcode.autoStart` | `false` | Start StemCode automatically when VS Code starts. |
| `stemcode.logLevel` | `info` | Extension log level. |

When ACP authentication is enabled, the extension resolves the token in this order:

1. VS Code `SecretStorage` key `stemcode.acpAuthToken`
2. `stemcode.acpAuthenticationToken` setting
3. `STEMCODE_ACP_AUTH_TOKEN` environment variable

## Troubleshooting

### `stemcode` is not found

Install the StemCode CLI, restart your terminal, and make sure `stemcode` is available on `PATH`.

### The extension starts but cannot connect

Run `stemcode` once in a terminal and finish provider onboarding. The extension starts StemCode in ACP mode and expects local configuration to already exist.

## License

Apache License 2.0. See `LICENSE.txt`.
