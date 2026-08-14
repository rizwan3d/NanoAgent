using StemCode.Application.Models;
using StemCode.Domain.Models;

namespace StemCode.Infrastructure.Conversation;

internal interface IConversationProviderHttpExecutor
{
    Task<ConversationProviderPayload> ExecuteAsync(
        ProviderKind providerKind,
        Func<HttpRequestMessage> createRequest,
        CancellationToken cancellationToken,
        Func<string, string>? normalizeResponseBody = null,
        Func<CancellationToken, Task<bool>>? refreshAuthorizationAsync = null,
        Func<ProviderRetryProgress, CancellationToken, Task>? onRetryAsync = null,
        Func<Stream, Func<string, CancellationToken, Task>?, CancellationToken, Task<StreamingResponseReadResult>>? readResponseBodyAsync = null,
        Func<string, CancellationToken, Task>? onAssistantMessageChunkAsync = null);
}
