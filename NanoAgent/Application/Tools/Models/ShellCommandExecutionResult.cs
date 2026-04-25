namespace NanoAgent.Application.Tools.Models;

public sealed record ShellCommandExecutionResult(
    string Command,
    string WorkingDirectory,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    string SandboxPermissions = "use_default",
    string? Justification = null,
    string SandboxMode = "workspace-write",
    string SandboxEnforcement = "none",
    bool PseudoTerminal = false);
