using StemCode.Application.Models;

namespace StemCode.Infrastructure.WindowsSandbox;

internal sealed record WindowsSandboxExecutionContext(
    ToolSandboxMode Mode,
    string StemCodeHome,
    string PolicyCwd,
    string CommandCwd,
    IReadOnlyList<string> WritableRoots,
    bool IncludeTempEnvironmentVariables = true,
    bool UsePrivateDesktop = true,
    bool UseElevatedRunner = false);
