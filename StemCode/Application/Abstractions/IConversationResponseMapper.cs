using StemCode.Application.Models;

namespace StemCode.Application.Abstractions;

public interface IConversationResponseMapper
{
    ConversationResponse Map(ConversationProviderPayload payload);
}
