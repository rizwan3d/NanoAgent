# StemCode for Visual Studio

StemCode for Visual Studio brings the StemCode chat experience into Visual Studio with a docked tool window and a local ACP bridge.

## Features

- Chat with StemCode from inside Visual Studio.
- Send prompts while staying in the current solution context.
- Use model selection, profile switching, session management, and tool execution through the StemCode backend.
- Start the local StemCode ACP process from the extension.

## Requirements

- Visual Studio 2022 or newer.
- `stemcode.exe` installed and available on `PATH`, or configured explicitly in the StemCode options page.
- StemCode provider onboarding completed at least once before first use.

## Setup

1. Install StemCode CLI and make sure `stemcode.exe` is available in a new terminal.
2. Run `stemcode` once and complete provider setup.
3. Install the VSIX in Visual Studio.
4. Open the StemCode tool window from Visual Studio.

## Source

Project repository:

```text
https://github.com/Rizwan3D/StemCode
```
