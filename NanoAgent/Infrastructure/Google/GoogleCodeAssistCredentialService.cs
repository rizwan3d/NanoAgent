using Microsoft.Extensions.Logging;
using NanoAgent.Application.Abstractions;
using NanoAgent.Domain.Models;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoAgent.Infrastructure.Google;

internal sealed class GoogleCodeAssistCredentialService :
    IGeminiCliAuthenticator,
    IGoogleAntigravityAuthenticator,
    IGoogleCodeAssistCredentialService
{
    private const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string UserInfoEndpoint = "https://www.googleapis.com/oauth2/v2/userinfo";
    private const string CodeAssistEndpoint = "https://cloudcode-pa.googleapis.com";
    private const string CredentialType = "google-code-assist";
    private const string GeminiCliClientId = "681255809395-oo8ft2oprdrnp9e3aqf6av3hmdib135j.apps.googleusercontent.com";
    private const string GeminiCliClientSecret = "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";
    private const string AntigravityClientId = "1071006060591-tmhssin2h21lcre235vtolojh4g403ep.apps.googleusercontent.com";
    private const string AntigravityClientSecret = "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";
    private const string GeminiCliScopes = "https://www.googleapis.com/auth/cloud-platform https://www.googleapis.com/auth/userinfo.email https://www.googleapis.com/auth/userinfo.profile";
    private const string AntigravityScopes = "https://www.googleapis.com/auth/cloud-platform https://www.googleapis.com/auth/userinfo.email https://www.googleapis.com/auth/userinfo.profile https://www.googleapis.com/auth/cclog https://www.googleapis.com/auth/experimentsandconfigs";
    private const string AntigravityFallbackProjectId = "rising-fact-p41fc";
    private const int AntigravityCallbackPort = 51121;
    private static readonly TimeSpan CallbackTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TokenExpiryBuffer = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient;
    private readonly IApiKeySecretStore _secretStore;
    private readonly IStatusMessageWriter _statusMessageWriter;
    private readonly ILogger<GoogleCodeAssistCredentialService> _logger;

    public GoogleCodeAssistCredentialService(
        HttpClient httpClient,
        IApiKeySecretStore secretStore,
        IStatusMessageWriter statusMessageWriter,
        ILogger<GoogleCodeAssistCredentialService> logger)
    {
        _httpClient = httpClient;
        _secretStore = secretStore;
        _statusMessageWriter = statusMessageWriter;
        _logger = logger;
    }

    public Task<string> AuthenticateAsync(CancellationToken cancellationToken)
    {
        return AuthenticateAsync(GoogleCodeAssistProvider.GeminiCli, cancellationToken);
    }

    Task<string> IGoogleAntigravityAuthenticator.AuthenticateAsync(CancellationToken cancellationToken)
    {
        return AuthenticateAsync(GoogleCodeAssistProvider.GoogleAntigravity, cancellationToken);
    }

    internal async Task<string> AuthenticateAsync(
        GoogleCodeAssistProvider provider,
        CancellationToken cancellationToken)
    {
        string state = GenerateState();
        string? codeVerifier = provider == GoogleCodeAssistProvider.GoogleAntigravity
            ? GenerateCodeVerifier()
            : null;
        string? codeChallenge = codeVerifier is null
            ? null
            : GenerateCodeChallenge(codeVerifier);

        using TcpListener listener = CreateCallbackListener(provider);
        listener.Start();
        int callbackPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        string callbackPath = GetCallbackPath(provider);
        string redirectUri = GetRedirectUri(provider, callbackPort);
        string authorizationUrl = BuildAuthorizationUrl(
            provider,
            redirectUri,
            state,
            codeChallenge);

        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(CallbackTimeout);

        await _statusMessageWriter.ShowInfoAsync(
            $"Opening browser for {GetDisplayName(provider)} sign-in.",
            cancellationToken);
        await _statusMessageWriter.ShowInfoAsync(
            $"If the browser does not open, visit: {authorizationUrl}",
            cancellationToken);

        if (!TryOpenBrowser(authorizationUrl))
        {
            _logger.LogInformation(
                "Unable to open the system browser for {ProviderName} sign-in.",
                GetDisplayName(provider));
        }

        GoogleAuthorizationCallback callback = await WaitForCallbackAsync(
            listener,
            callbackPath,
            state,
            timeoutSource.Token);
        GoogleCodeAssistCredentials credentials = await ExchangeCodeForCredentialsAsync(
            provider,
            callback.Code,
            redirectUri,
            codeVerifier,
            cancellationToken);

        await _statusMessageWriter.ShowSuccessAsync(
            $"{GetDisplayName(provider)} sign-in completed.",
            cancellationToken);

        return SerializeCredentials(credentials);
    }

    public async Task<GoogleCodeAssistResolvedCredential> ResolveAsync(
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

        GoogleCodeAssistCredentials credentials = DeserializeCredentials(credentialsJson);
        if (forceRefresh || IsExpired(credentials))
        {
            credentials = await RefreshCredentialsAsync(credentials, cancellationToken);
            await _secretStore.SaveAsync(SerializeCredentials(credentials), cancellationToken);
        }

        return new GoogleCodeAssistResolvedCredential(
            credentials.AccessToken,
            NormalizeOrNull(credentials.ProjectId),
            ParseProvider(credentials.Provider));
    }

    internal static void ApplyCodeAssistHeaders(
        HttpRequestMessage request,
        GoogleCodeAssistResolvedCredential credential,
        ProviderKind providerKind)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        request.Headers.Accept.ParseAdd("application/json");

        if (providerKind == ProviderKind.GoogleAntigravity)
        {
            request.Headers.TryAddWithoutValidation("User-Agent", "antigravity/1.18.3 windows/amd64");
            request.Headers.TryAddWithoutValidation("x-goog-api-client", "google-cloud-sdk vscode_cloudshelleditor/0.1");
            request.Headers.TryAddWithoutValidation(
                "Client-Metadata",
                """{"ideType":"ANTIGRAVITY","platform":"WINDOWS","pluginType":"GEMINI"}""");
            return;
        }

        request.Headers.TryAddWithoutValidation("User-Agent", "google-api-nodejs-client/9.15.1");
        request.Headers.TryAddWithoutValidation("x-goog-api-client", "gl-node/22.17.0 auth/9.15.1");
        request.Headers.TryAddWithoutValidation(
            "Client-Metadata",
            "ideType=IDE_UNSPECIFIED,platform=PLATFORM_UNSPECIFIED,pluginType=GEMINI");
    }

    private static TcpListener CreateCallbackListener(GoogleCodeAssistProvider provider)
    {
        return provider == GoogleCodeAssistProvider.GoogleAntigravity
            ? new TcpListener(IPAddress.Loopback, AntigravityCallbackPort)
            : new TcpListener(IPAddress.Loopback, 0);
    }

    private static string GetCallbackPath(GoogleCodeAssistProvider provider)
    {
        return provider == GoogleCodeAssistProvider.GoogleAntigravity
            ? "/oauth-callback"
            : "/oauth2callback";
    }

    private static string GetRedirectUri(GoogleCodeAssistProvider provider, int port)
    {
        return provider == GoogleCodeAssistProvider.GoogleAntigravity
            ? $"http://localhost:{port}/oauth-callback"
            : $"http://127.0.0.1:{port}/oauth2callback";
    }

    private static string BuildAuthorizationUrl(
        GoogleCodeAssistProvider provider,
        string redirectUri,
        string state,
        string? codeChallenge)
    {
        Dictionary<string, string> parameters = new(StringComparer.Ordinal)
        {
            ["client_id"] = GetClientId(provider),
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = GetScopes(provider),
            ["access_type"] = "offline",
            ["state"] = state,
            ["prompt"] = "consent"
        };

        if (!string.IsNullOrWhiteSpace(codeChallenge))
        {
            parameters["code_challenge"] = codeChallenge;
            parameters["code_challenge_method"] = "S256";
        }

        string query = string.Join(
            "&",
            parameters.Select(static pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

        return $"{AuthorizationEndpoint}?{query}";
    }

    private async Task<GoogleAuthorizationCallback> WaitForCallbackAsync(
        TcpListener listener,
        string callbackPath,
        string expectedState,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
            GoogleAuthorizationCallback? callback = await TryHandleCallbackClientAsync(
                client,
                callbackPath,
                expectedState,
                cancellationToken);
            if (callback is not null)
            {
                return callback;
            }
        }
    }

    private static async Task<GoogleAuthorizationCallback?> TryHandleCallbackClientAsync(
        TcpClient client,
        string callbackPath,
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
            !Uri.TryCreate(new Uri("http://localhost"), requestParts[1], out Uri? requestUri))
        {
            await WriteHttpResponseAsync(stream, HttpStatusCode.BadRequest, "Invalid callback request.", cancellationToken);
            return null;
        }

        if (!string.Equals(requestUri.AbsolutePath, callbackPath, StringComparison.Ordinal))
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
            throw new InvalidOperationException($"Google Code Assist authentication failed: {error}");
        }

        string? code = NormalizeOrNull(query.GetValueOrDefault("code"));
        string? state = NormalizeOrNull(query.GetValueOrDefault("state"));
        if (code is null || state is null)
        {
            await WriteHttpResponseAsync(stream, HttpStatusCode.BadRequest, "Missing callback parameters.", cancellationToken);
            throw new InvalidOperationException("Google Code Assist authentication returned an incomplete callback.");
        }

        if (!string.Equals(state, expectedState, StringComparison.Ordinal))
        {
            await WriteHttpResponseAsync(stream, HttpStatusCode.BadRequest, "State mismatch.", cancellationToken);
            throw new InvalidOperationException("Google Code Assist authentication state did not match.");
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

        return new GoogleAuthorizationCallback(code);
    }

    private async Task<GoogleCodeAssistCredentials> ExchangeCodeForCredentialsAsync(
        GoogleCodeAssistProvider provider,
        string code,
        string redirectUri,
        string? codeVerifier,
        CancellationToken cancellationToken)
    {
        List<KeyValuePair<string, string>> formValues =
        [
            new("grant_type", "authorization_code"),
            new("client_id", GetClientId(provider)),
            new("client_secret", GetClientSecret(provider)),
            new("code", code),
            new("redirect_uri", redirectUri)
        ];

        if (!string.IsNullOrWhiteSpace(codeVerifier))
        {
            formValues.Add(new KeyValuePair<string, string>("code_verifier", codeVerifier));
        }

        using FormUrlEncodedContent form = new(formValues);
        GoogleCodeAssistTokenResponse tokenResponse = await SendTokenRequestAsync(form, cancellationToken);
        if (string.IsNullOrWhiteSpace(tokenResponse.RefreshToken))
        {
            throw new InvalidOperationException(
                $"{GetDisplayName(provider)} authentication did not return a refresh token.");
        }

        string accessToken = NormalizeOrNull(tokenResponse.AccessToken)
            ?? throw new InvalidOperationException(
                $"{GetDisplayName(provider)} authentication did not return an access token.");
        string? projectId = await ResolveProjectIdAsync(
            accessToken,
            provider,
            existingProjectId: null,
            cancellationToken);
        string? email = await FetchUserEmailAsync(accessToken, cancellationToken);

        return CreateCredentials(
            provider,
            tokenResponse,
            existingCredentials: null,
            email,
            projectId);
    }

    private async Task<GoogleCodeAssistCredentials> RefreshCredentialsAsync(
        GoogleCodeAssistCredentials credentials,
        CancellationToken cancellationToken)
    {
        GoogleCodeAssistProvider provider = ParseProvider(credentials.Provider);
        using FormUrlEncodedContent form = new(
        [
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("client_id", GetClientId(provider)),
            new KeyValuePair<string, string>("client_secret", GetClientSecret(provider)),
            new KeyValuePair<string, string>("refresh_token", credentials.RefreshToken)
        ]);

        GoogleCodeAssistTokenResponse tokenResponse = await SendTokenRequestAsync(form, cancellationToken);
        string accessToken = NormalizeOrNull(tokenResponse.AccessToken)
            ?? throw new InvalidOperationException(
                $"{GetDisplayName(provider)} refresh did not return an access token.");
        string? projectId = await ResolveProjectIdAsync(
            accessToken,
            provider,
            credentials.ProjectId,
            cancellationToken);
        string? email = await FetchUserEmailAsync(accessToken, cancellationToken);

        return CreateCredentials(
            provider,
            tokenResponse,
            credentials,
            email,
            projectId);
    }

    private async Task<GoogleCodeAssistTokenResponse> SendTokenRequestAsync(
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
                $"Google Code Assist token request failed with HTTP {(int)response.StatusCode}: {Truncate(responseBody.Trim(), 300)}");
        }

        GoogleCodeAssistTokenResponse? tokenResponse = JsonSerializer.Deserialize(
            responseBody,
            GoogleCodeAssistJsonContext.Default.GoogleCodeAssistTokenResponse);

        if (tokenResponse?.AccessToken is null || tokenResponse.ExpiresInSeconds is not > 0)
        {
            throw new InvalidOperationException(
                "Google Code Assist token response was missing required fields.");
        }

        return tokenResponse;
    }

    private async Task<string?> FetchUserEmailAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, UserInfoEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            return TryGetString(document.RootElement, "email");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<string?> ResolveProjectIdAsync(
        string accessToken,
        GoogleCodeAssistProvider provider,
        string? existingProjectId,
        CancellationToken cancellationToken)
    {
        string? projectId = NormalizeOrNull(existingProjectId) ??
            NormalizeOrNull(Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT")) ??
            NormalizeOrNull(Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT_ID")) ??
            NormalizeOrNull(Environment.GetEnvironmentVariable("GCLOUD_PROJECT"));
        string requestBody = BuildLoadCodeAssistRequestBody(provider, projectId);

        using HttpRequestMessage request = new(
            HttpMethod.Post,
            new Uri($"{CodeAssistEndpoint}/v1internal:loadCodeAssist"));
        request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        ApplyCodeAssistHeaders(
            request,
            new GoogleCodeAssistResolvedCredential(accessToken, projectId, provider),
            ToProviderKind(provider));

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return projectId ??
                (provider == GoogleCodeAssistProvider.GoogleAntigravity ? AntigravityFallbackProjectId : null);
        }

        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            return ExtractProjectId(document.RootElement) ??
                projectId ??
                (provider == GoogleCodeAssistProvider.GoogleAntigravity ? AntigravityFallbackProjectId : null);
        }
        catch (JsonException)
        {
            return projectId ??
                (provider == GoogleCodeAssistProvider.GoogleAntigravity ? AntigravityFallbackProjectId : null);
        }
    }

    private static string BuildLoadCodeAssistRequestBody(
        GoogleCodeAssistProvider provider,
        string? projectId)
    {
        JsonObject metadata = new()
        {
            ["ideType"] = provider == GoogleCodeAssistProvider.GoogleAntigravity
                ? "ANTIGRAVITY"
                : "IDE_UNSPECIFIED",
            ["platform"] = provider == GoogleCodeAssistProvider.GoogleAntigravity
                ? "WINDOWS"
                : "PLATFORM_UNSPECIFIED",
            ["pluginType"] = "GEMINI"
        };
        if (!string.IsNullOrWhiteSpace(projectId))
        {
            metadata["duetProject"] = projectId;
        }

        JsonObject body = new()
        {
            ["metadata"] = metadata
        };
        if (!string.IsNullOrWhiteSpace(projectId))
        {
            body["cloudaicompanionProject"] = projectId;
        }

        return body.ToJsonString();
    }

    private static string? ExtractProjectId(JsonElement root)
    {
        string? directProject = TryReadProject(root, "cloudaicompanionProject");
        if (directProject is not null)
        {
            return directProject;
        }

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("response", out JsonElement response))
        {
            directProject = TryReadProject(response, "cloudaicompanionProject");
            if (directProject is not null)
            {
                return directProject;
            }

            if (response.TryGetProperty("currentTier", out JsonElement currentTier))
            {
                directProject = TryReadProject(currentTier, "cloudaicompanionProject");
                if (directProject is not null)
                {
                    return directProject;
                }
            }
        }

        return null;
    }

    private static string? TryReadProject(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out JsonElement property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            return NormalizeOrNull(property.GetString());
        }

        if (property.ValueKind == JsonValueKind.Object)
        {
            return TryGetString(property, "id") ??
                TryGetString(property, "projectId") ??
                TryGetString(property, "name");
        }

        return null;
    }

    private static GoogleCodeAssistCredentials CreateCredentials(
        GoogleCodeAssistProvider provider,
        GoogleCodeAssistTokenResponse tokenResponse,
        GoogleCodeAssistCredentials? existingCredentials,
        string? email,
        string? projectId)
    {
        string accessToken = NormalizeOrNull(tokenResponse.AccessToken)
            ?? throw new InvalidOperationException("Google Code Assist token response did not include an access token.");
        string refreshToken = NormalizeOrNull(tokenResponse.RefreshToken)
            ?? existingCredentials?.RefreshToken
            ?? throw new InvalidOperationException("Google Code Assist token response did not include a refresh token.");
        long expires = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresInSeconds.GetValueOrDefault()).ToUnixTimeMilliseconds();

        return new GoogleCodeAssistCredentials(
            CredentialType,
            GetProviderId(provider),
            accessToken,
            refreshToken,
            expires,
            NormalizeOrNull(email) ?? existingCredentials?.Email,
            NormalizeOrNull(projectId) ?? NormalizeOrNull(existingCredentials?.ProjectId));
    }

    private static GoogleCodeAssistCredentials DeserializeCredentials(string value)
    {
        try
        {
            GoogleCodeAssistCredentials? credentials = JsonSerializer.Deserialize(
                value,
                GoogleCodeAssistJsonContext.Default.GoogleCodeAssistCredentials);

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
                "Stored Google Code Assist credentials are invalid. Run onboarding again to sign in.",
                exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                "Stored Google Code Assist credentials are incomplete. Run onboarding again to sign in.",
                exception);
        }
    }

    private static string SerializeCredentials(GoogleCodeAssistCredentials credentials)
    {
        return JsonSerializer.Serialize(
            credentials,
            GoogleCodeAssistJsonContext.Default.GoogleCodeAssistCredentials);
    }

    private static bool IsExpired(GoogleCodeAssistCredentials credentials)
    {
        long refreshAfter = credentials.ExpiresUnixMilliseconds - (long)TokenExpiryBuffer.TotalMilliseconds;
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() >= refreshAfter;
    }

    private static GoogleCodeAssistProvider ParseProvider(string provider)
    {
        string normalized = new(
            provider
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());

        return normalized switch
        {
            "googleantigravity" or "antigravity" => GoogleCodeAssistProvider.GoogleAntigravity,
            _ => GoogleCodeAssistProvider.GeminiCli
        };
    }

    private static ProviderKind ToProviderKind(GoogleCodeAssistProvider provider)
    {
        return provider == GoogleCodeAssistProvider.GoogleAntigravity
            ? ProviderKind.GoogleAntigravity
            : ProviderKind.GeminiCli;
    }

    private static string GetClientId(GoogleCodeAssistProvider provider)
    {
        return provider == GoogleCodeAssistProvider.GoogleAntigravity
            ? AntigravityClientId
            : GeminiCliClientId;
    }

    private static string GetClientSecret(GoogleCodeAssistProvider provider)
    {
        return provider == GoogleCodeAssistProvider.GoogleAntigravity
            ? AntigravityClientSecret
            : GeminiCliClientSecret;
    }

    private static string GetScopes(GoogleCodeAssistProvider provider)
    {
        return provider == GoogleCodeAssistProvider.GoogleAntigravity
            ? AntigravityScopes
            : GeminiCliScopes;
    }

    private static string GetProviderId(GoogleCodeAssistProvider provider)
    {
        return provider == GoogleCodeAssistProvider.GoogleAntigravity
            ? "google-antigravity"
            : "gemini-cli";
    }

    private static string GetDisplayName(GoogleCodeAssistProvider provider)
    {
        return provider == GoogleCodeAssistProvider.GoogleAntigravity
            ? "Google Antigravity"
            : "Gemini CLI";
    }

    private static string GenerateState()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
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

    private sealed record GoogleAuthorizationCallback(string Code);
}
