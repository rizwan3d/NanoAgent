using NanoAgent.Application.Abstractions;

namespace NanoAgent.Application.Profiles;

internal sealed class BuiltInAgentProfileResolver : IAgentProfileResolver
{
    public IReadOnlyList<IAgentProfile> ListProfiles()
    {
        return BuiltInAgentProfiles.All;
    }

    public IAgentProfile Resolve(string? profileName)
    {
        return BuiltInAgentProfiles.Resolve(profileName);
    }
}
