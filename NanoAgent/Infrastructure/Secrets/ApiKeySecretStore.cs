using NanoAgent.Application.Abstractions;
using NanoAgent.Infrastructure.Configuration;

namespace NanoAgent.Infrastructure.Secrets;

internal sealed class ApiKeySecretStore : IApiKeySecretStore
{
    private const string ApiKeyAccountName = "default-api-key";
    private const string ApiKeyEnvironmentVariableName = "NANOAGENT_API_KEY";
    private const int MaxProviderAccountNameLength = 128;

    private readonly IPlatformCredentialStore _platformCredentialStore;

    public ApiKeySecretStore(IPlatformCredentialStore platformCredentialStore)
    {
        _platformCredentialStore = platformCredentialStore;
    }

    public Task<string?> LoadAsync(CancellationToken cancellationToken)
    {
        return LoadAsync(providerName: null, cancellationToken);
    }

    public Task<string?> LoadAsync(string? providerName, CancellationToken cancellationToken)
    {
        string? environmentApiKey = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(environmentApiKey))
        {
            return Task.FromResult<string?>(environmentApiKey.Trim());
        }

        return _platformCredentialStore.LoadAsync(BuildReference(providerName), cancellationToken);
    }

    public Task SaveAsync(string apiKey, CancellationToken cancellationToken)
    {
        return SaveAsync(providerName: null, apiKey, cancellationToken);
    }

    public Task SaveAsync(string? providerName, string apiKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        return _platformCredentialStore.SaveAsync(
            BuildReference(providerName),
            apiKey.Trim(),
            cancellationToken);
    }

    private SecretReference BuildReference(string? providerName)
    {
        string? normalizedProviderName = NormalizeProviderName(providerName);
        string accountName = normalizedProviderName is null
            ? ApiKeyAccountName
            : "provider-" + normalizedProviderName;
        string displayLabel = normalizedProviderName is null
            ? $"{ApplicationIdentity.ProductName} API key"
            : $"{ApplicationIdentity.ProductName} API key ({providerName!.Trim()})";

        return new SecretReference(
            ApplicationIdentity.ProductName,
            accountName,
            displayLabel);
    }

    private static string? NormalizeProviderName(string? providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            return null;
        }

        string normalized = new(
            providerName
                .Trim()
                .Select(static character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-')
                .ToArray());

        normalized = string.Join(
            '-',
            normalized.Split('-', StringSplitOptions.RemoveEmptyEntries));

        if (normalized.Length > MaxProviderAccountNameLength)
        {
            normalized = normalized[..MaxProviderAccountNameLength].TrimEnd('-');
        }

        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : normalized;
    }
}
