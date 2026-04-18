using System.Diagnostics;

namespace FinalAgent.Infrastructure.Secrets;

internal sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessExecutionResult> RunAsync(
        ProcessExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ProcessStartInfo startInfo = new()
        {
            FileName = request.FileName,
            RedirectStandardInput = request.StandardInput is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (string argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new()
        {
            StartInfo = startInfo
        };

        process.Start();

        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        if (request.StandardInput is not null)
        {
            await process.StandardInput.WriteAsync(request.StandardInput.AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
            process.StandardInput.Close();
        }

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKillProcess(process);
            throw;
        }

        return new ProcessExecutionResult(
            process.ExitCode,
            await standardOutputTask,
            await standardErrorTask);
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}
