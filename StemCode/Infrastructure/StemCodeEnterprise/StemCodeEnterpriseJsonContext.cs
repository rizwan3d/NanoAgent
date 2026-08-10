using System.Text.Json.Serialization;

namespace StemCode.Infrastructure.StemCodeEnterprise;

[JsonSerializable(typeof(StemCodeEnterpriseCredentials))]
[JsonSerializable(typeof(StemCodeEnterpriseTokenResponse))]
internal sealed partial class StemCodeEnterpriseJsonContext : JsonSerializerContext
{
}

internal sealed record StemCodeEnterpriseCredentials(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("providerBaseUrl")] string ProviderBaseUrl,
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("expires")] long ExpiresAtUnixTimeMilliseconds);

internal sealed record StemCodeEnterpriseTokenResponse(
    [property: JsonPropertyName("access_token")] string? AccessToken,
    [property: JsonPropertyName("token_type")] string? TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken);
