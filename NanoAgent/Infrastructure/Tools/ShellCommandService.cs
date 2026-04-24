using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Utilities;
using NanoAgent.Infrastructure.Secrets;
using System.Text;

namespace NanoAgent.Infrastructure.Tools;

internal sealed class ShellCommandService : IShellCommandService
{
    private const int MaxOutputCharacters = 8_000;

    private readonly IProcessRunner _processRunner;
    private readonly PermissionSettings _permissionSettings;
    private readonly IWorkspaceRootProvider _workspaceRootProvider;

    public ShellCommandService(
        IProcessRunner processRunner,
        IWorkspaceRootProvider workspaceRootProvider,
        PermissionSettings? permissionSettings = null)
    {
        _processRunner = processRunner;
        _workspaceRootProvider = workspaceRootProvider;
        _permissionSettings = permissionSettings ?? new PermissionSettings();
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
        string workspaceRoot = Path.GetFullPath(_workspaceRootProvider.GetWorkspaceRoot());
        IReadOnlyDictionary<string, string> sandboxEnvironment = BuildSandboxEnvironment(
            request,
            workspaceRoot);
        string commandText = OperatingSystem.IsWindows()
            ? BuildWindowsCommandText(request.Command)
            : request.Command;
        ProcessExecutionRequest processRequest = OperatingSystem.IsWindows()
            ? new ProcessExecutionRequest(
                "powershell",
                ["-NoProfile", "-NonInteractive", "-Command", commandText],
                WorkingDirectory: workingDirectory,
                MaxOutputCharacters: MaxOutputCharacters,
                EnvironmentVariables: sandboxEnvironment)
            : new ProcessExecutionRequest(
                "/bin/bash",
                ["-lc", request.Command],
                WorkingDirectory: workingDirectory,
                MaxOutputCharacters: MaxOutputCharacters,
                EnvironmentVariables: sandboxEnvironment);

        ProcessExecutionResult result = await _processRunner.RunAsync(
            processRequest,
            cancellationToken);

        return new ShellCommandExecutionResult(
            request.Command,
            ToWorkspaceRelativePath(workingDirectory),
            result.ExitCode,
            TrimOutput(result.StandardOutput),
            TrimOutput(result.StandardError),
            ShellCommandSandboxArguments.ToWireValue(request.SandboxPermissions),
            string.IsNullOrWhiteSpace(request.Justification)
                ? null
                : request.Justification.Trim());
    }

    private IReadOnlyDictionary<string, string> BuildSandboxEnvironment(
        ShellCommandExecutionRequest request,
        string workspaceRoot)
    {
        Dictionary<string, string> environment = new(StringComparer.Ordinal)
        {
            ["NANOAGENT_SANDBOX_MODE"] = ToWireValue(_permissionSettings.SandboxMode),
            ["NANOAGENT_SANDBOX_PERMISSIONS"] = ShellCommandSandboxArguments.ToWireValue(request.SandboxPermissions),
            ["NANOAGENT_WORKSPACE_ROOT"] = workspaceRoot
        };

        if (!string.IsNullOrWhiteSpace(request.Justification))
        {
            environment["NANOAGENT_SANDBOX_JUSTIFICATION"] = request.Justification.Trim();
        }

        if (request.PrefixRule is { Count: > 0 })
        {
            environment["NANOAGENT_SANDBOX_PREFIX_RULE"] = string.Join(" ", request.PrefixRule);
        }

        return environment;
    }

    private static string ToWireValue(ToolSandboxMode sandboxMode)
    {
        return sandboxMode switch
        {
            ToolSandboxMode.ReadOnly => "read-only",
            ToolSandboxMode.DangerFullAccess => "danger-full-access",
            _ => "workspace-write"
        };
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
        string fullPath = WorkspacePath.Resolve(workspaceRoot, requestedPath);

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
        return WorkspacePath.ToRelativePath(workspaceRoot, fullPath);
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
