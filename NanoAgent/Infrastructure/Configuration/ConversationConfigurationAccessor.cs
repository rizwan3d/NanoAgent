using Microsoft.Extensions.Options;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;

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
        return ApplicationSettingsFactory.CreateConversationSettings(_options.Value);
    }
}
