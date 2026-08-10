# Architecture

## Project Overview

StemCode is an autonomous AI coding agent that runs on the user's machine. It provides AI-powered code assistance through multiple surfaces: CLI terminal UI, desktop GUI (Avalonia), IDE plugins (JetBrains, VS Code), and an ACP (Agent Client Protocol) JSON-RPC server.

- **Repository**: github.com/rizwan3d/StemCode
- **Sponsor**: ALFAIN Technologies (PVT) Limited
- **Core Language**: C# (.NET 10, net10.0)
- **Solution File**: `StemCode.slnx`

## Major Components

### 1. StemCode (Core Library) â€” `StemCode/`
The shared application layer. Contains all business logic, backend services, abstractions, models, and the conversation pipeline.

| Layer | Directory | Purpose |
|---|---|---|
| **Abstractions** | `Application/Abstractions/` | 50+ interfaces defining service contracts (IAgentTurnService, IConversationPipeline, ITool, IPermissionEvaluator, etc.) |
| **Backend** | `Application/Backend/` | `StemCodeBackend` â€” the main entry point orchestrating initialization, session management, command dispatch, and conversation turns. Implements `IStemCodeBackend`. |
| **Commands** | `Application/Commands/` | REPL command parsing (`ReplCommandParser`), dispatch (`ReplCommandDispatcher`), and handlers for 30+ slash commands (`/allow`, `/budget`, `/clear`, `/config`, etc.) |
| **Conversation** | `Application/Conversation/` | `AgentConversationPipeline` â€” the core tool-driven loop: sends messages to the LLM provider, executes tool calls, retries on failures, tracks plans. |
| **Dependency Injection** | `Application/DependencyInjection/` | `ServiceCollectionExtensions.AddApplication()` â€” registers all core services as singletons. |
| **Models** | `Application/Models/` | Domain models: session context, turn results, permissions, budget controls, plans, tool definitions, etc. |
| **Exceptions** | `Application/Exceptions/` | Typed exceptions: ConversationPipelineException, ConversationProviderException, PromptCancelledException, etc. |
| **Formatting** | `Application/Formatting/` | Tool output and plan output formatters. |
| **Logging** | `Application/Logging/` | Structured log message definitions. |

### 2. StemCode.CLI (Terminal UI) â€” `StemCode.CLI/`
A Spectre.Console-based terminal application with three operating modes:

- **Interactive mode** â€” Full-screen TUI with live rendering, scrollable conversation, slash commands, and input handling. Uses `UiBridge` for async UI updates on a game-loop-style 16ms refresh cycle.
- **Single-turn mode** â€” One-shot prompt â†’ response via `stemcode "prompt"` or `--prompt`. Supports `--json` output.
- **ACP server mode** â€” `--acp` starts a JSON-RPC 2.0 server over stdin/stdout for IDE integration (JetBrains, VS Code).

Key files: `Program.cs` (entry point), `CliInvocation.cs` (argument parsing), `AppState.cs` (UI state), `ConsoleBridge.cs`/`UiBridge.cs` (UI abstractions).

### 3. StemCode.Desktop (Desktop GUI) â€” `StemCode.Desktop/`
An Avalonia UI desktop application using:
- **MVVM** with `CommunityToolkit.Mvvm`
- **Fluent theme** with custom `Dark.axaml`
- **ViewModels**: `ChatViewModel`, `MainWindowViewModel`, `ProjectViewModel`, `WorkspaceTreeItemViewModel`
- **Services**: `AgentRunner`, `DesktopUiBridge`, `GitService`, `SettingsService`, `SectionHistoryService`
- **Controls**: Custom `MarkdownMessageView` for rendering agent responses

### 4. StemCode.JetBrains (IDE Plugin) â€” `StemCode.JetBrains/`
A Kotlin-based JetBrains IntelliJ platform plugin:
- Communicates with the CLI via **ACP** (Agent Client Protocol) over stdin/stdout
- `AcpClient.kt` â€” Full JSON-RPC 2.0 client with session management, streaming responses, tool call handling, permission/text prompts
- `StemCodePlugin.kt` â€” Application-level service managing lifecycle
- `ChatPanel.kt` â€” Tool window UI for interacting with StemCode
- Targets IntelliJ IC 2024.1.7+, JVM 17

### 5. StemCode.VsCode (VS Code Extension) â€” `StemCode.VsCode/`
A VS Code extension (Node.js/npm) that also communicates with the CLI via ACP.

### 6. StemCode.Tests (Tests) â€” `StemCode.Tests/`
xUnit-based test project covering:
- Backend conversation history formatting
- Command handlers (init, models, onboard, provider, session, setting, update, use-model)
- Conversation pipeline
- Tool output formatting
- Permissions (tool permission evaluator, parser, approval prompts)
- Agent profile resolver
- Services (turn service, onboarding, model selection/discovery/activation, session service)
- Individual tool tests (agent delegate, orchestrate, apply-patch, codebase-index, code-intelligence, file-read/write/delete, directory-list, headless-browser, lesson-memory, planning-mode, repo-memory, search-files, shell-command, skill-load)

## Data Flow

```
User Input
    |
    v
StemCodeBackend.RunTurnAsync()
    |
    v
AgentTurnService.RunTurnAsync()
    |
    v
AgentConversationPipeline.ProcessAsync()
    |
    v
  +---- SendAndMapResponseAsync()  â†’  IConversationProviderClient.SendAsync()
  |         |                              (sends to LLM provider: OpenAI, Anthropic, etc.)
  |         v
  |    IConversationResponseMapper.Map()  â†’  ConversationResponse
  |
  v  (if tool calls)
ToolExecutionPipeline.ExecuteAsync()
    |
    v  (for each tool)
RegistryBackedToolInvoker â†’ ITool implementation
    |
    v  (feedback loop)
ConversationRequestMessage.ToolResult() â†’ back to provider
    |
    v  (when no more tool calls)
ConversationTurnResult returned to caller
```

## Key Integration Points

- **ACP Protocol**: JSON-RPC 2.0 used for IDE â†” CLI communication. Methods: initialize, authenticate, session/new, session/load, session/prompt, session/close, session/cancel. Notifications: session/update (agent_message, reasoning, tool_calls, tool_call_update, plan, session_info), session/request_permission, session/request_text.
- **Provider API**: Abstracted behind `IConversationProviderClient` and `IConversationResponseMapper` for multi-provider support.
- **Workspace Providers**: `IWorkspaceSystemPromptProvider`, `IWorkspaceInstructionsProvider`, `IWorkspaceAgentProfilePromptProvider` â€” extensible points for workspace-local configuration.
- **Lifecycle Hooks**: `ILifecycleHookService` with BeforeTaskStart, AfterTaskComplete, AfterTaskFailed events.
- **Budget Controls**: `IBudgetControlsUsageService`, `IBudgetControlsConfigurationStore`, `IBudgetControlsSecretStore` â€” monthly API spend limits.
- **Dynamic Tools**: MCP servers (Model Context Protocol) and custom tools can be dynamically added.
- **Agent Profiles**: Built-in profiles (build, plan, review, explore, general) and custom workspace profiles control which tools are enabled and system prompts.

## Configuration

- `appsettings.json` â€” Shared settings (copied to both CLI and Desktop outputs)
- `Directory.Build.props` â€” Lock file and CI build settings
- `.stemcode/` â€” Workspace-local agent configuration, profiles, permissions, memory, skills
- `global.json` â€” .NET SDK version pinning
- `packages.lock.json` â€” Locked NuGet dependencies for reproducible builds
