using Microsoft.Extensions.Logging;
using NanoAgent.Application.Abstractions;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NanoAgent.Infrastructure.OpenAi;

internal sealed class OpenAiChatGptAccountCredentialService :
    IOpenAiChatGptAccountAuthenticator,
    IOpenAiChatGptAccountCredentialService
{
    private const int CallbackPort = 1455;
    private const string AuthorizationEndpoint = "https://auth.openai.com/oauth/authorize";
    private const string TokenEndpoint = "https://auth.openai.com/oauth/token";
    private const string ClientId = "app_EMoamEEZ73f0CkXaXp7hrann";
    private const string RedirectUri = "http://localhost:1455/auth/callback";
    private const string Scopes = "openid profile email offline_access";
    private const string CredentialType = "openai-chatgpt-account";
    private const string Originator = "nanoagent";
    private static readonly TimeSpan CallbackTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TokenExpiryBuffer = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient;
    private readonly IApiKeySecretStore _secretStore;
    private readonly IStatusMessageWriter _statusMessageWriter;
    private readonly ILogger<OpenAiChatGptAccountCredentialService> _logger;

    public OpenAiChatGptAccountCredentialService(
        HttpClient httpClient,
        IApiKeySecretStore secretStore,
        IStatusMessageWriter statusMessageWriter,
        ILogger<OpenAiChatGptAccountCredentialService> logger)
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
        string state = GenerateState();
        string authorizationUrl = BuildAuthorizationUrl(codeChallenge, state);

        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(CallbackTimeout);

        using TcpListener listener = TcpListener.Create(CallbackPort);
        listener.Start();

        await _statusMessageWriter.ShowInfoAsync(
            "Opening browser for OpenAI ChatGPT Plus/Pro sign-in.",
            cancellationToken);
        await _statusMessageWriter.ShowInfoAsync(
            $"If the browser does not open, visit: {authorizationUrl}",
            cancellationToken);

        if (!TryOpenBrowser(authorizationUrl))
        {
            _logger.LogInformation("Unable to open the system browser for OpenAI ChatGPT Plus/Pro sign-in.");
        }

        OpenAiAuthorizationCallback callback = await WaitForCallbackAsync(
            listener,
            state,
            timeoutSource.Token);
        OpenAiChatGptAccountCredentials credentials = await ExchangeCodeForCredentialsAsync(
            callback.Code,
            codeVerifier,
            cancellationToken);

        await _statusMessageWriter.ShowSuccessAsync(
            "OpenAI ChatGPT Plus/Pro sign-in completed.",
            cancellationToken);

        return SerializeCredentials(credentials);
    }

    public async Task<OpenAiChatGptAccountResolvedCredential> ResolveAsync(
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

        OpenAiChatGptAccountCredentials credentials = DeserializeCredentials(credentialsJson);
        if (forceRefresh || IsExpired(credentials))
        {
            credentials = await RefreshCredentialsAsync(credentials, cancellationToken);
            await _secretStore.SaveAsync(SerializeCredentials(credentials), cancellationToken);
        }

        return new OpenAiChatGptAccountResolvedCredential(
            credentials.AccessToken,
            NormalizeOrNull(credentials.AccountId));
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

    private static string GenerateState()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string BuildAuthorizationUrl(string codeChallenge, string state)
    {
        Dictionary<string, string> parameters = new(StringComparer.Ordinal)
        {
            ["client_id"] = ClientId,
            ["redirect_uri"] = RedirectUri,
            ["scope"] = Scopes,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["response_type"] = "code",
            ["state"] = state,
            ["co" + "dex_cli_simplified_flow"] = "true",
            ["originator"] = Originator
        };

        string query = string.Join(
            "&",
            parameters.Select(static pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

        return $"{AuthorizationEndpoint}?{query}";
    }

    private async Task<OpenAiAuthorizationCallback> WaitForCallbackAsync(
        TcpListener listener,
        string expectedState,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
            OpenAiAuthorizationCallback? callback = await TryHandleCallbackClientAsync(
                client,
                expectedState,
                cancellationToken);
            if (callback is not null)
            {
                return callback;
            }
        }
    }

    private static async Task<OpenAiAuthorizationCallback?> TryHandleCallbackClientAsync(
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

        if (!string.Equals(requestUri.AbsolutePath, "/auth/callback", StringComparison.Ordinal))
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
            throw new InvalidOperationException($"OpenAI ChatGPT Plus/Pro authentication failed: {error}");
        }

        string? code = NormalizeOrNull(query.GetValueOrDefault("code"));
        string? state = NormalizeOrNull(query.GetValueOrDefault("state"));
        if (code is null || state is null)
        {
            await WriteHttpResponseAsync(stream, HttpStatusCode.BadRequest, "Missing callback parameters.", cancellationToken);
            throw new InvalidOperationException("OpenAI ChatGPT Plus/Pro authentication returned an incomplete callback.");
        }

        if (!string.Equals(state, expectedState, StringComparison.Ordinal))
        {
            await WriteHttpResponseAsync(stream, HttpStatusCode.BadRequest, "State mismatch.", cancellationToken);
            throw new InvalidOperationException("OpenAI ChatGPT Plus/Pro authentication state did not match.");
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

        return new OpenAiAuthorizationCallback(code);
    }

    private async Task<OpenAiChatGptAccountCredentials> ExchangeCodeForCredentialsAsync(
        string code,
        string codeVerifier,
        CancellationToken cancellationToken)
    {
        using FormUrlEncodedContent form = new(
        [
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("client_id", ClientId),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("redirect_uri", RedirectUri),
            new KeyValuePair<string, string>("code_verifier", codeVerifier)
        ]);

        OpenAiChatGptTokenResponse tokenResponse = await SendTokenRequestAsync(form, cancellationToken);
        if (string.IsNullOrWhiteSpace(tokenResponse.RefreshToken))
        {
            throw new InvalidOperationException(
                "OpenAI ChatGPT Plus/Pro authentication did not return a refresh token.");
        }

        return CreateCredentials(tokenResponse, existingCredentials: null);
    }

    private async Task<OpenAiChatGptAccountCredentials> RefreshCredentialsAsync(
        OpenAiChatGptAccountCredentials credentials,
        CancellationToken cancellationToken)
    {
        using FormUrlEncodedContent form = new(
        [
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("client_id", ClientId),
            new KeyValuePair<string, string>("refresh_token", credentials.RefreshToken)
        ]);

        OpenAiChatGptTokenResponse tokenResponse = await SendTokenRequestAsync(form, cancellationToken);
        return CreateCredentials(tokenResponse, credentials);
    }

    private async Task<OpenAiChatGptTokenResponse> SendTokenRequestAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.PostAsync(
            TokenEndpoint,
            content,
            cancellationToken);
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"OpenAI ChatGPT Plus/Pro token request failed with HTTP {(int)response.StatusCode}: {Truncate(responseBody.Trim(), 300)}");
        }

        OpenAiChatGptTokenResponse? tokenResponse = JsonSerializer.Deserialize(
            responseBody,
            OpenAiChatGptAccountJsonContext.Default.OpenAiChatGptTokenResponse);

        if (tokenResponse?.AccessToken is null || tokenResponse.ExpiresInSeconds is not > 0)
        {
            throw new InvalidOperationException(
                "OpenAI ChatGPT Plus/Pro token response was missing required fields.");
        }

        return tokenResponse;
    }

    private static OpenAiChatGptAccountCredentials CreateCredentials(
        OpenAiChatGptTokenResponse tokenResponse,
        OpenAiChatGptAccountCredentials? existingCredentials)
    {
        string accessToken = NormalizeOrNull(tokenResponse.AccessToken)
            ?? throw new InvalidOperationException("OpenAI ChatGPT Plus/Pro token response did not include an access token.");
        string refreshToken = NormalizeOrNull(tokenResponse.RefreshToken)
            ?? existingCredentials?.RefreshToken
            ?? throw new InvalidOperationException("OpenAI ChatGPT Plus/Pro token response did not include a refresh token.");
        long expires = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresInSeconds.GetValueOrDefault()).ToUnixTimeMilliseconds();

        JwtIdentity identity = ExtractIdentity(tokenResponse.IdToken) ??
            ExtractIdentity(accessToken) ??
            new JwtIdentity(null, null);

        return new OpenAiChatGptAccountCredentials(
            CredentialType,
            accessToken,
            refreshToken,
            expires,
            NormalizeOrNull(tokenResponse.Email) ?? identity.Email ?? existingCredentials?.Email,
            identity.AccountId ?? existingCredentials?.AccountId);
    }

    private static OpenAiChatGptAccountCredentials DeserializeCredentials(string value)
    {
        try
        {
            OpenAiChatGptAccountCredentials? credentials = JsonSerializer.Deserialize(
                value,
                OpenAiChatGptAccountJsonContext.Default.OpenAiChatGptAccountCredentials);

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
                "Stored OpenAI ChatGPT Plus/Pro credentials are invalid. Run onboarding again to sign in.",
                exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                "Stored OpenAI ChatGPT Plus/Pro credentials are incomplete. Run onboarding again to sign in.",
                exception);
        }
    }

    private static string SerializeCredentials(OpenAiChatGptAccountCredentials credentials)
    {
        return JsonSerializer.Serialize(
            credentials,
            OpenAiChatGptAccountJsonContext.Default.OpenAiChatGptAccountCredentials);
    }

    private static bool IsExpired(OpenAiChatGptAccountCredentials credentials)
    {
        long refreshAfter = credentials.ExpiresUnixMilliseconds - (long)TokenExpiryBuffer.TotalMilliseconds;
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() >= refreshAfter;
    }

    private static JwtIdentity? ExtractIdentity(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        string[] parts = token.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        try
        {
            byte[] payloadBytes = FromBase64Url(parts[1]);
            using JsonDocument document = JsonDocument.Parse(payloadBytes);
            JsonElement root = document.RootElement;

            string? accountId = TryGetString(root, "chatgpt_account_id");
            if (accountId is null &&
                root.TryGetProperty("https://api.openai.com/auth", out JsonElement authElement))
            {
                accountId = TryGetString(authElement, "chatgpt_account_id");
            }

            if (accountId is null &&
                root.TryGetProperty("organizations", out JsonElement organizations) &&
                organizations.ValueKind == JsonValueKind.Array)
            {
                JsonElement firstOrganization = organizations.EnumerateArray().FirstOrDefault();
                if (firstOrganization.ValueKind == JsonValueKind.Object)
                {
                    accountId = TryGetString(firstOrganization, "id");
                }
            }

            return new JwtIdentity(accountId, TryGetString(root, "email"));
        }
        catch (JsonException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return NormalizeOrNull(property.GetString());
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

    private static byte[] FromBase64Url(string value)
    {
        string normalized = value.Replace('-', '+').Replace('_', '/');
        int padding = (4 - normalized.Length % 4) % 4;
        return Convert.FromBase64String(normalized + new string('=', padding));
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

    private sealed record OpenAiAuthorizationCallback(string Code);

    private sealed record JwtIdentity(string? AccountId, string? Email);
}
