using System.Diagnostics;
using System.Text;

namespace NanoAgent.Infrastructure.Secrets;

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

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            startInfo.WorkingDirectory = request.WorkingDirectory;
        }

        foreach (string argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new()
        {
            StartInfo = startInfo
        };

        process.Start();

        Task<string> standardOutputTask = ReadToEndCappedAsync(
            process.StandardOutput,
            request.MaxOutputCharacters,
            cancellationToken);
        Task<string> standardErrorTask = ReadToEndCappedAsync(
            process.StandardError,
            request.MaxOutputCharacters,
            cancellationToken);

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

    private static async Task<string> ReadToEndCappedAsync(
        TextReader reader,
        int? maxCharacters,
        CancellationToken cancellationToken)
    {
        const int BufferSize = 4096;

        if (maxCharacters is <= 0)
        {
            await DrainAsync(reader, cancellationToken);
            return string.Empty;
        }

        char[] buffer = new char[BufferSize];
        StringBuilder builder = maxCharacters is null
            ? new StringBuilder()
            : new StringBuilder(Math.Min(maxCharacters.Value, BufferSize));
        bool truncated = false;

        while (true)
        {
            int read = await reader.ReadAsync(
                buffer.AsMemory(0, buffer.Length),
                cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (maxCharacters is null)
            {
                builder.Append(buffer, 0, read);
                continue;
            }

            int remaining = maxCharacters.Value - builder.Length;
            if (remaining <= 0)
            {
                truncated = true;
                continue;
            }

            int charactersToAppend = Math.Min(read, remaining);
            builder.Append(buffer, 0, charactersToAppend);
            truncated |= charactersToAppend < read;
        }

        if (truncated && maxCharacters is > 3)
        {
            builder.Length = Math.Min(builder.Length, maxCharacters.Value - 3);
            builder.Append("...");
        }

        return builder.ToString();
    }

    private static async Task DrainAsync(
        TextReader reader,
        CancellationToken cancellationToken)
    {
        char[] buffer = new char[4096];
        while (await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken) > 0)
        {
        }
    }
}
