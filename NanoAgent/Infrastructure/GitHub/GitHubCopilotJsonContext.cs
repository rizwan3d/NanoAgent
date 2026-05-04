using System.Text.Json.Serialization;

namespace NanoAgent.Infrastructure.GitHub;

[JsonSerializable(typeof(GitHubCopilotCredentials))]
internal sealed partial class GitHubCopilotJsonContext : JsonSerializerContext
{
}

internal sealed record GitHubCopilotCredentials(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("expires")] long ExpiresUnixMilliseconds,
    [property: JsonPropertyName("enterpriseDomain")] string? EnterpriseDomain,
    [property: JsonPropertyName("baseUrl")] string BaseUrl);
