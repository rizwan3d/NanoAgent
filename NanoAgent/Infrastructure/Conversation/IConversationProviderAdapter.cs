using NanoAgent.Application.Models;

namespace NanoAgent.Infrastructure.Conversation;

internal interface IConversationProviderAdapter
{
    bool CanHandle(ConversationProviderRequest request);

    Task<ConversationProviderPayload> SendAsync(
        ConversationProviderRequest request,
        CancellationToken cancellationToken);
}
