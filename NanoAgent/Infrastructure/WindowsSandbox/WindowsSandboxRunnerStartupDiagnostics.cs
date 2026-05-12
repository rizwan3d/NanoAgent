using NanoAgent.Application.Utilities;
using System.ComponentModel;
using System.Globalization;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace NanoAgent.Infrastructure.WindowsSandbox;

internal sealed record WindowsSandboxRunnerStartupContext(
    string NanoAgentHome,
    string RunnerExecutablePath,
    string CommandLine,
    string Cwd,
    string Username,
    string Domain,
    uint CreationFlags,
    bool LogonWithProfile,
    bool EnvironmentBlockIsNull);

internal sealed record WindowsSandboxRunnerStartupDiagnostics(
    int ErrorCode,
    string ErrorMessage,
    string SuggestedFix,
    string SetupState,
    string Text)
{
    public static WindowsSandboxRunnerStartupDiagnostics FromWin32Error(
        int errorCode,
        WindowsSandboxRunnerStartupContext context,
        bool commandLineTooLong = false)
    {
        string formatted = new Win32Exception(errorCode).Message;
        string suggestedFix = SuggestedFixFor(errorCode, commandLineTooLong);
        string setupState = DescribeSetupState(context.NanoAgentHome);
        string runnerReadable = CanSandboxUserReadAndExecute(
            context.RunnerExecutablePath,
            context.Username,
            context.Domain);

        StringBuilder builder = new();
        builder.AppendLine("Failed to start Windows sandbox runner before executing the requested command.");
        builder.AppendLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"CreateProcessWithLogonW returned error {errorCode}:"));
        builder.AppendLine(formatted);
        builder.AppendLine("runner=" + context.RunnerExecutablePath);
        builder.AppendLine("user=" + context.Username);
        builder.AppendLine("domain=" + context.Domain);
        builder.AppendLine("command_line=" + SecretRedactor.Redact(context.CommandLine));
        builder.AppendLine("command_line_length=" + context.CommandLine.Length.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("cwd=" + context.Cwd);
        builder.AppendLine("cwd_exists=" + Directory.Exists(context.Cwd).ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("runner_exists=" + File.Exists(context.RunnerExecutablePath).ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("runner_readable_by_sandbox_user=" + runnerReadable);
        builder.AppendLine("creation_flags=0x" + context.CreationFlags.ToString("X8", CultureInfo.InvariantCulture));
        builder.AppendLine("logon_with_profile=" + context.LogonWithProfile.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("environment_block=" + (context.EnvironmentBlockIsNull ? "NULL" : "explicit"));
        builder.AppendLine("setup_state=" + setupState);
        builder.AppendLine("setup_marker=" + WindowsSandboxPaths.SetupMarkerPath(context.NanoAgentHome));
        builder.AppendLine("sandbox_users=" + WindowsSandboxPaths.SandboxUsersPath(context.NanoAgentHome));
        builder.AppendLine("helper_binary_path=" + WindowsSandboxPaths.SandboxBinDir(context.NanoAgentHome));
        builder.Append("suggested_fix=" + suggestedFix);

        return new WindowsSandboxRunnerStartupDiagnostics(
            errorCode,
            formatted,
            suggestedFix,
            setupState,
            builder.ToString());
    }

    public static string SuggestedFixFor(int errorCode, bool commandLineTooLong = false)
    {
        if (commandLineTooLong)
        {
            return "command-line-too-long; move the runner payload to named pipe IPC and keep CreateProcessWithLogonW arguments under 1024 characters";
        }

        return (uint)errorCode switch
        {
            1326 => "rerun elevated sandbox setup; regenerate sandbox user secrets; ensure sandbox_users.json matches actual local users",
            1385 => "grant local logon right or recreate sandbox users through elevated setup",
            5 => "refresh ACLs; verify .sandbox-bin and runner path ACLs; verify cwd ACLs",
            2 => "resolve runner to an absolute path; materialize helper binaries into .sandbox-bin; do not rely only on sandbox user PATH",
            3 => "verify runner path and cwd exist for the sandbox user",
            267 => "use a valid existing cwd",
            740 => "choose a runner or shell that does not require elevation",
            1058 => "check Windows services required for secondary logon/profile loading",
            _ => "rerun sandbox setup and inspect the raw Windows error"
        };
    }

    public void Log(string nanoAgentHome)
    {
        WindowsSandboxLog.Write(
            nanoAgentHome,
            "runner startup failure:" + Environment.NewLine + Text);
    }

    private static string DescribeSetupState(string nanoAgentHome)
    {
        string markerPath = WindowsSandboxPaths.SetupMarkerPath(nanoAgentHome);
        string usersPath = WindowsSandboxPaths.SandboxUsersPath(nanoAgentHome);
        bool markerExists = File.Exists(markerPath);
        bool usersExists = File.Exists(usersPath);
        int? markerVersion = TryReadVersion(markerPath, WindowsSandboxJsonContext.Default.WindowsSandboxSetupMarker);
        int? usersVersion = TryReadVersion(usersPath, WindowsSandboxJsonContext.Default.WindowsSandboxUsersFile);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"marker_exists={markerExists}; marker_version={FormatVersion(markerVersion)}; users_exists={usersExists}; users_version={FormatVersion(usersVersion)}; expected_version={WindowsSandboxPaths.SetupVersion}");
    }

    private static int? TryReadVersion<T>(
        string path,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> jsonTypeInfo)
        where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            object? value = JsonSerializer.Deserialize(File.ReadAllText(path), jsonTypeInfo);
            return value switch
            {
                WindowsSandboxSetupMarker marker => marker.Version,
                WindowsSandboxUsersFile users => users.Version,
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static string FormatVersion(int? version)
    {
        return version is null
            ? "unknown"
            : version.Value.ToString(CultureInfo.InvariantCulture);
    }

    private static string CanSandboxUserReadAndExecute(
        string runnerExecutablePath,
        string username,
        string domain)
    {
        if (!File.Exists(runnerExecutablePath))
        {
            return "false";
        }

        try
        {
            SecurityIdentifier userSid = ResolveAccountSid(username, domain);
            FileSecurity security = new FileInfo(runnerExecutablePath).GetAccessControl();
            AuthorizationRuleCollection rules = security.GetAccessRules(
                includeExplicit: true,
                includeInherited: true,
                typeof(SecurityIdentifier));
            const FileSystemRights required = FileSystemRights.ReadAndExecute;
            bool allowed = false;
            foreach (FileSystemAccessRule rule in rules.OfType<FileSystemAccessRule>())
            {
                if (rule.IdentityReference != userSid ||
                    (rule.FileSystemRights & required) != required)
                {
                    continue;
                }

                if (rule.AccessControlType == AccessControlType.Deny)
                {
                    return "false";
                }

                allowed = true;
            }

            return allowed.ToString(CultureInfo.InvariantCulture);
        }
        catch
        {
            return "unknown";
        }
    }

    private static SecurityIdentifier ResolveAccountSid(string username, string domain)
    {
        NTAccount account = string.IsNullOrWhiteSpace(domain) || domain == "."
            ? new NTAccount(Environment.MachineName, username)
            : new NTAccount(domain, username);
        return (SecurityIdentifier)account.Translate(typeof(SecurityIdentifier));
    }
}
