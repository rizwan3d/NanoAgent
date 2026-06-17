namespace NanoAgent.Application.Models;

public sealed class ToolExecutionSettings
{
    public int AcpRequestTimeoutSeconds { get; set; }

    public int HttpClientTimeoutSeconds { get; set; }

    public int McpRequestTimeoutSeconds { get; set; }

    public int DefaultTimeoutSeconds { get; set; } = 180;

    public int MaxConcurrentBackgroundTerminalsPerSession { get; set; } = 4;

    public int CompletedBackgroundTerminalTtlSeconds { get; set; } = 300;

    public int AgentOrchestrationTimeoutSeconds { get; set; }

    /// <summary>
    /// Default rendering for tool results in session output. Accepts
    /// <c>full</c>/<c>complete</c> (print the complete output) or
    /// <c>compact</c>/<c>preview</c> (print the capped preview).
    /// <see langword="null"/> or unrecognized values keep the compact default.
    /// </summary>
    public string? ToolOutput { get; set; }
}
