using NanoAgent.Domain.Services;

namespace NanoAgent.Domain.Models;

public static class AgentProviderProfileExtensions
{
    public static string ResolveBaseUrl(this AgentProviderProfile providerProfile)
    {
        ArgumentNullException.ThrowIfNull(providerProfile);

        string? managedBaseUrl = providerProfile.ProviderKind.GetManagedBaseUrl();
        if (!string.IsNullOrWhiteSpace(managedBaseUrl))
        {
            return managedBaseUrl;
        }

        if (!string.IsNullOrWhiteSpace(providerProfile.BaseUrl))
        {
            return CompatibleProviderBaseUrlNormalizer.Normalize(providerProfile.BaseUrl);
        }

        throw new InvalidOperationException(
            $"The configured {providerProfile.ProviderKind.ToDisplayName()} is missing a base URL.");
    }

    public static Uri ResolveBaseUri(this AgentProviderProfile providerProfile)
    {
        string resolvedBaseUrl = providerProfile.ResolveBaseUrl();
        string baseUri = resolvedBaseUrl.EndsWith("/", StringComparison.Ordinal)
            ? resolvedBaseUrl
            : $"{resolvedBaseUrl}/";

        return new Uri(baseUri);
    }
}
