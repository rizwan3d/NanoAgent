using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Infrastructure.Secrets;
using System.Text;

namespace NanoAgent.Infrastructure.Tools;

internal sealed class ShellCommandService : IShellCommandService
{
    private const int MaxOutputCharacters = 8_000;

    private readonly IProcessRunner _processRunner;
    private readonly IWorkspaceRootProvider _workspaceRootProvider;

    public ShellCommandService(
        IProcessRunner processRunner,
        IWorkspaceRootProvider workspaceRootProvider)
    {
        _processRunner = processRunner;
        _workspaceRootProvider = workspaceRootProvider;
    }

    public async Task<ShellCommandExecutionResult> ExecuteAsync(
        ShellCommandExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Command))
        {
            throw new ArgumentException(
                "Shell command must be provided.",
                nameof(request));
        }

        string workingDirectory = ResolveWorkspacePath(request.WorkingDirectory, directoryRequired: true);
        string commandText = OperatingSystem.IsWindows()
            ? BuildWindowsCommandText(request.Command)
            : request.Command;
        ProcessExecutionRequest processRequest = OperatingSystem.IsWindows()
            ? new ProcessExecutionRequest(
                "powershell",
                ["-NoProfile", "-NonInteractive", "-Command", commandText],
                WorkingDirectory: workingDirectory,
                MaxOutputCharacters: MaxOutputCharacters)
            : new ProcessExecutionRequest(
                "/bin/bash",
                ["-lc", request.Command],
                WorkingDirectory: workingDirectory,
                MaxOutputCharacters: MaxOutputCharacters);

        ProcessExecutionResult result = await _processRunner.RunAsync(
            processRequest,
            cancellationToken);

        return new ShellCommandExecutionResult(
            request.Command,
            ToWorkspaceRelativePath(workingDirectory),
            result.ExitCode,
            TrimOutput(result.StandardOutput),
            TrimOutput(result.StandardError));
    }

    private static string BuildWindowsCommandText(string commandText)
    {
        IReadOnlyList<ShellCommandSegment> segments = ShellCommandText.ParseSegments(commandText);
        if (segments.Count <= 1)
        {
            return commandText;
        }

        StringBuilder scriptBuilder = new();
        scriptBuilder.AppendLine("$ErrorActionPreference = 'Continue'");
        scriptBuilder.AppendLine("$__nano_exit = 0");
        scriptBuilder.AppendLine("$__nano_segment_exit = 0");
        scriptBuilder.AppendLine("function Invoke-NanoSegment([string]$encoded) {");
        scriptBuilder.AppendLine("  Set-Variable -Name LASTEXITCODE -Scope Global -Value 0 -Force");
        scriptBuilder.AppendLine("  $script:__nano_segment_exit = 0");
        scriptBuilder.AppendLine("  $scriptText = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($encoded))");
        scriptBuilder.AppendLine("  try {");
        scriptBuilder.AppendLine("    & ([ScriptBlock]::Create($scriptText))");
        scriptBuilder.AppendLine("    if (-not $?) { $script:__nano_segment_exit = 1; return }");
        scriptBuilder.AppendLine("    $script:__nano_segment_exit = [int]$global:LASTEXITCODE");
        scriptBuilder.AppendLine("  }");
        scriptBuilder.AppendLine("  catch {");
        scriptBuilder.AppendLine("    Write-Error $_");
        scriptBuilder.AppendLine("    $script:__nano_segment_exit = 1");
        scriptBuilder.AppendLine("  }");
        scriptBuilder.AppendLine("}");

        for (int index = 0; index < segments.Count; index++)
        {
            ShellCommandSegment segment = segments[index];
            string encodedSegment = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(segment.CommandText));
            string invocation = $"Invoke-NanoSegment('{encodedSegment}'); $__nano_exit = $__nano_segment_exit";

            if (index == 0 || segment.Condition == ShellCommandSegmentCondition.Always)
            {
                scriptBuilder.AppendLine(invocation);
                continue;
            }

            if (segment.Condition == ShellCommandSegmentCondition.OnSuccess)
            {
                scriptBuilder.AppendLine($"if ($__nano_exit -eq 0) {{ {invocation} }}");
                continue;
            }

            scriptBuilder.AppendLine($"if ($__nano_exit -ne 0) {{ {invocation} }}");
        }

        scriptBuilder.AppendLine("exit $__nano_exit");
        return scriptBuilder.ToString();
    }

    private string ResolveWorkspacePath(
        string? requestedPath,
        bool directoryRequired)
    {
        string workspaceRoot = Path.GetFullPath(_workspaceRootProvider.GetWorkspaceRoot());
        string normalizedRequestedPath = string.IsNullOrWhiteSpace(requestedPath)
            ? workspaceRoot
            : requestedPath.Trim();

        string fullPath = Path.GetFullPath(
            Path.IsPathRooted(normalizedRequestedPath)
                ? normalizedRequestedPath
                : Path.Combine(workspaceRoot, normalizedRequestedPath));

        EnsureWithinWorkspace(workspaceRoot, fullPath);

        if (directoryRequired && !Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(
                $"Directory '{ToWorkspaceRelativePath(fullPath)}' does not exist.");
        }

        return fullPath;
    }

    private string ToWorkspaceRelativePath(string fullPath)
    {
        string workspaceRoot = Path.GetFullPath(_workspaceRootProvider.GetWorkspaceRoot());
        if (string.Equals(workspaceRoot, fullPath, GetPathComparison()))
        {
            return ".";
        }

        return Path.GetRelativePath(workspaceRoot, fullPath)
            .Replace('\\', '/');
    }

    private static void EnsureWithinWorkspace(
        string workspaceRoot,
        string candidatePath)
    {
        string normalizedRoot = EnsureTrailingSeparator(workspaceRoot);
        string normalizedCandidate = EnsureTrailingSeparator(candidatePath);

        if (!normalizedCandidate.StartsWith(
                normalizedRoot,
                GetPathComparison()) &&
            !string.Equals(workspaceRoot, candidatePath, GetPathComparison()))
        {
            throw new InvalidOperationException(
                "Tool paths must stay within the current workspace.");
        }
    }

    private static StringComparison GetPathComparison()
    {
        return OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) ||
               path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static string TrimOutput(string value)
    {
        string normalizedValue = value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (normalizedValue.Length <= MaxOutputCharacters)
        {
            return normalizedValue;
        }

        return normalizedValue[..MaxOutputCharacters] + "...";
    }
}
