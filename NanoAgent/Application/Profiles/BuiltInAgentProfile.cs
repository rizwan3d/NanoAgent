using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;

namespace NanoAgent.Application.Profiles;

internal sealed class BuiltInAgentProfile : IAgentProfile
{
    public BuiltInAgentProfile(
        string name,
        AgentProfileMode mode,
        string description,
        string? systemPrompt,
        IReadOnlySet<string> enabledTools,
        AgentProfilePermissionOverlay permissionIntent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(enabledTools);
        ArgumentNullException.ThrowIfNull(permissionIntent);

        Name = name.Trim();
        Mode = mode;
        Description = description.Trim();
        SystemPrompt = string.IsNullOrWhiteSpace(systemPrompt)
            ? null
            : systemPrompt.Trim();
        EnabledTools = enabledTools;
        PermissionIntent = permissionIntent;
    }

    public string Name { get; }

    public AgentProfileMode Mode { get; }

    public string Description { get; }

    public string? SystemPrompt { get; }

    public IReadOnlySet<string> EnabledTools { get; }

    public AgentProfilePermissionOverlay PermissionIntent { get; }
}
