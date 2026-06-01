using System.Text.Json.Serialization;

namespace NanoAgent.Infrastructure.NanoAgentEnterprise;

[JsonSerializable(typeof(NanoAgentEnterpriseCredentials))]
[JsonSerializable(typeof(NanoAgentEnterpriseTokenResponse))]
internal sealed partial class NanoAgentEnterpriseJsonContext : JsonSerializerContext
{
}

internal sealed record NanoAgentEnterpriseCredentials(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("providerBaseUrl")] string ProviderBaseUrl,
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("expires")] long ExpiresAtUnixTimeMilliseconds);

internal sealed record NanoAgentEnterpriseTokenResponse(
    [property: JsonPropertyName("access_token")] string? AccessToken,
    [property: JsonPropertyName("token_type")] string? TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken);
