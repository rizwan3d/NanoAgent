using StemCode.Application.Models;
using StemCode.Infrastructure.Workspaces;

namespace StemCode.Infrastructure.WindowsSandbox;

internal sealed record WindowsSandboxExecutionContext(
    ToolSandboxMode Mode,
    string StemCodeHome,
    string PolicyCwd,
    string CommandCwd,
    IReadOnlyList<string> WritableRoots,
    bool IncludeTempEnvironmentVariables = true,
    bool UsePrivateDesktop = true,
    bool UseElevatedRunner = false,
    WorkspaceRestrictedPathPolicy? RestrictedPathPolicy = null)
{
    /// <summary>
    /// Workspace read/write restrictions resolved from <c>.stemcode/.stemcodeignore</c>. Falls
    /// back to the policy for <see cref="PolicyCwd"/> when a caller does not supply one.
    /// </summary>
    public WorkspaceRestrictedPathPolicy ResolvedRestrictedPathPolicy =>
        RestrictedPathPolicy ?? WorkspaceRestrictedPathPolicy.Load(PolicyCwd);
}
