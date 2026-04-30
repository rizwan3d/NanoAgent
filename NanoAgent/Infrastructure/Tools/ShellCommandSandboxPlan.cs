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
    public const string WindowsAppContainerEnforcement = "windows-appcontainer";
    private static readonly string[] LinuxReadOnlySystemPaths =
    [
        "/usr",
        "/bin",
        "/lib",
        "/lib64",
        "/sbin",
        "/etc/alternatives",
        "/etc/ca-certificates",
        "/etc/hosts",
        "/etc/nsswitch.conf",
        "/etc/pki",
        "/etc/resolv.conf",
        "/etc/ssl"
    ];

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

        if (OperatingSystem.IsWindows())
        {
            return CreateWindowsPlan(
                shellRequest,
                effectiveSandboxMode,
                normalizedWorkspaceRoot);
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
            "--dir",
            "/etc",
            "--dir",
            "/tmp",
            "--dir",
            "/var",
            "--dir",
            "/var/tmp"
        ];

        AddLinuxReadOnlySystemMounts(arguments);
        AddLinuxWritableTempMount(arguments, "/tmp", workspaceRoot);
        AddLinuxWritableTempMount(arguments, "/var/tmp", workspaceRoot);
        AddLinuxSandboxDirectories(arguments, workspaceRoot);

        if (effectiveSandboxMode == ToolSandboxMode.WorkspaceWrite)
        {
            arguments.Add("--bind");
            arguments.Add(workspaceRoot);
            arguments.Add(workspaceRoot);
        }
        else
        {
            arguments.Add("--ro-bind");
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

    private static void AddLinuxReadOnlySystemMounts(List<string> arguments)
    {
        foreach (string path in LinuxReadOnlySystemPaths)
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                continue;
            }

            arguments.Add("--ro-bind-try");
            arguments.Add(path);
            arguments.Add(path);
        }
    }

    private static void AddLinuxSandboxDirectories(
        List<string> arguments,
        string workspaceRoot)
    {
        HashSet<string> existingDirectories = new(
            ["/etc", "/tmp", "/var", "/var/tmp"],
            StringComparer.Ordinal);

        foreach (string directory in EnumerateParentDirectories(workspaceRoot))
        {
            if (!existingDirectories.Add(directory))
            {
                continue;
            }

            arguments.Add("--dir");
            arguments.Add(directory);
        }
    }

    private static IEnumerable<string> EnumerateParentDirectories(string path)
    {
        Stack<string> parents = new();
        string? parent = Path.GetDirectoryName(Path.GetFullPath(path));

        while (!string.IsNullOrWhiteSpace(parent) &&
               parent != Path.GetPathRoot(parent))
        {
            parents.Push(parent);
            parent = Path.GetDirectoryName(parent);
        }

        while (parents.Count > 0)
        {
            yield return parents.Pop();
        }
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

    private static ShellCommandSandboxPlan CreateWindowsPlan(
        ProcessExecutionRequest shellRequest,
        ToolSandboxMode effectiveSandboxMode,
        string workspaceRoot)
    {
        string modeSegment = effectiveSandboxMode == ToolSandboxMode.WorkspaceWrite
            ? "write"
            : "read";
        string workspaceHash = CreateShortHash(workspaceRoot);
        string profileName = $"NanoAgent.Shell.{modeSegment}.{workspaceHash}";
        string tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "NanoAgent",
            "sandbox-temp",
            workspaceHash,
            modeSegment);

        return new ShellCommandSandboxPlan(
            shellRequest with
            {
                WindowsSandbox = new WindowsSandboxConfiguration(
                    profileName,
                    workspaceRoot,
                    tempDirectory,
                    effectiveSandboxMode == ToolSandboxMode.WorkspaceWrite)
            },
            WindowsAppContainerEnforcement);
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
            $"{reason} The effective sandbox mode is '{ToWireValue(effectiveSandboxMode)}'. The command was blocked. Rerun with sandbox_permissions 'require_escalated' and a justification to allow full host access.");
    }

    private static string ToSandboxString(string value)
    {
        return "\"" +
               value
                   .Replace("\\", "\\\\", StringComparison.Ordinal)
                   .Replace("\"", "\\\"", StringComparison.Ordinal) +
               "\"";
    }

    private static string CreateShortHash(string value)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
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
