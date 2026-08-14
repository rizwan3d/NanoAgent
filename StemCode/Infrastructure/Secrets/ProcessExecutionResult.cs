namespace StemCode.Infrastructure.Secrets;

internal sealed record ProcessExecutionResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);
