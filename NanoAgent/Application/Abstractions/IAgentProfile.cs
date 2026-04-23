using NanoAgent.Application.Models;

namespace NanoAgent.Application.Abstractions;

public interface IAgentProfile
{
    string Name { get; }

    AgentProfileMode Mode { get; }

    string Description { get; }

    string? SystemPrompt { get; }

    IReadOnlySet<string> EnabledTools { get; }

    AgentProfilePermissionOverlay PermissionIntent { get; }
}
