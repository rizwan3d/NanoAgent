using StemCode.Application.Models;

namespace StemCode.Application.Abstractions;

public interface IConversationProviderClient
{
    Task<ConversationProviderPayload> SendAsync(
        ConversationProviderRequest request,
        CancellationToken cancellationToken);
}
