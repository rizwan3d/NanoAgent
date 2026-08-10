using StemCode.Application.Utilities;

namespace StemCode.Application.Models;

public sealed record ConversationFailureInfo
{
    public ConversationFailureInfo(
        string category,
        string? providerName,
        string? modelId,
        bool isRetryable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);

        Category = SecretRedactor.Redact(category.Trim());
        ProviderName = NormalizeOptionalText(providerName);
        ModelId = NormalizeOptionalText(modelId);
        IsRetryable = isRetryable;
    }

    public string Category { get; }

    public bool IsRetryable { get; }

    public string? ModelId { get; }

    public string? ProviderName { get; }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : SecretRedactor.Redact(value.Trim());
    }
}
