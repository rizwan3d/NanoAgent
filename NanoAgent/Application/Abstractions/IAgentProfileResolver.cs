namespace NanoAgent.Application.Abstractions;

public interface IAgentProfileResolver
{
    IAgentProfile Resolve(string? profileName);

    IReadOnlyList<IAgentProfile> ListProfiles();
}
