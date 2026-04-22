using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;

namespace NanoAgent.Application.Profiles;

internal sealed class BuiltInAgentProfile : IAgentProfile
{
    public BuiltInAgentProfile(
        string name,
        string description,
        string? systemPromptContribution,
        IReadOnlySet<string> enabledTools,
        AgentProfilePermissionOverlay permissionOverlay)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(enabledTools);
        ArgumentNullException.ThrowIfNull(permissionOverlay);

        Name = name.Trim();
        Description = description.Trim();
        SystemPromptContribution = string.IsNullOrWhiteSpace(systemPromptContribution)
            ? null
            : systemPromptContribution.Trim();
        EnabledTools = enabledTools;
        PermissionOverlay = permissionOverlay;
    }

    public string Name { get; }

    public string Description { get; }

    public string? SystemPromptContribution { get; }

    public IReadOnlySet<string> EnabledTools { get; }

    public AgentProfilePermissionOverlay PermissionOverlay { get; }
}
