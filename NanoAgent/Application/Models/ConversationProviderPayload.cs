using NanoAgent.Domain.Models;

namespace NanoAgent.Application.Models;

public sealed record ConversationProviderPayload(
    ProviderKind ProviderKind,
    string RawContent,
    string? ResponseId,
    int RetryCount = 0);
