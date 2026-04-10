using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace NanoAgent;

internal static class ToolRuntime
{
    public static string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, path));
    }

    public static string ResolveScope(string? path) =>
        string.IsNullOrWhiteSpace(path) ? Environment.CurrentDirectory : ResolvePath(path);

    public static ProcessStartInfo CreateShellStartInfo(string command)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -EncodedCommand {EncodePowerShellCommand(command)}",
                WorkingDirectory = Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        string shell = File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh";
        return new ProcessStartInfo
        {
            FileName = shell,
            Arguments = $"-lc {EscapePosix(command)}",
            WorkingDirectory = Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }

    public static string FormatShellCommand(ProcessStartInfo startInfo) =>
        string.IsNullOrWhiteSpace(startInfo.Arguments)
            ? startInfo.FileName
            : $"{startInfo.FileName} {startInfo.Arguments}";

    public static bool IsCommandAvailable(string commandName)
    {
        try
        {
            ProcessStartInfo startInfo = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new ProcessStartInfo
                {
                    FileName = "where",
                    Arguments = commandName,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
                : new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    Arguments = $"-lc 'command -v {commandName}'",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

            using Process process = new() { StartInfo = startInfo };
            process.Start();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static ProcessStartInfo CreateGitApplyStartInfo(string patchPath) =>
        new()
        {
            FileName = "git",
            Arguments = $"apply --whitespace=nowarn --recount \"{patchPath.Replace("\"", "\"\"")}\"",
            WorkingDirectory = Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

    public static ProcessStartInfo CreateGitDiffNoIndexStartInfo(string originalPath, string updatedPath) =>
        new()
        {
            FileName = "git",
            Arguments =
                $"diff --no-index --unified=3 --no-color -- \"{originalPath.Replace("\"", "\"\"")}\" \"{updatedPath.Replace("\"", "\"\"")}\"",
            WorkingDirectory = Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

    public static int CountOccurrences(string content, string value)
    {
        int count = 0;
        int index = 0;

        while ((index = content.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    public static string ReplaceFirst(string content, string oldValue, string newValue)
    {
        int index = content.IndexOf(oldValue, StringComparison.Ordinal);
        if (index < 0)
        {
            return content;
        }

        return string.Concat(
            content.AsSpan(0, index),
            newValue,
            content.AsSpan(index + oldValue.Length));
    }

    public static string NormalizeNewlines(string content) =>
        content.Replace("\r\n", "\n").Replace('\r', '\n');

    public static string DetectPreferredNewline(string content) =>
        content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    public static string RestoreNewlines(string content, string newline) =>
        newline == "\n" ? content : content.Replace("\n", newline, StringComparison.Ordinal);

    private static string EscapePosix(string command) =>
        $"'{command.Replace("'", "'\"'\"'")}'";

    private static string EncodePowerShellCommand(string command)
    {
        string wrappedCommand = $"& {{ {command} }}";
        byte[] bytes = Encoding.Unicode.GetBytes(wrappedCommand);
        return Convert.ToBase64String(bytes);
    }
}
