using Microsoft.Extensions.Logging;
using NanoAgent.Application.Abstractions;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NanoAgent.Infrastructure.Anthropic;

internal sealed class AnthropicClaudeAccountCredentialService :
    IAnthropicClaudeAccountAuthenticator,
    IAnthropicClaudeAccountCredentialService
{
    private const int CallbackPort = 53692;
    private const string CallbackPath = "/callback";
    private const string AuthorizationEndpoint = "https://claude.ai/oauth/authorize";
    private const string TokenEndpoint = "https://platform.claude.com/v1/oauth/token";
    private const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
    private const string RedirectUri = "http://localhost:53692/callback";
    private const string Scopes = "org:create_api_key user:profile user:inference user:sessions:claude_code user:mcp_servers user:file_upload";
    private const string CredentialType = "anthropic-claude-account";
    private static readonly TimeSpan CallbackTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TokenExpiryBuffer = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient;
    private readonly IApiKeySecretStore _secretStore;
    private readonly IStatusMessageWriter _statusMessageWriter;
    private readonly ILogger<AnthropicClaudeAccountCredentialService> _logger;

    public AnthropicClaudeAccountCredentialService(
        HttpClient httpClient,
        IApiKeySecretStore secretStore,
        IStatusMessageWriter statusMessageWriter,
        ILogger<AnthropicClaudeAccountCredentialService> logger)
    {
        _httpClient = httpClient;
        _secretStore = secretStore;
        _statusMessageWriter = statusMessageWriter;
        _logger = logger;
    }

    public async Task<string> AuthenticateAsync(CancellationToken cancellationToken)
    {
        string codeVerifier = GenerateCodeVerifier();
        string codeChallenge = GenerateCodeChallenge(codeVerifier);
        string state = codeVerifier;
        string authorizationUrl = BuildAuthorizationUrl(codeChallenge, state);

        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(CallbackTimeout);

        using TcpListener listener = TcpListener.Create(CallbackPort);
        listener.Start();

        await _statusMessageWriter.ShowInfoAsync(
            "Opening browser for Anthropic Claude Pro/Max sign-in.",
            cancellationToken);
        await _statusMessageWriter.ShowInfoAsync(
            $"If the browser does not open, visit: {authorizationUrl}",
            cancellationToken);

        if (!TryOpenBrowser(authorizationUrl))
        {
            _logger.LogInformation("Unable to open the system browser for Anthropic Claude Pro/Max sign-in.");
        }

        AnthropicAuthorizationCallback callback = await WaitForCallbackAsync(
            listener,
            state,
            timeoutSource.Token);
        AnthropicClaudeAccountCredentials credentials = await ExchangeCodeForCredentialsAsync(
            callback.Code,
            callback.State,
            codeVerifier,
            cancellationToken);

        await _statusMessageWriter.ShowSuccessAsync(
            "Anthropic Claude Pro/Max sign-in completed.",
            cancellationToken);

        return SerializeCredentials(credentials);
    }

    public async Task<AnthropicClaudeAccountResolvedCredential> ResolveAsync(
        string storedCredentials,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storedCredentials);

        string credentialsJson = storedCredentials;
        if (forceRefresh)
        {
            credentialsJson = await _secretStore.LoadAsync(cancellationToken) ?? storedCredentials;
        }

        AnthropicClaudeAccountCredentials credentials = DeserializeCredentials(credentialsJson);
        if (forceRefresh || IsExpired(credentials))
        {
            credentials = await RefreshCredentialsAsync(credentials, cancellationToken);
            await _secretStore.SaveAsync(SerializeCredentials(credentials), cancellationToken);
        }

        return new AnthropicClaudeAccountResolvedCredential(credentials.AccessToken);
    }

    private static string GenerateCodeVerifier()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return ToBase64Url(bytes);
    }

    private static string GenerateCodeChallenge(string codeVerifier)
    {
        byte[] hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return ToBase64Url(hash);
    }

    private static string BuildAuthorizationUrl(string codeChallenge, string state)
    {
        Dictionary<string, string> parameters = new(StringComparer.Ordinal)
        {
            ["code"] = "true",
            ["client_id"] = ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = RedirectUri,
            ["scope"] = Scopes,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state
        };

        string query = string.Join(
            "&",
            parameters.Select(static pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

        return $"{AuthorizationEndpoint}?{query}";
    }

    private async Task<AnthropicAuthorizationCallback> WaitForCallbackAsync(
        TcpListener listener,
        string expectedState,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
            AnthropicAuthorizationCallback? callback = await TryHandleCallbackClientAsync(
                client,
                expectedState,
                cancellationToken);
            if (callback is not null)
            {
                return callback;
            }
        }
    }

    private static async Task<AnthropicAuthorizationCallback?> TryHandleCallbackClientAsync(
        TcpClient client,
        string expectedState,
        CancellationToken cancellationToken)
    {
        await using NetworkStream stream = client.GetStream();
        using StreamReader reader = new(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

        string? requestLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(requestLine))
        {
            return null;
        }

        while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cancellationToken)))
        {
        }

        string[] requestParts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (requestParts.Length < 2 ||
            !Uri.TryCreate(new Uri($"http://localhost:{CallbackPort}"), requestParts[1], out Uri? requestUri))
        {
            await WriteHttpResponseAsync(stream, HttpStatusCode.BadRequest, "Invalid callback request.", cancellationToken);
            return null;
        }

        if (!string.Equals(requestUri.AbsolutePath, CallbackPath, StringComparison.Ordinal))
        {
            await WriteHttpResponseAsync(stream, HttpStatusCode.NotFound, "Not Found", cancellationToken);
            return null;
        }

        Dictionary<string, string?> query = ParseQuery(requestUri.Query);
        if (NormalizeOrNull(query.GetValueOrDefault("error")) is string error)
        {
            await WriteHttpResponseAsync(
                stream,
                HttpStatusCode.BadRequest,
                $"Authentication failed: {WebUtility.HtmlEncode(error)}",
                cancellationToken);
            throw new InvalidOperationException($"Anthropic Claude Pro/Max authentication failed: {error}");
        }

        string? code = NormalizeOrNull(query.GetValueOrDefault("code"));
        string? state = NormalizeOrNull(query.GetValueOrDefault("state"));
        if (code is null || state is null)
        {
            await WriteHttpResponseAsync(stream, HttpStatusCode.BadRequest, "Missing callback parameters.", cancellationToken);
            throw new InvalidOperationException("Anthropic Claude Pro/Max authentication returned an incomplete callback.");
        }

        if (!string.Equals(state, expectedState, StringComparison.Ordinal))
        {
            await WriteHttpResponseAsync(stream, HttpStatusCode.BadRequest, "State mismatch.", cancellationToken);
            throw new InvalidOperationException("Anthropic Claude Pro/Max authentication state did not match.");
        }

        await WriteHttpResponseAsync(
            stream,
            HttpStatusCode.OK,
            """
            <!doctype html>
            <html lang="en">
            <head><meta charset="utf-8"><title>Authentication complete</title></head>
            <body style="font-family: sans-serif; margin: 3rem;">
            <h1>Authentication complete</h1>
            <p>You can close this window and return to NanoAgent.</p>
            </body>
            </html>
            """,
            cancellationToken);

        return new AnthropicAuthorizationCallback(code, state);
    }

    private async Task<AnthropicClaudeAccountCredentials> ExchangeCodeForCredentialsAsync(
        string code,
        string state,
        string codeVerifier,
        CancellationToken cancellationToken)
    {
        AnthropicClaudeTokenRequest request = new(
            "authorization_code",
            ClientId,
            Code: code,
            State: state,
            RedirectUri: RedirectUri,
            CodeVerifier: codeVerifier);

        AnthropicClaudeTokenResponse tokenResponse = await SendTokenRequestAsync(request, cancellationToken);
        if (string.IsNullOrWhiteSpace(tokenResponse.RefreshToken))
        {
            throw new InvalidOperationException(
                "Anthropic Claude Pro/Max authentication did not return a refresh token.");
        }

        return CreateCredentials(tokenResponse, existingCredentials: null);
    }

    private async Task<AnthropicClaudeAccountCredentials> RefreshCredentialsAsync(
        AnthropicClaudeAccountCredentials credentials,
        CancellationToken cancellationToken)
    {
        AnthropicClaudeTokenRequest request = new(
            "refresh_token",
            ClientId,
            RefreshToken: credentials.RefreshToken);

        AnthropicClaudeTokenResponse tokenResponse = await SendTokenRequestAsync(request, cancellationToken);
        return CreateCredentials(tokenResponse, credentials);
    }

    private async Task<AnthropicClaudeTokenResponse> SendTokenRequestAsync(
        AnthropicClaudeTokenRequest tokenRequest,
        CancellationToken cancellationToken)
    {
        string requestBody = JsonSerializer.Serialize(
            tokenRequest,
            AnthropicClaudeAccountJsonContext.Default.AnthropicClaudeTokenRequest);

        using StringContent content = new(requestBody, Encoding.UTF8, "application/json");
        using HttpRequestMessage request = new(HttpMethod.Post, TokenEndpoint)
        {
            Content = content
        };
        request.Headers.Accept.ParseAdd("application/json");

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Anthropic Claude Pro/Max token request failed with HTTP {(int)response.StatusCode}: {Truncate(responseBody.Trim(), 300)}");
        }

        AnthropicClaudeTokenResponse? tokenResponse = JsonSerializer.Deserialize(
            responseBody,
            AnthropicClaudeAccountJsonContext.Default.AnthropicClaudeTokenResponse);

        if (tokenResponse?.AccessToken is null || tokenResponse.ExpiresInSeconds is not > 0)
        {
            throw new InvalidOperationException(
                "Anthropic Claude Pro/Max token response was missing required fields.");
        }

        return tokenResponse;
    }

    private static AnthropicClaudeAccountCredentials CreateCredentials(
        AnthropicClaudeTokenResponse tokenResponse,
        AnthropicClaudeAccountCredentials? existingCredentials)
    {
        string accessToken = NormalizeOrNull(tokenResponse.AccessToken)
            ?? throw new InvalidOperationException("Anthropic Claude Pro/Max token response did not include an access token.");
        string refreshToken = NormalizeOrNull(tokenResponse.RefreshToken)
            ?? existingCredentials?.RefreshToken
            ?? throw new InvalidOperationException("Anthropic Claude Pro/Max token response did not include a refresh token.");
        long expires = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresInSeconds.GetValueOrDefault()).ToUnixTimeMilliseconds();

        return new AnthropicClaudeAccountCredentials(
            CredentialType,
            accessToken,
            refreshToken,
            expires);
    }

    private static AnthropicClaudeAccountCredentials DeserializeCredentials(string value)
    {
        try
        {
            AnthropicClaudeAccountCredentials? credentials = JsonSerializer.Deserialize(
                value,
                AnthropicClaudeAccountJsonContext.Default.AnthropicClaudeAccountCredentials);

            if (credentials is null ||
                !string.Equals(credentials.Type, CredentialType, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(credentials.AccessToken) ||
                string.IsNullOrWhiteSpace(credentials.RefreshToken))
            {
                throw new InvalidOperationException();
            }

            return credentials;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "Stored Anthropic Claude Pro/Max credentials are invalid. Run onboarding again to sign in.",
                exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                "Stored Anthropic Claude Pro/Max credentials are incomplete. Run onboarding again to sign in.",
                exception);
        }
    }

    private static string SerializeCredentials(AnthropicClaudeAccountCredentials credentials)
    {
        return JsonSerializer.Serialize(
            credentials,
            AnthropicClaudeAccountJsonContext.Default.AnthropicClaudeAccountCredentials);
    }

    private static bool IsExpired(AnthropicClaudeAccountCredentials credentials)
    {
        long refreshAfter = credentials.ExpiresUnixMilliseconds - (long)TokenExpiryBuffer.TotalMilliseconds;
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() >= refreshAfter;
    }

    private static Dictionary<string, string?> ParseQuery(string query)
    {
        Dictionary<string, string?> values = new(StringComparer.Ordinal);
        string normalizedQuery = query.StartsWith("?", StringComparison.Ordinal)
            ? query[1..]
            : query;

        foreach (string part in normalizedQuery.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pieces = part.Split('=', 2);
            string key = Uri.UnescapeDataString(pieces[0].Replace('+', ' '));
            string? value = pieces.Length == 2
                ? Uri.UnescapeDataString(pieces[1].Replace('+', ' '))
                : null;
            values[key] = value;
        }

        return values;
    }

    private static async Task WriteHttpResponseAsync(
        Stream stream,
        HttpStatusCode statusCode,
        string body,
        CancellationToken cancellationToken)
    {
        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
        string reasonPhrase = statusCode switch
        {
            HttpStatusCode.OK => "OK",
            HttpStatusCode.BadRequest => "Bad Request",
            HttpStatusCode.NotFound => "Not Found",
            _ => statusCode.ToString()
        };
        string headers =
            $"HTTP/1.1 {(int)statusCode} {reasonPhrase}\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            "Connection: close\r\n" +
            "\r\n";

        await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), cancellationToken);
        await stream.WriteAsync(bodyBytes, cancellationToken);
    }

    private static bool TryOpenBrowser(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                return true;
            }

            string fileName = OperatingSystem.IsMacOS() ? "open" : "xdg-open";
            Process.Start(new ProcessStartInfo(fileName, url) { UseShellExecute = false });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ToBase64Url(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string? NormalizeOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength
            ? value
            : value[..Math.Max(0, maxLength - 3)] + "...";
    }

    private sealed record AnthropicAuthorizationCallback(string Code, string State);
}
