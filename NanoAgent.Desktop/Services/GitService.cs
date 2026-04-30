using System.Diagnostics;

namespace NanoAgent.Desktop.Services;

public class GitService
{
    public async Task<IReadOnlyList<string>> GetChangedFilesAsync(string workingDirectory)
    {
        if (!Directory.Exists(workingDirectory))
        {
            return [];
        }

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("status");
        psi.ArgumentList.Add("--short");

        using var process = Process.Start(psi);
        if (process is null)
        {
            return [];
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var files = new List<string>();
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.TrimEnd();
            if (trimmed.Length <= 3)
            {
                continue;
            }

            files.Add(trimmed[3..]);
        }

        return files;
    }
}
