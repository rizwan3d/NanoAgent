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

    /// <summary>
    /// Whether tool results should render their complete output (<see langword="true"/>)
    /// or the compact preview (<see langword="false"/>) while this profile is active.
    /// <see langword="null"/> means the profile expresses no preference and the session default applies.
    /// </summary>
    bool? FullToolOutput { get; }
}
