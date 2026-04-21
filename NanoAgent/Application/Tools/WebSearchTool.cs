using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;

namespace NanoAgent.Application.Tools;

internal sealed class WebSearchTool : ITool
{
    private const int DefaultMaxResults = 5;
    private const int MinMaxResults = 1;
    private const int MaxMaxResults = 10;

    private readonly IWebSearchService _webSearchService;

    public WebSearchTool(IWebSearchService webSearchService)
    {
        _webSearchService = webSearchService;
    }

    public string Description => "Search the web for current external information and return the top matching results.";

    public string Name => AgentToolNames.WebSearch;

    public string PermissionRequirements => """
        {
          "approvalMode": "Automatic",
          "toolTags": ["webfetch"],
          "webRequest": {
            "requestArgumentName": "query"
          }
        }
        """;

    public string Schema => """
        {
          "type": "object",
          "properties": {
            "query": {
              "type": "string",
              "description": "Search query to run on the public web."
            },
            "maxResults": {
              "type": "integer",
              "description": "Optional number of results to return. Must be between 1 and 10. Defaults to 5."
            }
          },
          "required": ["query"],
          "additionalProperties": false
        }
        """;

    public async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!ToolArguments.TryGetNonEmptyString(context.Arguments, "query", out string? query))
        {
            return ToolResultFactory.InvalidArguments(
                "missing_query",
                "Tool 'web_search' requires a non-empty 'query' string.",
                new ToolRenderPayload(
                    "Invalid web_search arguments",
                    "Provide a non-empty 'query' string."));
        }

        int maxResults = DefaultMaxResults;
        if (ToolArguments.TryGetInt32(context.Arguments, "maxResults", out int parsedMaxResults))
        {
            if (parsedMaxResults is < MinMaxResults or > MaxMaxResults)
            {
                return ToolResultFactory.InvalidArguments(
                    "invalid_max_results",
                    $"Tool 'web_search' requires 'maxResults' to be between {MinMaxResults} and {MaxMaxResults}.",
                    new ToolRenderPayload(
                        "Invalid web_search arguments",
                        $"Set 'maxResults' to a value between {MinMaxResults} and {MaxMaxResults}."));
            }

            maxResults = parsedMaxResults;
        }

        WebSearchResult result = await _webSearchService.SearchAsync(
            new WebSearchRequest(
                query!,
                maxResults),
            cancellationToken);

        string renderText = result.Results.Count == 0
            ? "No web results found."
            : string.Join(
                Environment.NewLine + Environment.NewLine,
                result.Results.Select(static (item, index) =>
                    $"{index + 1}. {item.Title}{Environment.NewLine}{item.Url}" +
                    (string.IsNullOrWhiteSpace(item.Snippet)
                        ? string.Empty
                        : $"{Environment.NewLine}{item.Snippet}")));

        return ToolResultFactory.Success(
            $"Found {result.Results.Count} web {(result.Results.Count == 1 ? "result" : "results")} for '{result.Query}'.",
            result,
            ToolJsonContext.Default.WebSearchResult,
            new ToolRenderPayload(
                $"Web search for '{result.Query}'",
                renderText));
    }

}
