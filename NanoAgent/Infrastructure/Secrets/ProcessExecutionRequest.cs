namespace NanoAgent.Infrastructure.Secrets;

internal sealed record ProcessExecutionRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? StandardInput = null,
    string? WorkingDirectory = null,
    int? MaxOutputCharacters = null,
    IReadOnlyDictionary<string, string>? EnvironmentVariables = null,
    bool UsePseudoTerminal = false,
    // Optional: invoked with each completed stdout/stderr line as it is read, in
    // addition to the captured output, so callers can stream live progress. The
    // callback may run on a background reader thread and must be thread-safe.
    Action<string>? OnOutputLine = null);
