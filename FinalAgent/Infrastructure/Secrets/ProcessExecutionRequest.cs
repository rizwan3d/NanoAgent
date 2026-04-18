namespace FinalAgent.Infrastructure.Secrets;

internal sealed record ProcessExecutionRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? StandardInput = null);
