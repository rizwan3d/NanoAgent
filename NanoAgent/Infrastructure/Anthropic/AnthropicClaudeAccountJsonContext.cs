using System.Text.Json.Serialization;

namespace NanoAgent.Infrastructure.Anthropic;

[JsonSerializable(typeof(AnthropicClaudeAccountCredentials))]
[JsonSerializable(typeof(AnthropicClaudeTokenRequest))]
[JsonSerializable(typeof(AnthropicClaudeTokenResponse))]
internal sealed partial class AnthropicClaudeAccountJsonContext : JsonSerializerContext
{
}

internal sealed record AnthropicClaudeAccountCredentials(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("expires")] long ExpiresUnixMilliseconds);

internal sealed record AnthropicClaudeTokenRequest(
    [property: JsonPropertyName("grant_type")] string GrantType,
    [property: JsonPropertyName("client_id")] string ClientId,
    [property: JsonPropertyName("code")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Code = null,
    [property: JsonPropertyName("state")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? State = null,
    [property: JsonPropertyName("redirect_uri")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RedirectUri = null,
    [property: JsonPropertyName("code_verifier")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CodeVerifier = null,
    [property: JsonPropertyName("refresh_token")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RefreshToken = null);

internal sealed record AnthropicClaudeTokenResponse(
    [property: JsonPropertyName("access_token")] string? AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("expires_in")] int? ExpiresInSeconds,
    [property: JsonPropertyName("scope")] string? Scope,
    [property: JsonPropertyName("token_type")] string? TokenType);
