# NanoAgent for VS Code

NanoAgent for VS Code is a companion extension that starts the local `nanoai` CLI in the integrated terminal and sends prompts from VS Code commands, selections, and files.

## Features

- Start or restart NanoAgent in the current workspace terminal.
- Ask NanoAgent from the command palette or `Ctrl+Alt+N` / `Cmd+Alt+N`.
- Explain or fix the current editor selection.
- Ask about a file from the editor or Explorer context menu.
- Start a workspace review prompt.
- Configure the NanoAgent command, profile, thinking effort, extra CLI args, and terminal reuse.

## Development

```bash
npm install
npm run compile
```

Open this folder in VS Code, press `F5`, and run the extension in an Extension Development Host.

## Packaging

```bash
npm install
npm run package
```

The extension expects the NanoAgent CLI to be installed as `nanoai` or configured through `nanoAgent.command`.
