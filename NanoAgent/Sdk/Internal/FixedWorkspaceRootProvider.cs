using NanoAgent.Application.Abstractions;

namespace NanoAgent.Sdk.Internal;

/// <summary>
/// Pins the agent's workspace root to an explicit directory supplied via
/// <see cref="NanoAgentClientBuilder.WithWorkspace"/>, instead of relying on the
/// process current directory. Only registered when a workspace is configured.
/// </summary>
internal sealed class FixedWorkspaceRootProvider : IWorkspaceRootProvider
{
    private readonly string _workspaceRoot;

    public FixedWorkspaceRootProvider(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.GetFullPath(workspaceRoot.Trim());
    }

    public string GetWorkspaceRoot()
    {
        return _workspaceRoot;
    }
}
