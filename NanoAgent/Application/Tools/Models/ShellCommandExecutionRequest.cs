namespace NanoAgent.Application.Tools.Models;

public sealed record ShellCommandExecutionRequest(
    string Command,
    string? WorkingDirectory,
    ShellCommandSandboxPermissions SandboxPermissions = ShellCommandSandboxPermissions.UseDefault,
    string? Justification = null,
    IReadOnlyList<string>? PrefixRule = null,
    bool PseudoTerminal = false);
