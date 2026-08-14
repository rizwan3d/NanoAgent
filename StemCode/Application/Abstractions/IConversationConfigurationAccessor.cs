using StemCode.Application.Models;

namespace StemCode.Application.Abstractions;

public interface IConversationConfigurationAccessor
{
    ConversationSettings GetSettings();
}
