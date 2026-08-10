using Microsoft.Extensions.Options;
using StemCode.Application.Abstractions;
using StemCode.Application.Models;

namespace StemCode.Infrastructure.Configuration;

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
