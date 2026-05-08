# Decisions

## Technical Decisions

### 1. AOT Compilation for CLI
**Date**: Project inception  
**Decision**: The CLI project uses `<PublishAot>true</PublishAot>` and targets Native AOT.  
**Context**: NanoAgent is distributed as a single-file, self-contained binary across Windows, Linux, and macOS. AOT provides fast startup, small footprint, and no runtime JIT dependency.  
**Consequences**:
- All JSON serialization must use source-generated `JsonSerializerContext` (`ConversationJsonContext`, `ToolJsonContext`)
- Reflection-heavy patterns (e.g., `Activator.CreateInstance`) are avoided
- Dynamic code generation is not available
- `InvariantGlobalization` is enabled (no ICU dependencies)

### 2. ACP (Agent Client Protocol) for IDE Integration
**Date**: Project inception  
**Decision**: IDE plugins (JetBrains, VS Code) communicate with the NanoAgent CLI via a JSON-RPC 2.0 protocol called ACP over stdin/stdout.  
**Context**: Rather than embedding the AI agent logic in each IDE plugin separately, a single CLI backend serves all IDE integrations through a standardized protocol.  
**Consequences**:
- The CLI must be installed and on PATH for IDE plugins to work
- New IDE integrations only need an ACP client implementation
- Protocol versioning is handled via `initialize` handshake
- Streaming responses, tool calls, permission prompts, and session management are all supported in the protocol

### 3. Singleton Lifetime for All Application Services
**Date**: Project inception  
**Decision**: All application-layer services (turn service, conversation pipeline, tool registry, permission evaluator, etc.) are registered as singletons in DI.  
**Context**: The application is a long-running agent process; there is typically one workspace session at a time, and stateless services are safe to share.  
**Consequences**:
- No per-request scoping overhead
- Services must be thread-safe or use synchronization for shared mutable state
- Session-specific state lives in `ReplSessionContext`, not in the singleton services

### 4. Hosted Service Pattern via NanoAgentHostFactory
**Date**: Project inception  
**Decision**: `NanoAgentHostFactory.Create()` builds an `IHost` using `Host.CreateEmptyApplicationBuilder()` with manual service registration, rather than `Host.CreateDefaultBuilder()`.  
**Context**: The CLI and Desktop apps have different UI surfaces but share the same backend services. A lightweight host avoids unnecessary default host configuration (hosting, environment, etc.).  
**Consequences**:
- No implicit appsettings.json loading from the host builder; `appsettings.json` is linked manually in each project
- Minimal startup overhead
- More explicit control over service registration

### 5. Retry Logic for Provider Empty/Incomplete Responses
**Date**: Project inception  
**Decision**: The conversation pipeline retries up to 3 times when the provider returns an empty response or exposes raw tool-call protocol text in assistant content.  
**Context**: LLM providers occasionally return empty or malformed responses. Rather than failing the turn, the agent re-prompts the model with context about the issue.  
**Consequences**:
- More robust user experience (fewer unexplained failures)
- The retry system prompt includes the original system prompt plus recovery instructions
- A separate retry limit (1 attempt) exists for incomplete plan responses

### 6. Planning Mode with Execution Plan Tracking
**Date**: Project inception  
**Decision**: When the agent produces a plan (via `planning_mode`), subsequent user approval executes the plan with a tracked `ExecutionPlanTracker` that prevents the model from concluding while plan items are still pending.  
**Context**: The plan-first workflow requires the agent to complete all planned tasks before producing a final answer, preventing premature responses.  
**Consequences**:
- Pipeline detects incomplete plans and retries the model
- `ExecutionPlanProgress` is reported to the UI via `IConversationProgressSink`
- The model receives a special execution-phase system prompt

### 7. Spectre.Console for CLI Terminal UI
**Date**: Project inception  
**Decision**: The CLI uses Spectre.Console for rich terminal output (live updates, panels, markup).  
**Context**: Spectre.Console provides cross-platform support for ANSI escape sequences, live rendering, and a column-based layout.  
**Consequences**:
- Single dependency for terminal rendering
- Game-loop pattern (16ms) for smooth UI updates
- Manual escape sequence handling for alternate screen, bracketed paste, mouse tracking
- Windows requires `EnableVirtualTerminalInput` mode

### 8. Avalonia for Desktop UI
**Date**: Project inception  
**Decision**: The desktop application uses Avalonia (cross-platform .NET UI framework).  
**Context**: Avalonia provides a WPF-like XAML development experience that runs on Windows, Linux, and macOS with native look and feel.  
**Consequences**:
- XAML-based UI with MVVM pattern
- Fluent theme with custom dark theme
- Compiled bindings enabled by default
- CommunityToolkit.Mvvm for observable properties and commands
