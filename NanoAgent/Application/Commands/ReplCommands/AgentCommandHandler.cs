using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;

namespace NanoAgent.Application.Commands;

internal sealed class AgentCommandHandler : IReplCommandHandler
{
    private readonly IAgentProfileResolver _profileResolver;

    public AgentCommandHandler(IAgentProfileResolver profileResolver)
    {
        _profileResolver = profileResolver;
    }

    public string CommandName => "agent";

    public string Description => "List available subagents for delegated work.";

    public string Usage => "/agent";

    public Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(ReplCommandResult.Continue(
            AgentCommandSupport.CreateSubagentSummary(context.Session, _profileResolver.List())));
    }
}

internal sealed class AgentAliasCommandHandler : IReplCommandHandler
{
    private readonly IAgentProfileResolver _profileResolver;

    public AgentAliasCommandHandler(IAgentProfileResolver profileResolver)
    {
        _profileResolver = profileResolver;
    }

    public string CommandName => "a";

    public string Description => "Alias for /agent.";

    public string Usage => "/a";

    public Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(ReplCommandResult.Continue(
            AgentCommandSupport.CreateSubagentSummary(context.Session, _profileResolver.List())));
    }
}

internal static class AgentCommandSupport
{
    public static string CreateSubagentSummary(
        ReplSessionContext session,
        IReadOnlyList<IAgentProfile> availableProfiles)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(availableProfiles);

        IAgentProfile[] subagents = availableProfiles
            .Where(static profile => profile.Mode == AgentProfileMode.Subagent)
            .ToArray();

        if (subagents.Length == 0)
        {
            return
                $"Active agent profile: {session.AgentProfile.Name} - {session.AgentProfile.Description}\n" +
                "No subagents are currently available.";
        }

        string[] subagentLines = subagents
            .Select(static profile => $"* {profile.Name} - {profile.Description}")
            .ToArray();

        return
            $"Active agent profile: {session.AgentProfile.Name} - {session.AgentProfile.Description}\n" +
            $"Available subagents ({subagents.Length}):\n" +
            string.Join("\n", subagentLines) +
            "\nUse @<subagent-name> for a one-turn handoff, or use agent_delegate / agent_orchestrate from a primary agent.";
    }
}
