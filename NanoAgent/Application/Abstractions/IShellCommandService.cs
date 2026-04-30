using NanoAgent.Application.Tools.Models;

namespace NanoAgent.Application.Abstractions;

public interface IShellCommandService
{
    Task<ShellCommandExecutionResult> ExecuteAsync(
        ShellCommandExecutionRequest request,
        CancellationToken cancellationToken);

    Task<ShellCommandExecutionResult> StartBackgroundAsync(
        ShellCommandExecutionRequest request,
        CancellationToken cancellationToken);

    Task<ShellCommandExecutionResult> ReadBackgroundAsync(
        string terminalId,
        CancellationToken cancellationToken);

    Task<ShellCommandExecutionResult> StopBackgroundAsync(
        string terminalId,
        CancellationToken cancellationToken);
}
