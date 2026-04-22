namespace NanoAgent.Application.Models;

public sealed record AgentProfilePermissionOverlay(
    AgentProfileEditMode EditMode,
    AgentProfileShellMode ShellMode,
    string BehaviorIntent);

public enum AgentProfileEditMode
{
    AllowEdits,
    ReadOnly
}

public enum AgentProfileShellMode
{
    Default,
    SafeInspectionOnly
}
