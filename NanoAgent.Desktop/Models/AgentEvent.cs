using System;

namespace NanoAgent.Desktop.Models;

public sealed record AgentEvent(string Kind, string Message, DateTimeOffset Timestamp)
{
    public AgentEvent(string kind, string message)
        : this(kind, message, DateTimeOffset.Now)
    {
    }
}
