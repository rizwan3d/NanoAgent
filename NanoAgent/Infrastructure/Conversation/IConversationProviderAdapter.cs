using NanoAgent.Application.Models;

namespace NanoAgent.Infrastructure.Conversation;

internal interface IConversationProviderAdapter
{
    Task<ConversationProviderPayload> SendAsync(
        ConversationProviderRequest request,
        CancellationToken cancellationToken);
}
