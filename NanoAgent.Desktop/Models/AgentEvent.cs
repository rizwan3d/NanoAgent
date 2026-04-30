namespace NanoAgent.Desktop.Models;

public sealed record AgentEvent(
    string Kind,
    string Message,
    DateTimeOffset Timestamp,
    string? WorkspacePath = null)
{
    public AgentEvent(string kind, string message)
        : this(kind, message, DateTimeOffset.Now)
    {
    }

    public AgentEvent(string kind, string message, string? workspacePath)
        : this(kind, message, DateTimeOffset.Now, workspacePath)
    {
    }
}
