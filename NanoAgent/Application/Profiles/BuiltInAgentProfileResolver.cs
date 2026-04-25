using NanoAgent.Application.Abstractions;

namespace NanoAgent.Application.Profiles;

internal sealed class BuiltInAgentProfileResolver : IAgentProfileResolver
{
    private readonly IWorkspaceRootProvider? _workspaceRootProvider;

    public BuiltInAgentProfileResolver()
    {
    }

    public BuiltInAgentProfileResolver(IWorkspaceRootProvider workspaceRootProvider)
    {
        _workspaceRootProvider = workspaceRootProvider;
    }

    public IReadOnlyList<IAgentProfile> List()
    {
        IReadOnlyList<IAgentProfile> workspaceProfiles = LoadWorkspaceProfiles();
        if (workspaceProfiles.Count == 0)
        {
            return BuiltInAgentProfiles.All;
        }

        List<IAgentProfile> profiles = [.. BuiltInAgentProfiles.All];
        HashSet<string> existingNames = new(
            profiles.Select(static profile => profile.Name),
            StringComparer.OrdinalIgnoreCase);

        foreach (IAgentProfile profile in workspaceProfiles)
        {
            if (existingNames.Add(profile.Name))
            {
                profiles.Add(profile);
            }
        }

        return profiles;
    }

    public IAgentProfile Resolve(string? profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return BuiltInAgentProfiles.Build;
        }

        string normalizedProfileName = profileName.Trim();
        IReadOnlyList<IAgentProfile> profiles = List();
        return profiles.FirstOrDefault(profile =>
            string.Equals(profile.Name, normalizedProfileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException(
                $"Unknown agent profile '{normalizedProfileName}'. Available profiles: {string.Join(", ", profiles.Select(static profile => profile.Name))}.",
                nameof(profileName));
    }

    private IReadOnlyList<IAgentProfile> LoadWorkspaceProfiles()
    {
        if (_workspaceRootProvider is null)
        {
            return [];
        }

        string workspaceRoot;
        try
        {
            workspaceRoot = _workspaceRootProvider.GetWorkspaceRoot();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        return WorkspaceAgentProfileLoader.Load(workspaceRoot);
    }
}
