using System.Text.Json.Serialization;

namespace NanoAgent.Infrastructure.OpenAi;

[JsonSerializable(typeof(OpenAiChatGptAccountCredentials))]
[JsonSerializable(typeof(OpenAiChatGptTokenResponse))]
internal sealed partial class OpenAiChatGptAccountJsonContext : JsonSerializerContext
{
}

internal sealed record OpenAiChatGptAccountCredentials(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("expires")] long ExpiresUnixMilliseconds,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("accountId")] string? AccountId);

internal sealed record OpenAiChatGptTokenResponse(
    [property: JsonPropertyName("access_token")] string? AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("id_token")] string? IdToken,
    [property: JsonPropertyName("expires_in")] int? ExpiresInSeconds,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("token_type")] string? TokenType);

