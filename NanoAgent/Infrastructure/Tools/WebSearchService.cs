using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Infrastructure.Mcp;
using System.Text.Json;

namespace NanoAgent.Infrastructure.Tools;

/// <summary>
/// Backs the <c>web_search</c> tool with a single generic web search powered by the
/// hosted Exa MCP endpoint (<c>web_search_exa</c>). The endpoint works anonymously;
/// when an Exa API key is present in the environment it is forwarded for higher rate
/// limits.
/// </summary>
internal sealed class WebSearchService : IWebSearchService
{
    private const string ExaEndpoint = "https://mcp.exa.ai/mcp";
    private const string ExaSearchToolName = "web_search_exa";
    private static readonly string[] ExaApiKeyEnvVars = ["EXA_API_KEY", "exaApiKey"];

    private const int DefaultSearchResultsShort = 3;
    private const int DefaultSearchResultsMedium = 5;
    private const int DefaultSearchResultsLong = 8;
    private const int MinSearchResults = 1;
    private const int MaxSearchResults = 25;

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private IMcpServerClient? _client;

    public WebSearchService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<WebSearchResult> RunAsync(
        WebSearchRequest request,
        string sessionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        int defaultResults = GetDefaultResultCount(request.ResponseLength);
        List<string> warnings = [];
        List<WebSearchQueryResult> results = [];

        foreach (WebSearchQuery query in request.SearchQuery)
        {
            int numResults = Math.Clamp(
                query.NumResults ?? defaultResults,
                MinSearchResults,
                MaxSearchResults);

            try
            {
                IMcpServerClient client = await GetClientAsync(cancellationToken);
                McpCallToolResult call = await client.CallToolAsync(
                    ExaSearchToolName,
                    BuildSearchArguments(query.Query, numResults),
                    cancellationToken);

                if (call.IsError)
                {
                    string warning = $"Search '{query.Query}' failed: {DescribeError(call.RenderText)}";
                    warnings.Add(warning);
                    results.Add(new WebSearchQueryResult(query.Query, string.Empty, [], warning));
                    continue;
                }

                string content = call.RenderText;
                results.Add(new WebSearchQueryResult(
                    query.Query,
                    content,
                    ParseSearchItems(content)));
            }
            catch (Exception exception) when (
                exception is HttpRequestException or McpProtocolException or JsonException or TaskCanceledException &&
                !cancellationToken.IsCancellationRequested)
            {
                string warning = $"Search '{query.Query}' failed: {exception.Message}";
                warnings.Add(warning);
                results.Add(new WebSearchQueryResult(query.Query, string.Empty, [], warning));
            }
        }

        return new WebSearchResult(request.ResponseLength, results, warnings);
    }

    private async Task<IMcpServerClient> GetClientAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
        {
            return _client;
        }

        await _initGate.WaitAsync(cancellationToken);
        try
        {
            if (_client is null)
            {
                McpServerConfiguration configuration = new("exa") { Url = BuildEndpoint() };
                McpHttpServerClient client = new(_httpClient, configuration, Timeout.InfiniteTimeSpan);
                await client.InitializeAsync(cancellationToken);
                _client = client;
            }
        }
        finally
        {
            _initGate.Release();
        }

        return _client;
    }

    private static string BuildEndpoint()
    {
        string? apiKey = ReadApiKey();
        return string.IsNullOrWhiteSpace(apiKey)
            ? ExaEndpoint
            : $"{ExaEndpoint}?exaApiKey={Uri.EscapeDataString(apiKey)}";
    }

    private static string? ReadApiKey()
    {
        foreach (string envVar in ExaApiKeyEnvVars)
        {
            string? value = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static JsonElement BuildSearchArguments(string query, int numResults)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("query", query);
            writer.WriteNumber("numResults", numResults);
            writer.WriteEndObject();
        }

        using JsonDocument document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static IReadOnlyList<WebSearchItem> ParseSearchItems(string content)
    {
        List<WebSearchItem> items = [];
        string? title = null;
        string? url = null;
        string? published = null;
        string? author = null;

        void Flush()
        {
            if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(url))
            {
                items.Add(new WebSearchItem(title!, url!, published, author));
            }

            title = null;
            url = null;
            published = null;
            author = null;
        }

        foreach (string rawLine in content.Split('\n'))
        {
            string line = rawLine.Trim();
            if (TryReadField(line, "Title:", out string titleValue))
            {
                Flush();
                title = titleValue;
            }
            else if (title is not null && url is null && TryReadField(line, "URL:", out string urlValue))
            {
                url = urlValue;
            }
            else if (title is not null && TryReadField(line, "Published:", out string publishedValue))
            {
                published = NormalizeOptional(publishedValue);
            }
            else if (title is not null && TryReadField(line, "Author:", out string authorValue))
            {
                author = NormalizeOptional(authorValue);
            }
        }

        Flush();
        return items;
    }

    private static bool TryReadField(string line, string prefix, out string value)
    {
        if (line.StartsWith(prefix, StringComparison.Ordinal))
        {
            value = line[prefix.Length..].Trim();
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static string? NormalizeOptional(string value)
    {
        return string.IsNullOrWhiteSpace(value) || string.Equals(value, "N/A", StringComparison.OrdinalIgnoreCase)
            ? null
            : value;
    }

    private static string DescribeError(string renderText)
    {
        string trimmed = renderText.Trim();
        return string.IsNullOrWhiteSpace(trimmed)
            ? "the search provider returned an error."
            : trimmed;
    }

    private static int GetDefaultResultCount(string responseLength)
    {
        return responseLength switch
        {
            "short" => DefaultSearchResultsShort,
            "long" => DefaultSearchResultsLong,
            _ => DefaultSearchResultsMedium
        };
    }
}
