namespace NanoAgent.Application.Backend;

public sealed class BackendRuntimeOptions
{
    public BackendRuntimeOptions(
        IReadOnlyList<BackendMcpServerConfiguration>? sessionMcpServers = null,
        bool autoApproveAllTools = false)
    {
        SessionMcpServers = sessionMcpServers ?? [];
        AutoApproveAllTools = autoApproveAllTools;
    }

    public bool AutoApproveAllTools { get; }

    public IReadOnlyList<BackendMcpServerConfiguration> SessionMcpServers { get; }
}
