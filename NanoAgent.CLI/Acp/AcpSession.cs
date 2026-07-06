using NanoAgent.Application.Backend;

namespace NanoAgent.CLI;

internal sealed class AcpSession
{
    public AcpSession(
        string sessionId,
        string workingDirectory,
        INanoAgentBackend backend,
        AcpUiBridge bridge,
        BackendSessionInfo sessionInfo)
    {
        SessionId = sessionId;
        WorkingDirectory = workingDirectory;
        Backend = backend;
        Bridge = bridge;
        SessionInfo = sessionInfo;
    }

    public CancellationTokenSource? ActivePromptCancellation { get; set; }

    public INanoAgentBackend Backend { get; }

    public AcpUiBridge Bridge { get; }

    public string SessionId { get; }

    public BackendSessionInfo SessionInfo { get; set; }

    public SemaphoreSlim TurnLock { get; } = new(1, 1);

    public string WorkingDirectory { get; }
}
