using NanoAgent.Application.Models;

namespace NanoAgent.Infrastructure.WindowsSandbox;

internal sealed record WindowsSandboxExecutionContext(
    ToolSandboxMode Mode,
    string NanoAgentHome,
    string PolicyCwd,
    string CommandCwd,
    IReadOnlyList<string> WritableRoots,
    bool IncludeTempEnvironmentVariables = true,
    bool UsePrivateDesktop = true,
    bool UseElevatedRunner = false);
