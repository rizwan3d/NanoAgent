using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;

namespace NanoAgent.Application.Profiles;

internal sealed class BuiltInAgentProfile : IAgentProfile
{
    public BuiltInAgentProfile(
        string name,
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
        Description = description.Trim();
        SystemPrompt = string.IsNullOrWhiteSpace(systemPrompt)
            ? null
            : systemPrompt.Trim();
        EnabledTools = enabledTools;
        PermissionIntent = permissionIntent;
    }

    public string Name { get; }

    public string Description { get; }

    public string? SystemPrompt { get; }

    public IReadOnlySet<string> EnabledTools { get; }

    public AgentProfilePermissionOverlay PermissionIntent { get; }
}
