using NanoAgent.Application.Abstractions;

namespace NanoAgent.Application.Models;

public sealed class ToolRegistration
{
    public ToolRegistration(
        ITool tool,
        ToolPermissionPolicy permissionPolicy)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(permissionPolicy);

        Tool = tool;
        PermissionPolicy = permissionPolicy;
    }

    public string Name => Tool.Name;

    public ToolPermissionPolicy PermissionPolicy { get; }

    public ITool Tool { get; }
}
