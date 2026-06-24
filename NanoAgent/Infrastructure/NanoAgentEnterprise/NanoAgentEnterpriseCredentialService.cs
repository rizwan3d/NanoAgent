using Microsoft.Extensions.Logging;
using NanoAgent.Application.Abstractions;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NanoAgent.Infrastructure.NanoAgentEnterprise;

internal sealed class NanoAgentEnterpriseCredentialService :
    INanoAgentEnterpriseAuthenticator,
    INanoAgentEnterpriseCredentialService
{
    private const string CallbackPath = "/callback";
    private const string ClientId = "nanoagent";
    private const string CredentialType = "nanoagent-enterprise";
    private static readonly TimeSpan CallbackTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TokenExpiryBuffer = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient;
    private readonly IAgentConfigurationStore _configurationStore;
    private readonly IApiKeySecretStore _secretStore;
    private readonly IStatusMessageWriter _statusMessageWriter;
    private readonly ILogger<NanoAgentEnterpriseCredentialService> _logger;
    private readonly Func<string, bool> _browserOpener;

    public NanoAgentEnterpriseCredentialService(
        HttpClient httpClient,
        IAgentConfigurationStore configurationStore,
        IApiKeySecretStore secretStore,
        IStatusMessageWriter statusMessageWriter,
        ILogger<NanoAgentEnterpriseCredentialService> logger,
        Func<string, bool>? browserOpener = null)
    {
        _httpClient = httpClient;
        _configurationStore = configurationStore;
        _secretStore = secretStore;
        _statusMessageWriter = statusMessageWriter;
        _logger = logger;
        _browserOpener = browserOpener ?? TryOpenBrowser;
    }

    public bool CanResolve(string storedCredentials)
    {
        if (string.IsNullOrWhiteSpace(storedCredentials))
        {
            return false;
        }

        try
        {
            NanoAgentEnterpriseCredentials? credentials = JsonSerializer.Deserialize(
                storedCredentials,
                NanoAgentEnterpriseJsonContext.Default.NanoAgentEnterpriseCredentials);
            return string.Equals(credentials?.Type, CredentialType, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public async Task<string> AuthenticateAsync(
        string baseUrl,
        CancellationToken cancellationToken)
    {
        string providerBaseUrl = NormalizeProviderBaseUrl(baseUrl);
        string codeVerifier = GenerateCodeVerifier();
        string codeChallenge = GenerateCodeChallenge(codeVerifier);
        string state = GenerateState();

        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        int callbackPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        string redirectUri = $"http://127.0.0.1:{callbackPort}{CallbackPath}";
        string authorizationUrl = BuildAuthorizationUrl(providerBaseUrl, redirectUri, codeChallenge, state);

        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(CallbackTimeout);

        await _statusMessageWriter.ShowInfoAsync(
            "Opening browser for NanoAgent Enterprise sign-in.",
            cancellationToken);
        await _statusMessageWriter.ShowInfoAsync(
            $"If the browser does not open, visit: {authorizationUrl}",
            cancellationToken);

        if (!_browserOpener(authorizationUrl))
        {
            _logger.LogInformation("Unable to open the system browser for NanoAgent Enterprise sign-in.");
        }

        NanoAgentEnterpriseAuthorizationCallback callback = await WaitForCallbackAsync(
            listener,
            state,
            timeoutSource.Token);
        NanoAgentEnterpriseCredentials credentials = callback switch
        {
            { AccessToken: not null } => CreateAccessTokenCredentials(
                providerBaseUrl,
                callback.AccessToken,
                callback.ExpiresAtUnixTimeMilliseconds),
            { Code: not null } => await ExchangeCodeForCredentialsAsync(
                providerBaseUrl,
                callback.Code,
                redirectUri,
                codeVerifier,
                cancellationToken),
            _ => throw new InvalidOperationException("NanoAgent Enterprise authentication callback was incomplete.")
        };
        await SaveCredentialsAsync(credentials, cancellationToken);

        await _statusMessageWriter.ShowSuccessAsync(
            "NanoAgent Enterprise sign-in completed.",
            cancellationToken);

        return SerializeCredentials(credentials);
    }

    public async Task<NanoAgentEnterpriseResolvedCredential> ResolveAsync(
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

        NanoAgentEnterpriseCredentials credentials = DeserializeCredentials(credentialsJson);
        if (forceRefresh || IsExpired(credentials))
        {
            if (string.IsNullOrWhiteSpace(credentials.RefreshToken))
            {
                string reauthenticatedCredentials = await AuthenticateAsync(
                    credentials.ProviderBaseUrl,
                    cancellationToken);
                credentials = DeserializeCredentials(reauthenticatedCredentials);
            }
            else
            {
                credentials = await RefreshCredentialsAsync(credentials, cancellationToken);
                await SaveCredentialsAsync(credentials, cancellationToken);
            }
        }

        return new NanoAgentEnterpriseResolvedCredential(credentials.AccessToken);
    }

    private static string NormalizeProviderBaseUrl(string baseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        return baseUrl.Trim().TrimEnd('/');
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

    private static string BuildAuthorizationUrl(
        string providerBaseUrl,
        string redirectUri,
        string codeChallenge,
        string state)
    {
        Dictionary<string, string> parameters = new(StringComparer.Ordinal)
        {
            ["client_id"] = ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256"
        };

        string query = string.Join(
            "&",
            parameters.Select(static pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

        return $"{GetNativeAuthApiBaseUrl(providerBaseUrl)}/oauth/authorize?{query}";
    }

    private async Task<NanoAgentEnterpriseAuthorizationCallback> WaitForCallbackAsync(
        TcpListener listener,
        string expectedState,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
            NanoAgentEnterpriseAuthorizationCallback? callback = await TryHandleCallbackClientAsync(
                client,
                expectedState,
                cancellationToken);
            if (callback is not null)
            {
                return callback;
            }
        }
    }

    private static async Task<NanoAgentEnterpriseAuthorizationCallback?> TryHandleCallbackClientAsync(
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

        string[] requestParts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (requestParts.Length < 2 ||
            !Uri.TryCreate(new Uri("http://127.0.0.1"), requestParts[1], out Uri? requestUri) ||
            requestUri is null)
        {
            await WriteHttpResponseAsync(stream, HttpStatusCode.BadRequest, "Invalid callback request.", cancellationToken);
            return null;
        }

        Dictionary<string, string> headers = await ReadHeadersAsync(reader, cancellationToken);
        string requestMethod = requestParts[0];
        string requestBody = await ReadRequestBodyAsync(reader, headers, cancellationToken);

        if (!string.Equals(requestUri.AbsolutePath, CallbackPath, StringComparison.Ordinal))
        {
            await WriteHttpResponseAsync(stream, HttpStatusCode.NotFound, "Not Found", cancellationToken);
            return null;
        }

        if (TryReadBearerTokenCallback(
                requestMethod,
                headers,
                requestBody,
                out NanoAgentEnterpriseAuthorizationCallback? bearerCallback))
        {
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

            return bearerCallback;
        }

        Dictionary<string, string?> query = ParseQuery(requestUri.Query);
        if (NormalizeOrNull(query.GetValueOrDefault("error")) is string error)
        {
            await WriteHttpResponseAsync(
                stream,
                HttpStatusCode.BadRequest,
                $"Authentication failed: {WebUtility.HtmlEncode(error)}",
                cancellationToken);
            throw new InvalidOperationException($"NanoAgent Enterprise authentication failed: {error}");
        }

        string? code = NormalizeOrNull(query.GetValueOrDefault("code"));
        string? state = NormalizeOrNull(query.GetValueOrDefault("state"));
        if (code is null || state is null)
        {
            await WriteHttpResponseAsync(stream, HttpStatusCode.BadRequest, "Missing callback parameters.", cancellationToken);
            throw new InvalidOperationException("NanoAgent Enterprise authentication returned an incomplete callback.");
        }

        if (!string.Equals(state, expectedState, StringComparison.Ordinal))
        {
            await WriteHttpResponseAsync(stream, HttpStatusCode.BadRequest, "State mismatch.", cancellationToken);
            throw new InvalidOperationException("NanoAgent Enterprise authentication state did not match.");
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

        return NanoAgentEnterpriseAuthorizationCallback.FromCode(code);
    }

    private async Task<NanoAgentEnterpriseCredentials> ExchangeCodeForCredentialsAsync(
        string providerBaseUrl,
        string code,
        string redirectUri,
        string codeVerifier,
        CancellationToken cancellationToken)
    {
        using FormUrlEncodedContent form = new(
        [
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("redirect_uri", redirectUri),
            new KeyValuePair<string, string>("code_verifier", codeVerifier)
        ]);

        using HttpRequestMessage request = new(HttpMethod.Post, $"{GetNativeAuthApiBaseUrl(providerBaseUrl)}/oauth/token")
        {
            Content = form
        };

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"NanoAgent Enterprise token exchange failed with HTTP {(int)response.StatusCode}.");
        }

        NanoAgentEnterpriseTokenResponse payload = JsonSerializer.Deserialize(
            responseBody,
            NanoAgentEnterpriseJsonContext.Default.NanoAgentEnterpriseTokenResponse) ??
            throw new InvalidOperationException("NanoAgent Enterprise token exchange returned an invalid response.");

        if (string.IsNullOrWhiteSpace(payload.AccessToken) || string.IsNullOrWhiteSpace(payload.RefreshToken))
        {
            throw new InvalidOperationException("NanoAgent Enterprise token exchange returned incomplete credentials.");
        }

        return new NanoAgentEnterpriseCredentials(
            CredentialType,
            providerBaseUrl,
            payload.AccessToken,
            payload.RefreshToken,
            DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, payload.ExpiresIn)).ToUnixTimeMilliseconds());
    }

    private async Task<NanoAgentEnterpriseCredentials> RefreshCredentialsAsync(
        NanoAgentEnterpriseCredentials credentials,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(credentials.RefreshToken))
        {
            throw new InvalidOperationException("NanoAgent Enterprise credentials do not include a refresh token.");
        }

        using FormUrlEncodedContent form = new(
        [
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", credentials.RefreshToken)
        ]);

        using HttpRequestMessage request = new(HttpMethod.Post, $"{GetNativeAuthApiBaseUrl(credentials.ProviderBaseUrl)}/oauth/token")
        {
            Content = form
        };

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"NanoAgent Enterprise token refresh failed with HTTP {(int)response.StatusCode}.");
        }

        NanoAgentEnterpriseTokenResponse payload = JsonSerializer.Deserialize(
            responseBody,
            NanoAgentEnterpriseJsonContext.Default.NanoAgentEnterpriseTokenResponse) ??
            throw new InvalidOperationException("NanoAgent Enterprise token refresh returned an invalid response.");

        if (string.IsNullOrWhiteSpace(payload.AccessToken) || string.IsNullOrWhiteSpace(payload.RefreshToken))
        {
            throw new InvalidOperationException("NanoAgent Enterprise token refresh returned incomplete credentials.");
        }

        return credentials with
        {
            AccessToken = payload.AccessToken,
            RefreshToken = payload.RefreshToken,
            ExpiresAtUnixTimeMilliseconds = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, payload.ExpiresIn)).ToUnixTimeMilliseconds()
        };
    }

    private static string GetControlPlaneBaseUrl(string providerBaseUrl)
    {
        string trimmed = providerBaseUrl.TrimEnd('/');
        return trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^3]
            : trimmed;
    }

    private static string GetNativeAuthApiBaseUrl(string providerBaseUrl)
    {
        return $"{GetControlPlaneBaseUrl(providerBaseUrl)}/api/v1/auth";
    }

    private static NanoAgentEnterpriseCredentials CreateAccessTokenCredentials(
        string providerBaseUrl,
        string accessToken,
        long? expiresAtUnixTimeMilliseconds)
    {
        long expires = expiresAtUnixTimeMilliseconds ?? DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();

        return new NanoAgentEnterpriseCredentials(
            CredentialType,
            providerBaseUrl,
            accessToken,
            RefreshToken: null,
            expires);
    }

    private async Task SaveCredentialsAsync(
        NanoAgentEnterpriseCredentials credentials,
        CancellationToken cancellationToken)
    {
        string serializedCredentials = SerializeCredentials(credentials);
        await _secretStore.SaveAsync(serializedCredentials, cancellationToken);

        string? activeProviderName = (await _configurationStore.LoadAsync(cancellationToken))?.ActiveProviderName;
        if (!string.IsNullOrWhiteSpace(activeProviderName))
        {
            await _secretStore.SaveAsync(activeProviderName, serializedCredentials, cancellationToken);
        }
    }

    private static bool IsExpired(NanoAgentEnterpriseCredentials credentials)
    {
        long currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long expiryBoundary = currentTime + (long)TokenExpiryBuffer.TotalMilliseconds;
        return credentials.ExpiresAtUnixTimeMilliseconds <= expiryBoundary;
    }

    private static string SerializeCredentials(NanoAgentEnterpriseCredentials credentials)
    {
        return JsonSerializer.Serialize(
            credentials,
            NanoAgentEnterpriseJsonContext.Default.NanoAgentEnterpriseCredentials);
    }

    private static NanoAgentEnterpriseCredentials DeserializeCredentials(string storedCredentials)
    {
        NanoAgentEnterpriseCredentials? credentials = JsonSerializer.Deserialize(
            storedCredentials,
            NanoAgentEnterpriseJsonContext.Default.NanoAgentEnterpriseCredentials);
        if (credentials is null || !string.Equals(credentials.Type, CredentialType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("NanoAgent Enterprise credentials are invalid.");
        }

        return credentials;
    }

    private static Dictionary<string, string?> ParseQuery(string query)
    {
        Dictionary<string, string?> values = new(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(query))
        {
            return values;
        }

        foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2);
            string key = Uri.UnescapeDataString(parts[0]);
            string? value = parts.Length > 1
                ? Uri.UnescapeDataString(parts[1])
                : null;
            values[key] = value;
        }

        return values;
    }

    private static async Task<Dictionary<string, string>> ReadHeadersAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);

        while (true)
        {
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrEmpty(line))
            {
                break;
            }

            int separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
            {
                continue;
            }

            string name = line[..separatorIndex].Trim();
            string value = line[(separatorIndex + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                headers[name] = value;
            }
        }

        return headers;
    }

    private static async Task<string> ReadRequestBodyAsync(
        StreamReader reader,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        if (!headers.TryGetValue("Content-Length", out string? contentLengthValue) ||
            !int.TryParse(contentLengthValue, out int contentLength) ||
            contentLength <= 0)
        {
            return string.Empty;
        }

        char[] buffer = new char[contentLength];
        int totalRead = 0;

        while (totalRead < contentLength)
        {
            int read = await reader.ReadAsync(
                buffer.AsMemory(totalRead, contentLength - totalRead),
                cancellationToken);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        return new string(buffer, 0, totalRead);
    }

    private static bool TryReadBearerTokenCallback(
        string requestMethod,
        IReadOnlyDictionary<string, string> headers,
        string requestBody,
        out NanoAgentEnterpriseAuthorizationCallback? callback)
    {
        callback = null;

        if (!string.Equals(requestMethod, "POST", StringComparison.OrdinalIgnoreCase) ||
            !headers.TryGetValue("Authorization", out string? authorizationValue) ||
            !AuthenticationHeaderValue.TryParse(authorizationValue, out AuthenticationHeaderValue? authorizationHeader) ||
            !string.Equals(authorizationHeader.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(authorizationHeader.Parameter))
        {
            return false;
        }

        long? expiresAtUnixTimeMilliseconds = ParseExpiryHeader(headers.GetValueOrDefault("X-NanoAgent-Token-Expires-At")) ??
            ParseExpiryBody(requestBody);

        callback = NanoAgentEnterpriseAuthorizationCallback.FromAccessToken(
            authorizationHeader.Parameter.Trim(),
            expiresAtUnixTimeMilliseconds);
        return true;
    }

    private static long? ParseExpiryHeader(string? value)
    {
        return TryParseUnixTimeMilliseconds(value) ??
            (DateTimeOffset.TryParse(value, out DateTimeOffset parsed)
                ? parsed.ToUnixTimeMilliseconds()
                : null);
    }

    private static long? ParseExpiryBody(string requestBody)
    {
        if (string.IsNullOrWhiteSpace(requestBody))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(requestBody);
            if (!document.RootElement.TryGetProperty("expiresAtUtc", out JsonElement expiresElement))
            {
                return null;
            }

            return expiresElement.ValueKind switch
            {
                JsonValueKind.Number when expiresElement.TryGetInt64(out long numericValue) => numericValue,
                JsonValueKind.String => ParseExpiryHeader(expiresElement.GetString()),
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static long? TryParseUnixTimeMilliseconds(string? value)
    {
        return long.TryParse(value, out long parsed)
            ? parsed
            : null;
    }

    private static async Task WriteHttpResponseAsync(
        NetworkStream stream,
        HttpStatusCode statusCode,
        string body,
        CancellationToken cancellationToken)
    {
        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
        string header =
            $"HTTP/1.1 {(int)statusCode} {statusCode}\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            "Connection: close\r\n\r\n";
        byte[] headerBytes = Encoding.ASCII.GetBytes(header);

        await stream.WriteAsync(headerBytes, cancellationToken);
        await stream.WriteAsync(bodyBytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static string? NormalizeOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static string ToBase64Url(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
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

            string fileName = OperatingSystem.IsMacOS()
                ? "open"
                : "xdg-open";
            Process.Start(new ProcessStartInfo(fileName, url) { UseShellExecute = false });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed record NanoAgentEnterpriseAuthorizationCallback(
        string? Code,
        string? AccessToken,
        long? ExpiresAtUnixTimeMilliseconds)
    {
        public static NanoAgentEnterpriseAuthorizationCallback FromCode(string code)
        {
            return new(code, AccessToken: null, ExpiresAtUnixTimeMilliseconds: null);
        }

        public static NanoAgentEnterpriseAuthorizationCallback FromAccessToken(
            string accessToken,
            long? expiresAtUnixTimeMilliseconds)
        {
            return new(Code: null, accessToken, expiresAtUnixTimeMilliseconds);
        }
    }
}
