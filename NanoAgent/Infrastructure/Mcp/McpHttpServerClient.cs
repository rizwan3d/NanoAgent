using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NanoAgent.Infrastructure.Mcp;

internal sealed class McpHttpServerClient : IMcpServerClient
{
    private readonly HttpClient _httpClient;
    private readonly McpServerConfiguration _configuration;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private int _nextRequestId;
    private string? _sessionId;

    public McpHttpServerClient(
        HttpClient httpClient,
        McpServerConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public string Endpoint => _configuration.Url ?? string.Empty;

    public string ServerName => _configuration.Name;

    public string TransportKind => "http";

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await SendRequestAsync(
            "initialize",
            McpJson.WriteInitializeParams,
            cancellationToken);
        await SendNotificationAsync(
            "notifications/initialized",
            writeParams: null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<McpRemoteTool>> ListToolsAsync(CancellationToken cancellationToken)
    {
        List<McpRemoteTool> tools = [];
        string? cursor = null;

        do
        {
            JsonElement result = await SendRequestAsync(
                "tools/list",
                writer => McpJson.WriteListToolsParams(cursor, writer),
                cancellationToken);
            tools.AddRange(McpJson.ParseTools(result));
            cursor = McpJson.GetNextCursor(result);
        }
        while (!string.IsNullOrWhiteSpace(cursor));

        return tools;
    }

    public async Task<McpCallToolResult> CallToolAsync(
        string toolName,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        JsonElement result = await SendRequestAsync(
            "tools/call",
            writer => McpJson.WriteCallToolParams(toolName, arguments, writer),
            cancellationToken);
        return McpJson.ParseCallToolResult(result);
    }

    public ValueTask DisposeAsync()
    {
        _requestGate.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<JsonElement> SendRequestAsync(
        string method,
        Action<Utf8JsonWriter>? writeParams,
        CancellationToken cancellationToken)
    {
        int requestId = Interlocked.Increment(ref _nextRequestId);
        string payload = McpJson.BuildRequest(requestId, method, writeParams);

        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            JsonElement response = await SendPayloadAsync(
                payload,
                expectResponse: true,
                cancellationToken);
            if (response.TryGetProperty("error", out _))
            {
                throw new McpProtocolException(McpJson.GetJsonRpcErrorMessage(response));
            }

            if (!response.TryGetProperty("result", out JsonElement result))
            {
                throw new McpProtocolException(
                    $"MCP server '{ServerName}' returned a response without a result.");
            }

            return result.Clone();
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private async Task SendNotificationAsync(
        string method,
        Action<Utf8JsonWriter>? writeParams,
        CancellationToken cancellationToken)
    {
        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            await SendPayloadAsync(
                McpJson.BuildNotification(method, writeParams),
                expectResponse: false,
                cancellationToken);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private async Task<JsonElement> SendPayloadAsync(
        string payload,
        bool expectResponse,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_configuration.Url))
        {
            throw new McpProtocolException(
                $"MCP server '{ServerName}' does not define an HTTP URL.");
        }

        using HttpRequestMessage request = new(HttpMethod.Post, _configuration.Url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        AddConfiguredHeaders(request);

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        CaptureSessionId(response);

        string responseBody = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string bodySuffix = string.IsNullOrWhiteSpace(responseBody)
                ? string.Empty
                : $" Response: {responseBody.Trim()}";
            throw new McpProtocolException(
                $"MCP server '{ServerName}' returned HTTP {(int)response.StatusCode}.{bodySuffix}");
        }

        if (!expectResponse || string.IsNullOrWhiteSpace(responseBody))
        {
            return default;
        }

        string contentType = response.Content?.Headers.ContentType?.MediaType ?? string.Empty;
        string json = contentType.Contains("event-stream", StringComparison.OrdinalIgnoreCase)
            ? ExtractSseJson(responseBody)
            : responseBody;

        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private void AddConfiguredHeaders(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_sessionId))
        {
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
        }

        if (!string.IsNullOrWhiteSpace(_configuration.BearerTokenEnvVar))
        {
            string? token = Environment.GetEnvironmentVariable(_configuration.BearerTokenEnvVar);
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
            }
        }

        foreach (KeyValuePair<string, string> header in _configuration.HttpHeaders)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (KeyValuePair<string, string> header in _configuration.EnvHttpHeaders)
        {
            string? value = Environment.GetEnvironmentVariable(header.Value);
            if (!string.IsNullOrWhiteSpace(value))
            {
                request.Headers.TryAddWithoutValidation(header.Key, value);
            }
        }
    }

    private void CaptureSessionId(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Mcp-Session-Id", out IEnumerable<string>? values))
        {
            _sessionId = values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        }
    }

    private static string ExtractSseJson(string responseBody)
    {
        StringBuilder builder = new();
        string normalized = responseBody.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

        foreach (string line in normalized.Split('\n'))
        {
            if (line.Length == 0)
            {
                if (builder.Length > 0)
                {
                    break;
                }

                continue;
            }

            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(line["data:".Length..].TrimStart());
        }

        string json = builder.ToString().Trim();
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new McpProtocolException("The MCP server returned an empty SSE response.");
        }

        return json;
    }
}
