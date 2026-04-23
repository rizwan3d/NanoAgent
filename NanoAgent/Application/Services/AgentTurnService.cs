using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;

namespace NanoAgent.Application.Services;

internal sealed class AgentTurnService : IAgentTurnService
{
    private readonly IConversationPipeline _conversationPipeline;
    private readonly IAgentProfileResolver _profileResolver;

    public AgentTurnService(
        IConversationPipeline conversationPipeline,
        IAgentProfileResolver profileResolver)
    {
        _conversationPipeline = conversationPipeline;
        _profileResolver = profileResolver;
    }

    public async Task<ConversationTurnResult> RunTurnAsync(
        AgentTurnRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryParseLeadingAgentMention(
                request.UserInput,
                out string? agentName,
                out string? delegatedInput))
        {
            return await _conversationPipeline.ProcessAsync(
                request.UserInput,
                request.Session,
                request.ProgressSink,
                cancellationToken);
        }

        IAgentProfile mentionedProfile;
        try
        {
            mentionedProfile = _profileResolver.Resolve(agentName);
        }
        catch (ArgumentException)
        {
            return ConversationTurnResult.AssistantMessage(
                $"Unknown agent '@{agentName}'. Available subagents: {FormatProfileNames(_profileResolver.List().Where(static profile => profile.Mode == AgentProfileMode.Subagent))}.");
        }

        if (mentionedProfile.Mode != AgentProfileMode.Subagent)
        {
            return ConversationTurnResult.AssistantMessage(
                $"Agent '@{mentionedProfile.Name}' is a primary profile. Use /profile {mentionedProfile.Name} to switch primary profiles.");
        }

        if (string.IsNullOrWhiteSpace(delegatedInput))
        {
            return ConversationTurnResult.AssistantMessage(
                $"Tell '@{mentionedProfile.Name}' what to do, for example: @{mentionedProfile.Name} inspect the authentication flow.");
        }

        IAgentProfile originalProfile = request.Session.AgentProfile;
        request.Session.SetAgentProfile(mentionedProfile);

        try
        {
            return await _conversationPipeline.ProcessAsync(
                delegatedInput,
                request.Session,
                request.ProgressSink,
                cancellationToken);
        }
        finally
        {
            request.Session.SetAgentProfile(originalProfile);
        }
    }

    private static bool TryParseLeadingAgentMention(
        string input,
        out string agentName,
        out string delegatedInput)
    {
        agentName = string.Empty;
        delegatedInput = string.Empty;

        string trimmedInput = input.Trim();
        if (!trimmedInput.StartsWith('@'))
        {
            return false;
        }

        int index = 1;
        while (index < trimmedInput.Length && IsAgentNameCharacter(trimmedInput[index]))
        {
            index++;
        }

        if (index == 1)
        {
            return false;
        }

        agentName = trimmedInput[1..index];
        delegatedInput = trimmedInput[index..].Trim();
        return true;
    }

    private static bool IsAgentNameCharacter(char value)
    {
        return char.IsLetterOrDigit(value) ||
               value is '-' or '_';
    }

    private static string FormatProfileNames(IEnumerable<IAgentProfile> profiles)
    {
        return string.Join(
            ", ",
            profiles.Select(static profile => profile.Name));
    }
}
