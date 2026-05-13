namespace NanoAgent.Application.Models;

public sealed class ToolExecutionSettings
{
    public int AcpRequestTimeoutSeconds { get; set; }

    public int HttpClientTimeoutSeconds { get; set; }

    public int DefaultTimeoutSeconds { get; set; } = 180;

    public int MaxConcurrentBackgroundTerminalsPerSession { get; set; } = 4;

    public int CompletedBackgroundTerminalTtlSeconds { get; set; } = 300;

    public int AgentOrchestrationTimeoutSeconds { get; set; }
}
