namespace NanoAgent.Application.Models;

public sealed class ShellCommandPermissionPolicy
{
    public string CommandArgumentName { get; set; } = "command";

    public string JustificationArgumentName { get; set; } = "justification";

    public string PrefixRuleArgumentName { get; set; } = "prefix_rule";

    public string SandboxPermissionsArgumentName { get; set; } = "sandbox_permissions";
}
