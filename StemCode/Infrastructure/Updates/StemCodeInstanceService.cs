using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using StemCode.Application.Abstractions;
using StemCode.Infrastructure.Secrets;

namespace StemCode.Infrastructure.Updates;

/// <summary>
/// Detects and terminates other running StemCode CLI sessions so an in-place
/// update can replace the currently executing binary without file-lock errors.
/// Process enumeration and termination are delegated to OS tools (tasklist/ps,
/// taskkill/kill) via <see cref="IProcessRunner"/> so the logic stays
/// AOT-compatible and avoids platform-specific process APIs.
/// </summary>
internal sealed class StemCodeInstanceService : IStemCodeInstanceService
{
    // Process names that identify a running StemCode CLI binary: the archive
    // executable (StemCode.CLI) and the installed command name (stemcode).
    private static readonly HashSet<string> StemCodeProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "stemcode",
        "stemcode.cli"
    };

    private static readonly Regex QuotedFieldRegex = new("\"([^\"]*)\"");

    private readonly IProcessRunner _processRunner;

    public StemCodeInstanceService(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<IReadOnlyList<RunningStemCodeInstance>> GetOtherRunningInstancesAsync(
        CancellationToken cancellationToken)
    {
        int currentProcessId = Environment.ProcessId;
        List<RunningStemCodeInstance> instances = [];

        try
        {
            ProcessExecutionResult result = await _processRunner.RunAsync(
                OperatingSystem.IsWindows()
                    ? new ProcessExecutionRequest("tasklist", ["/NH", "/FO", "CSV"], MaxOutputCharacters: 500_000)
                    : new ProcessExecutionRequest("ps", ["-eo", "pid,comm"], MaxOutputCharacters: 500_000),
                cancellationToken);

            foreach (string line in result.StandardOutput.Split('\n'))
            {
                RunningStemCodeInstance? instance = ParseLine(line, currentProcessId);
                if (instance is not null)
                {
                    instances.Add(instance);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Best-effort: enumeration failures must never block an update.
        }

        return instances;
    }

    public async Task TerminateAsync(RunningStemCodeInstance instance, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string fileName;
        IReadOnlyList<string> arguments;
        if (OperatingSystem.IsWindows())
        {
            fileName = "taskkill";
            arguments = ["/PID", instance.ProcessId.ToString(CultureInfo.InvariantCulture), "/F", "/T"];
        }
        else
        {
            fileName = "kill";
            arguments = ["-9", instance.ProcessId.ToString(CultureInfo.InvariantCulture)];
        }

        try
        {
            await _processRunner.RunAsync(
                new ProcessExecutionRequest(fileName, arguments),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Best-effort: the target may have already exited or be protected.
        }
    }

    internal static RunningStemCodeInstance? ParseLine(string line, int currentProcessId)
    {
        string trimmed = line.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        return OperatingSystem.IsWindows()
            ? ParseWindowsLine(trimmed, currentProcessId)
            : ParsePosixLine(trimmed, currentProcessId);
    }

    internal static RunningStemCodeInstance? ParseWindowsLine(string line, int currentProcessId)
    {
        List<string> fields = [];
        foreach (Match match in QuotedFieldRegex.Matches(line))
        {
            if (match.Groups.Count > 1)
            {
                fields.Add(match.Groups[1].Value);
            }
        }

        if (fields.Count < 2)
        {
            return null;
        }

        string name = fields[0];
        if (!int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int processId))
        {
            return null;
        }

        if (processId == currentProcessId || !MatchesStemCodeName(name))
        {
            return null;
        }

        return new RunningStemCodeInstance(processId, name);
    }

    internal static RunningStemCodeInstance? ParsePosixLine(string line, int currentProcessId)
    {
        line = line.Trim();
        if (line.Length == 0)
        {
            return null;
        }

        if (line.StartsWith("PID", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        int firstSpace = line.IndexOf(' ');
        if (firstSpace < 0)
        {
            return null;
        }

        string pidText = line[..firstSpace];
        string name = line[(firstSpace + 1)..].Trim();

        if (!int.TryParse(pidText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int processId))
        {
            return null;
        }

        if (processId == currentProcessId || !MatchesStemCodeName(name))
        {
            return null;
        }

        return new RunningStemCodeInstance(processId, name);
    }

    internal static bool MatchesStemCodeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        string normalized = name.Trim();
        if (normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        return StemCodeProcessNames.Contains(normalized);
    }
}
