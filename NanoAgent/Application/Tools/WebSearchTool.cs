using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;
using System.Text.Json;

namespace NanoAgent.Application.Tools;

internal sealed class WebSearchTool : ITool
{
    private static readonly HashSet<string> AllowedResponseLengths = new(StringComparer.OrdinalIgnoreCase)
    {
        "short",
        "medium",
        "long"
    };

    private readonly IWebSearchService _webSearchService;

    public WebSearchTool(IWebSearchService webSearchService)
    {
        _webSearchService = webSearchService;
    }

    public string Description =>
        "Search the web for current information. Provide one or more natural-language queries and get back clean, ready-to-use text content from the top results.";

    public string Name => AgentToolNames.WebSearch;

    public string PermissionRequirements => """
        {
          "approvalMode": "Automatic",
          "toolTags": ["webfetch"],
          "webRequest": {
            "requestArgumentName": "search_query"
          }
        }
        """;

    public string Schema => """
        {
          "type": "object",
          "properties": {
            "search_query": {
              "type": "array",
              "description": "One or more web searches to run.",
              "items": {
                "type": "object",
                "properties": {
                  "q": {
                    "type": "string",
                    "description": "Natural-language search query. Describe the ideal page, not just keywords (e.g. 'blog post comparing React and Vue performance')."
                  },
                  "num_results": {
                    "type": "integer",
                    "description": "Optional number of results to return (1-25). Defaults to the response_length preset."
                  }
                },
                "required": ["q"],
                "additionalProperties": false
              }
            },
            "response_length": {
              "type": "string",
              "enum": ["short", "medium", "long"],
              "description": "Optional result-count hint. Defaults to medium."
            }
          },
          "required": ["search_query"],
          "additionalProperties": false
        }
        """;

    public async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        string responseLength = ToolArguments.GetOptionalString(context.Arguments, "response_length") ?? "medium";
        if (!AllowedResponseLengths.Contains(responseLength))
        {
            return InvalidArguments("Set 'response_length' to short, medium, or long.");
        }

        try
        {
            IReadOnlyList<WebSearchQuery> queries = ParseSearchQueries(context.Arguments);
            if (queries.Count == 0)
            {
                return InvalidArguments("Provide at least one search in 'search_query'.");
            }

            WebSearchRequest request = new(responseLength.ToLowerInvariant(), queries);

            WebSearchResult result = await _webSearchService.RunAsync(
                request,
                context.Session.SessionId,
                cancellationToken);

            return ToolResultFactory.Success(
                BuildSuccessMessage(result),
                result,
                ToolJsonContext.Default.WebSearchResult,
                new ToolRenderPayload(
                    "web_search completed",
                    BuildRenderText(result)));
        }
        catch (ArgumentException exception)
        {
            return InvalidArguments(exception.Message);
        }
    }

    private static ToolResult InvalidArguments(string message)
    {
        return ToolResultFactory.InvalidArguments(
            "invalid_web_search_arguments",
            message,
            new ToolRenderPayload(
                "Invalid web_search arguments",
                message));
    }

    private static IReadOnlyList<WebSearchQuery> ParseSearchQueries(JsonElement arguments)
    {
        if (!arguments.TryGetProperty("search_query", out JsonElement property))
        {
            return [];
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("Property 'search_query' must be an array.");
        }

        List<WebSearchQuery> queries = [];
        foreach (JsonElement item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("Each 'search_query' item must be an object.");
            }

            if (!ToolArguments.TryGetNonEmptyString(item, "q", out string? query))
            {
                throw new ArgumentException("Each 'search_query' item requires a non-empty 'q' string.");
            }

            int? numResults = ToolArguments.TryGetInt32(item, "num_results", out int value)
                ? value
                : null;
            queries.Add(new WebSearchQuery(query!, numResults));
        }

        return queries;
    }

    private static string BuildSuccessMessage(WebSearchResult result)
    {
        int count = result.SearchQuery.Count;
        return $"web_search completed {count} {(count == 1 ? "search" : "searches")}.";
    }

    private static string BuildRenderText(WebSearchResult result)
    {
        List<string> sections = [];

        foreach (WebSearchQueryResult search in result.SearchQuery)
        {
            sections.Add($"Search '{search.Query}': {search.Results.Count} result(s)");
        }

        if (result.Warnings.Count > 0)
        {
            sections.Add("Warnings:");
            sections.AddRange(result.Warnings);
        }

        return sections.Count == 0
            ? "No web_search output."
            : string.Join(Environment.NewLine, sections);
    }
}
