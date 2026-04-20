# NanoAgent

## Installation

### macOS/Linux
```bash
curl -fsSL https://raw.githubusercontent.com/rizwan3d/NanoAgent/master/scripts/install.sh | bash
```
### Windows

```powershell
irm https://raw.githubusercontent.com/rizwan3d/NanoAgent/master/scripts/install.ps1 | iex
```

Restart your shell after installation if the `NanoAgent` command is not immediately available.

## Planning Workflow

NanoAgent now uses an automatic planning-first workflow for normal coding prompts.

- It inspects and analyzes first using read-only tools.
- It produces an internal step-by-step plan grounded in the repo state.
- It then executes that plan step by step instead of jumping straight into edits.
- Planning does not write files, apply patches, or run destructive commands.

There are no separate `/plan` or `/execute` mode commands in this flow. A normal prompt goes through planning first, then execution, and the final response summarizes the work, validation, and any remaining risks.
