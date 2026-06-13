using NanoAgent.Application.Tools.Models;

namespace NanoAgent.Application.Abstractions;

public interface IShellCommandService
{
    bool IsPseudoTerminalSupported { get; }

    Task<ShellCommandExecutionResult> ExecuteAsync(
        ShellCommandExecutionRequest request,
        CancellationToken cancellationToken);

    Task<ShellCommandExecutionResult> StartBackgroundAsync(
        ShellCommandExecutionRequest request,
        CancellationToken cancellationToken);

    Task<ShellCommandExecutionResult> ReadBackgroundAsync(
        string terminalId,
        string? sessionId,
        CancellationToken cancellationToken);

    Task<ShellCommandExecutionResult> StopBackgroundAsync(
        string terminalId,
        string? sessionId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<BackgroundTerminalInfo>> ListBackgroundAsync(
        string? sessionId,
        CancellationToken cancellationToken);
}
