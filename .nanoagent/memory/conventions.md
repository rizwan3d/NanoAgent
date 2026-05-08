# Conventions

## Coding Conventions

### C# (NanoAgent, NanoAgent.CLI, NanoAgent.Desktop, NanoAgent.Tests)

- **Language version**: C# latest (implicit from .NET 10)
- **Nullable**: Enabled (`<Nullable>enable</Nullable>` in all projects)
- **Implicit usings**: Enabled
- **Target**: net10.0
- **AOT**: CLI project uses `<PublishAot>true</PublishAot>` and `<InvariantGlobalization>true</InvariantGlobalization>`
- **Lock files**: All projects use `packages.lock.json` for deterministic restore (`RestorePackagesWithLockFile=true` in `Directory.Build.props`)

### Naming

| Element | Convention | Example |
|---|---|---|
| Namespaces | `PascalCase`, project-rooted | `NanoAgent.Application.Backend`, `NanoAgent.CLI.Presentation` |
| Interfaces | `I` prefix, `PascalCase` | `INanoAgentBackend`, `IConversationPipeline` |
| Classes/Structs | `PascalCase` | `NanoAgentBackend`, `AgentConversationPipeline` |
| Methods | `PascalCase` | `RunTurnAsync`, `ProcessAsync` |
| Async methods | `Async` suffix | `InitializeAsync`, `DisposeAsync` |
| Private fields | `_camelCase` with underscore prefix | `_session`, `_host`, `_agentTurnService` |
| Local constants | `PascalCase` | `RetryableProviderOutputRetryLimit` |
| Public constants | `PascalCase` | `RepositoryUrl` |
| Records | `PascalCase` | `BackendSessionInfo`, `CliSessionOptions` |
| Enums | `PascalCase` | `ConversationExecutionPhase`, `PermissionMode` |
| Tests | `{Unit}Tests` suffix | `AgentDelegateToolTests`, `FileReadToolTests` |

### Patterns & Style

- **Constructor injection** for dependencies (all services)
- **Primary constructors** used where practical (records, some classes)
- **Target-typed new** expressions (`new()`)
- **Collection expressions** (`[]`, `[.. items]`)
- **Pattern matching**: switch expressions, property patterns, `is not null`
- **`ArgumentNullException.ThrowIfNull()`** for null guard clauses
- **`ArgumentNullException.ThrowIfNullOrWhiteSpace()`** for string null/blank guards
- **`CancellationToken`** passed through async chains as last parameter
- **`TimeProvider` abstraction** used instead of `DateTime.UtcNow` / `Task.Delay` directly
- **`ILogger<T>`** with structured logging (log message definitions in `ApplicationLogMessages.cs`)
- **Sealed internal classes** for non-public types (e.g., `sealed class ConversationTelemetryAccumulator`)
- **Record types** for DTOs, immutable state, and result types
- **`JsonSerializerContext`** (source-generated serialization context) for AOT-compatible JSON

### Error Handling

- Typed exception hierarchy in `NanoAgent.Application.Exceptions`
- Operations that can fail with provider/network issues catch `OperationCanceledException` first
- Lifecycle hook failures in after-complete/after-failed hooks are caught and squashed to avoid masking primary results
- Budget recording failures are advisory (caught and swallowed)
- Lesson memory failures return null gracefully

### Concurrency

- `CancellationToken` throughout async paths
- `ConcurrentDictionary` / `ConcurrentHashMap` for shared state (ACP client)
- `Interlocked` / `AtomicLong` for counters in concurrent contexts
- Game-loop pattern in CLI UI (16ms refresh with `Task.Delay`)
- `SemaphoreSlim` for critical sections where needed

## Review Conventions

- PR reviews should verify: AOT compatibility, nullable annotation correctness, cancellation token propagation, exception handling isolation, and lock file updates
- New tools must be registered in `ServiceCollectionExtensions.cs`
- New models must have source-gen JSON context entries if serialized
- Test coverage should include both success and failure paths for services
- Tool tests follow the pattern in `TestSessionFactory.Create()` for session setup

## Workflow Conventions

- CI runs on push to master and PRs (`.github/workflows/ci.yml`)
- Release triggered by `v*` tags (`.github/workflows/release.yml` and `release-signing.yml`)
- VS Code extension CD via tag push or workflow dispatch (`.github/workflows/vscode-extension-cd.yml`)
- GitLab CI config at `.gitlab-ci.yml`, Bitbucket script at `.bitbucket/nanoai-bitbucket-review.sh`
- `.nanoagent/.nanoignore` manages which files/patterns are excluded from agent workspace scanning
