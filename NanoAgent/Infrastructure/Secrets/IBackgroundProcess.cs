namespace NanoAgent.Infrastructure.Secrets;

internal interface IBackgroundProcess : IDisposable
{
    TextReader StandardOutput { get; }

    TextReader StandardError { get; }

    bool HasExited { get; }

    int ExitCode { get; }

    Task StopAsync(CancellationToken cancellationToken);

    Task WaitForExitAsync(CancellationToken cancellationToken);
}
