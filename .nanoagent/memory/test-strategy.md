# Test Strategy

## Test Layers

### 1. Unit Tests — `NanoAgent.Tests/`

The test project targets xUnit (implied by convention and project structure). Tests are organized by area mirroring the application layer:

| Area | Test Files | Focus |
|---|---|---|
| **Backend** | `BackendConversationHistoryFormatterTests.cs` | Session history formatting |
| **Commands** | `InitCommandHandlerTests.cs`, `ModelsCommandHandlerTests.cs`, `OnboardCommandHandlerTests.cs`, `ProviderCommandHandlerTests.cs`, `SessionCommandHandlerTests.cs`, `SettingCommandHandlerTests.cs`, `UpdateCommandHandlerTests.cs`, `UseModelCommandHandlerTests.cs` | REPL command handler logic |
| **Conversation** | `AgentConversationPipelineTests.cs` | Pipeline behavior with tool calls, retries, plan execution |
| **Formatting** | `ToolOutputFormatterTests.cs` | Tool result formatting |
| **Models** | `ReplSessionContextTests.cs`, `ToolResultRedactionTests.cs` | Session context, redaction |
| **Permissions** | `SelectionPermissionApprovalPromptTests.cs`, `ToolPermissionEvaluatorTests.cs`, `ToolPermissionParserTests.cs` | Permission evaluation and parsing |
| **Profiles** | `BuiltInAgentProfileResolverTests.cs` | Profile resolution |
| **Services** | `AgentTurnServiceTests.cs`, `FirstRunOnboardingServiceTests.cs`, `InteractiveModelSelectionServiceTests.cs`, `ModelActivationServiceTests.cs`, `ModelDiscoveryServiceTests.cs`, `OnboardingInputValidatorTests.cs`, `ReplSectionServiceTests.cs`, `SessionAppServiceTests.cs` | Core service behavior |
| **Tools** | `AgentDelegateToolTests.cs`, `AgentOrchestrateToolTests.cs`, `ApplyPatchToolTests.cs`, `CodeIntelligenceToolTests.cs`, `CodebaseIndexToolTests.cs`, `DirectoryListToolTests.cs`, `FileDeleteToolTests.cs`, `FileReadToolTests.cs`, `FileWriteToolTests.cs`, `HeadlessBrowserToolTests.cs`, `LessonMemoryToolTests.cs`, `PlanningModeToolTests.cs`, `RepoMemoryToolTests.cs`, `SearchFilesToolTests.cs`, `ShellCommandToolTests.cs`, `SkillLoadToolTests.cs` | Individual tool execution |
| **Tool Services** | `RegistryBackedToolInvokerTests.cs`, `ToolExecutionPipelineTests.cs`, `ToolRegistryTests.cs` | Tool infrastructure |

### 2. Integration Tests

*(Not yet present — the current test suite is primarily unit-level with mocked dependencies.)*

### 3. Build Validation

- CI validates that `dotnet restore --locked-mode` succeeds (packages match lock files)
- AOT publish is validated implicitly by the release pipeline

## Important Commands

```bash
# Restore
dotnet restore NanoAgent.slnx

# Build
dotnet build NanoAgent.slnx --configuration Release

# Run all tests
dotnet test NanoAgent.Tests/NanoAgent.Tests.csproj --configuration Release

# Run tests with coverage (Coverlet)
dotnet test NanoAgent.Tests/NanoAgent.Tests.csproj \
  --configuration Release \
  /p:CollectCoverage=true \
  /p:CoverletOutput=./artifacts/coverage/ \
  /p:CoverletOutputFormat=cobertura \
  /p:Threshold=60 \
  /p:ThresholdType=line \
  /p:ThresholdStat=total

# Run a specific test class
dotnet test NanoAgent.Tests/NanoAgent.Tests.csproj \
  --filter "FullyQualifiedName~FileReadToolTests"

# Run tests with TRX logger
dotnet test NanoAgent.Tests/NanoAgent.Tests.csproj \
  --logger "trx;LogFileName=test-results.trx" \
  --results-directory ./artifacts/test-results
```

## Coverage Requirements

- **Minimum line coverage threshold**: 60% (set in CI with Coverlet's `Threshold=60`)
- Coverage is enforced on total line coverage (`ThresholdStat=total`, `ThresholdType=line`)
- Coverage reports are uploaded as CI artifacts in Cobertura format
- A coverage summary is posted to the GitHub Actions step summary

## Test Patterns

### Session Factory
Tests use `TestSessionFactory.Create()` which creates a `ReplSessionContext` with:
- `AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1")`
- Model ID: `gpt-5-mini`
- Available models: `["gpt-5-mini", "gpt-4.1"]`

### Mocking Strategy
- Dependencies are mocked implicitly via the constructor injection pattern
- Tests construct services with mock/fake implementations of their dependencies
- Pipeline tests use `IConversationProgressSink` with fake/mock implementations

## Validation Guidance

When adding or modifying code:
1. Add or update unit tests for new/changed behavior
2. Run `dotnet test` with the relevant filter before committing
3. Ensure AOT compatibility if changing CLI project code (no reflection, source-gen JSON)
4. Update `packages.lock.json` if dependencies change
5. Verify CI passes on PR — the pipeline runs restore (locked-mode), build, test with coverage
6. For new tools, add a corresponding `{ToolName}Tests.cs` and register in `ServiceCollectionExtensions`
7. For new models, ensure `ConversationJsonContext` or `ToolJsonContext` includes source-gen entries if serialized
