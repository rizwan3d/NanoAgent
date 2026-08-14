using StemCode.Application.Models;

namespace StemCode.Infrastructure.Conversation;

internal interface IConversationProviderAdapter
{
    Task<ConversationProviderPayload> SendAsync(
        ConversationProviderRequest request,
        CancellationToken cancellationToken);
}
