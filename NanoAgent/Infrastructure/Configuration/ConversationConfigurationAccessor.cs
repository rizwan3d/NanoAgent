using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using Microsoft.Extensions.Options;

namespace NanoAgent.Infrastructure.Configuration;

internal sealed class ConversationConfigurationAccessor : IConversationConfigurationAccessor
{
    private readonly IOptions<ApplicationOptions> _options;

    public ConversationConfigurationAccessor(IOptions<ApplicationOptions> options)
    {
        _options = options;
    }

    public ConversationSettings GetSettings()
    {
        ConversationOptions conversation = _options.Value.Conversation ?? new ConversationOptions();
        string? systemPrompt = string.IsNullOrWhiteSpace(conversation.SystemPrompt)
            ? null
            : conversation.SystemPrompt.Trim();
        TimeSpan requestTimeout = conversation.RequestTimeoutSeconds <= 0
            ? Timeout.InfiniteTimeSpan
            : TimeSpan.FromSeconds(conversation.RequestTimeoutSeconds);

        return new ConversationSettings(
            systemPrompt,
            requestTimeout);
    }
}
