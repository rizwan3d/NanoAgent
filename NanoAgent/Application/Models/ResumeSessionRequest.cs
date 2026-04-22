namespace NanoAgent.Application.Models;

public sealed record ResumeSessionRequest(
    string SessionId,
    string? ProfileName = null);
