using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Commands;

namespace NanoAgent.Application.Commands;

internal sealed class ProfileCommandHandler : IReplCommandHandler
{
    private readonly IAgentProfileResolver _profileResolver;

    public ProfileCommandHandler(IAgentProfileResolver profileResolver)
    {
        _profileResolver = profileResolver;
    }

    public string CommandName => "profile";

    public string Description => "Switch the active agent profile for subsequent prompts.";

    public string Usage => "/profile <name>";

    public Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<IAgentProfile> availableProfiles = _profileResolver.List();
        if (string.IsNullOrWhiteSpace(context.ArgumentText))
        {
            return Task.FromResult(ReplCommandResult.Continue(
                CreateProfileSummary(context.Session, availableProfiles)));
        }

        string requestedProfileName = context.ArgumentText.Trim();
        IAgentProfile requestedProfile;
        try
        {
            requestedProfile = _profileResolver.Resolve(requestedProfileName);
        }
        catch (ArgumentException)
        {
            return Task.FromResult(ReplCommandResult.Continue(
                $"Unknown agent profile '{requestedProfileName}'. Available profiles: {string.Join(", ", availableProfiles.Select(static profile => profile.Name))}.",
                ReplFeedbackKind.Error));
        }

        if (string.Equals(
                context.Session.AgentProfile.Name,
                requestedProfile.Name,
                StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(ReplCommandResult.Continue(
                $"Already using '{requestedProfile.Name}'."));
        }

        context.Session.SetAgentProfile(requestedProfile);

        return Task.FromResult(ReplCommandResult.Continue(
            $"Active agent profile switched to '{requestedProfile.Name}'. Subsequent prompts in this session will use the '{requestedProfile.Name}' profile."));
    }

    private static string CreateProfileSummary(
        ReplSessionContext session,
        IReadOnlyList<IAgentProfile> availableProfiles)
    {
        string[] profileLines = availableProfiles
            .Select(profile =>
            {
                string suffix = string.Equals(
                    profile.Name,
                    session.AgentProfile.Name,
                    StringComparison.OrdinalIgnoreCase)
                    ? " (active)"
                    : string.Empty;

                return $"* {profile.Name}{suffix} [{profile.Mode.ToString().ToLowerInvariant()}] - {profile.Description}";
            })
            .ToArray();

        return
            $"Active agent profile: {session.AgentProfile.Name} - {session.AgentProfile.Description}\n" +
            $"Available profiles ({availableProfiles.Count}):\n" +
            string.Join("\n", profileLines) +
            "\nUse /profile <name> to switch profiles for this session, or start a prompt with @<subagent-name> for a one-turn subagent handoff.";
    }
}
