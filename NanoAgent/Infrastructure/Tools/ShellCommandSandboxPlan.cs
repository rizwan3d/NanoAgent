using NanoAgent.Application.Models;
using NanoAgent.Application.Utilities;
using NanoAgent.Infrastructure.Secrets;
using System.Text;

namespace NanoAgent.Infrastructure.Tools;

internal sealed record ShellCommandSandboxPlan(
    ProcessExecutionRequest Request,
    string Enforcement,
    string? UnsupportedReason = null)
{
    public bool IsUnsupported => !string.IsNullOrWhiteSpace(UnsupportedReason);
}

internal static class ShellCommandSandboxPlanner
{
    public const string NoEnforcement = "none";
    public const string UnsupportedEnforcement = "unsupported";
    public const string BubblewrapEnforcement = "bubblewrap";
    public const string SandboxExecEnforcement = "sandbox-exec";

    public static ShellCommandSandboxPlan Create(
        ProcessExecutionRequest shellRequest,
        ToolSandboxMode effectiveSandboxMode,
        string workspaceRoot,
        string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(shellRequest);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        if (effectiveSandboxMode == ToolSandboxMode.DangerFullAccess)
        {
            return new ShellCommandSandboxPlan(shellRequest, NoEnforcement);
        }

        string normalizedWorkspaceRoot = Path.GetFullPath(workspaceRoot);
        string normalizedWorkingDirectory = Path.GetFullPath(workingDirectory);

        if (OperatingSystem.IsLinux())
        {
            return CreateLinuxPlan(
                shellRequest,
                effectiveSandboxMode,
                normalizedWorkspaceRoot,
                normalizedWorkingDirectory);
        }

        if (OperatingSystem.IsMacOS())
        {
            return CreateMacOsPlan(
                shellRequest,
                effectiveSandboxMode,
                normalizedWorkspaceRoot,
                normalizedWorkingDirectory);
        }

        return Unsupported(
            shellRequest,
            effectiveSandboxMode,
            "OS-level shell sandboxing is not available on this platform.");
    }

    private static ShellCommandSandboxPlan CreateLinuxPlan(
        ProcessExecutionRequest shellRequest,
        ToolSandboxMode effectiveSandboxMode,
        string workspaceRoot,
        string workingDirectory)
    {
        List<string> arguments =
        [
            "--die-with-parent",
            "--unshare-all",
            "--share-net",
            "--proc",
            "/proc",
            "--dev",
            "/dev",
            "--ro-bind",
            "/",
            "/"
        ];

        if (effectiveSandboxMode == ToolSandboxMode.WorkspaceWrite)
        {
            AddLinuxWritableTempMount(arguments, "/tmp", workspaceRoot);
            AddLinuxWritableTempMount(arguments, "/var/tmp", workspaceRoot);
            arguments.Add("--bind");
            arguments.Add(workspaceRoot);
            arguments.Add(workspaceRoot);
        }

        arguments.Add("--chdir");
        arguments.Add(workingDirectory);
        arguments.Add(shellRequest.FileName);
        arguments.AddRange(shellRequest.Arguments);

        return new ShellCommandSandboxPlan(
            shellRequest with
            {
                FileName = "bwrap",
                Arguments = arguments,
                WorkingDirectory = workspaceRoot
            },
            BubblewrapEnforcement);
    }

    private static void AddLinuxWritableTempMount(
        List<string> arguments,
        string tempRoot,
        string workspaceRoot)
    {
        if (!Directory.Exists(tempRoot) ||
            WorkspacePath.IsSamePathOrDescendant(
                Path.GetFullPath(tempRoot),
                workspaceRoot))
        {
            return;
        }

        arguments.Add("--tmpfs");
        arguments.Add(tempRoot);
    }

    private static ShellCommandSandboxPlan CreateMacOsPlan(
        ProcessExecutionRequest shellRequest,
        ToolSandboxMode effectiveSandboxMode,
        string workspaceRoot,
        string workingDirectory)
    {
        string profile = BuildMacOsSandboxProfile(
            effectiveSandboxMode,
            workspaceRoot);
        List<string> arguments = ["-p", profile, shellRequest.FileName];
        arguments.AddRange(shellRequest.Arguments);

        return new ShellCommandSandboxPlan(
            shellRequest with
            {
                FileName = "sandbox-exec",
                Arguments = arguments,
                WorkingDirectory = workingDirectory
            },
            SandboxExecEnforcement);
    }

    private static string BuildMacOsSandboxProfile(
        ToolSandboxMode effectiveSandboxMode,
        string workspaceRoot)
    {
        StringBuilder builder = new();
        builder.AppendLine("(version 1)");
        builder.AppendLine("(allow default)");
        builder.AppendLine("(deny file-write*)");

        if (effectiveSandboxMode == ToolSandboxMode.WorkspaceWrite)
        {
            string[] writableRoots =
            [
                workspaceRoot,
                Path.GetTempPath(),
                "/tmp",
                "/private/tmp",
                "/var/tmp"
            ];

            builder.AppendLine("(allow file-write*");
            foreach (string writableRoot in writableRoots
                         .Where(static root => !string.IsNullOrWhiteSpace(root))
                         .Select(static root => Path.GetFullPath(root))
                         .Distinct(StringComparer.Ordinal))
            {
                builder.Append("  (subpath ");
                builder.Append(ToSandboxString(writableRoot));
                builder.AppendLine(")");
            }

            builder.AppendLine(")");
        }

        return builder.ToString();
    }

    private static ShellCommandSandboxPlan Unsupported(
        ProcessExecutionRequest shellRequest,
        ToolSandboxMode effectiveSandboxMode,
        string reason)
    {
        return new ShellCommandSandboxPlan(
            shellRequest,
            UnsupportedEnforcement,
            $"{reason} The effective sandbox mode is '{ToWireValue(effectiveSandboxMode)}'. The command will run after NanoAgent permission approval without OS-level sandbox enforcement.");
    }

    private static string ToSandboxString(string value)
    {
        return "\"" +
               value
                   .Replace("\\", "\\\\", StringComparison.Ordinal)
                   .Replace("\"", "\\\"", StringComparison.Ordinal) +
               "\"";
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
}
