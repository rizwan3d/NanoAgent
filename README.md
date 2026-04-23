# NanoAgent

NanoAgent is a local command-line coding agent for inspecting repositories, editing files, running shell commands, and keeping implementation work grounded in the current workspace.

![NanoAgent terminal preview](.github/nano.jpg)

## Description

NanoAgent helps with day-to-day software engineering tasks from a terminal workflow. It can search and read files, apply focused patches, run build and test commands, manage model/provider configuration, and preserve local session history.

## Supported Providers

- OpenAI
- Anthropic
- Google AI Studio
- OpenAI-compatible providers with a custom base URL

## Installation

### macOS/Linux

```bash
curl -fsSL https://raw.githubusercontent.com/rizwan3d/NanoAgent/master/scripts/install.sh | bash
```

### Windows

```powershell
irm https://raw.githubusercontent.com/rizwan3d/NanoAgent/master/scripts/install.ps1 | iex
```

Restart your shell after installation if the `nano` command is not immediately available.

## License

This repository is licensed under the Apache-2.0 License.
