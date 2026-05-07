using System.Text.Json.Serialization;

namespace NanoAgent.Infrastructure.Google;

[JsonSerializable(typeof(GoogleCodeAssistCredentials))]
[JsonSerializable(typeof(GoogleCodeAssistTokenResponse))]
internal sealed partial class GoogleCodeAssistJsonContext : JsonSerializerContext
{
}

internal sealed record GoogleCodeAssistCredentials(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("expires")] long ExpiresUnixMilliseconds,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("project_id")] string? ProjectId);

internal sealed record GoogleCodeAssistTokenResponse(
    [property: JsonPropertyName("access_token")] string? AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("expires_in")] int? ExpiresInSeconds,
    [property: JsonPropertyName("scope")] string? Scope,
    [property: JsonPropertyName("token_type")] string? TokenType,
    [property: JsonPropertyName("id_token")] string? IdToken);
