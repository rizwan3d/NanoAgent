namespace NanoAgent.Infrastructure.Secrets;

internal sealed record ProcessExecutionRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? StandardInput = null,
    string? WorkingDirectory = null,
    int? MaxOutputCharacters = null);
