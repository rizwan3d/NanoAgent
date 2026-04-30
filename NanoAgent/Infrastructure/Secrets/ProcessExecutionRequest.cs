namespace NanoAgent.Infrastructure.Secrets;

internal sealed record ProcessExecutionRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? StandardInput = null,
    string? WorkingDirectory = null,
    int? MaxOutputCharacters = null,
    IReadOnlyDictionary<string, string>? EnvironmentVariables = null,
    bool UsePseudoTerminal = false,
    WindowsSandboxConfiguration? WindowsSandbox = null);

internal sealed record WindowsSandboxConfiguration(
    string ProfileName,
    string WorkspaceRoot,
    string TempDirectory,
    bool AllowWorkspaceWrite);
