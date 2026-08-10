using StemCode.Domain.Models;

namespace StemCode.Application.Models;

public sealed record ConversationProviderPayload(
    ProviderKind ProviderKind,
    string RawContent,
    string? ResponseId,
    int RetryCount = 0,
    bool AssistantMessageWasStreamed = false);
