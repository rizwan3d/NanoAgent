namespace NanoAgent.Application.Models;

public sealed class PermissionSettings
{
    public PermissionMode DefaultMode { get; set; } = PermissionMode.Ask;

    public PermissionRule[] Rules { get; set; } = [];

    public ToolSandboxMode SandboxMode { get; set; } = ToolSandboxMode.WorkspaceWrite;
}
