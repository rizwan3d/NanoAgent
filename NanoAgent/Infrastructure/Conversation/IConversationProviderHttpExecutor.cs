using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;

namespace NanoAgent.Infrastructure.Conversation;

internal interface IConversationProviderHttpExecutor
{
    Task<ConversationProviderPayload> ExecuteAsync(
        ProviderKind providerKind,
        string requestBody,
        Func<HttpRequestMessage> createRequest,
        CancellationToken cancellationToken,
        Func<string, string>? normalizeResponseBody = null,
        Func<CancellationToken, Task<bool>>? refreshAuthorizationAsync = null);
}
