# NanoAgent for Visual Studio

NanoAgent for Visual Studio brings the NanoAgent chat experience into Visual Studio with a docked tool window and a local ACP bridge.

## Features

- Chat with NanoAgent from inside Visual Studio.
- Send prompts while staying in the current solution context.
- Use model selection, profile switching, session management, and tool execution through the NanoAgent backend.
- Start the local NanoAgent ACP process from the extension.

## Requirements

- Visual Studio 2022 or newer.
- `nanoai.exe` installed and available on `PATH`, or configured explicitly in the NanoAgent options page.
- NanoAgent provider onboarding completed at least once before first use.

## Setup

1. Install NanoAgent CLI and make sure `nanoai.exe` is available in a new terminal.
2. Run `nanoai` once and complete provider setup.
3. Install the VSIX in Visual Studio.
4. Open the NanoAgent tool window from Visual Studio.

## Source

Project repository:

```text
https://github.com/Rizwan3D/NanoAgent
```
