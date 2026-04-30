using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;
using System.Text.Json;

namespace NanoAgent.Application.Tools;

internal sealed class WebRunTool : ITool
{
    private static readonly HashSet<string> AllowedResponseLengths = new(StringComparer.OrdinalIgnoreCase)
    {
        "short",
        "medium",
        "long"
    };

    private readonly IWebRunService _webRunService;

    public WebRunTool(IWebRunService webRunService)
    {
        _webRunService = webRunService;
    }

    public string Description =>
        "Run mixed web operations in one request: web search, image search, open pages, find text, screenshots, finance, weather, sports, and time.";

    public string Name => AgentToolNames.WebRun;

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
              "items": {
                "type": "object",
                "properties": {
                  "q": { "type": "string", "description": "Web search query text." },
                  "recency": { "type": "integer", "description": "Optional recency filter in days." },
                  "domains": {
                    "type": "array",
                    "items": { "type": "string" },
                    "description": "Optional domain filters."
                  }
                },
                "required": ["q"],
                "additionalProperties": false
              }
            },
            "image_query": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "q": { "type": "string", "description": "Image search query text." },
                  "recency": { "type": "integer", "description": "Optional recency filter in days." },
                  "domains": {
                    "type": "array",
                    "items": { "type": "string" },
                    "description": "Optional domain filters."
                  }
                },
                "required": ["q"],
                "additionalProperties": false
              }
            },
            "open": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "ref_id": { "type": "string", "description": "Reference id returned by web_run or a direct URL." },
                  "lineno": { "type": "integer", "description": "Optional 1-based line number to center the excerpt on." }
                },
                "required": ["ref_id"],
                "additionalProperties": false
              }
            },
            "find": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "ref_id": { "type": "string", "description": "Reference id returned by web_run or a direct URL." },
                  "pattern": { "type": "string", "description": "Text pattern to search for in the page." }
                },
                "required": ["ref_id", "pattern"],
                "additionalProperties": false
              }
            },
            "screenshot": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "ref_id": { "type": "string", "description": "Reference id returned by web_run or a direct URL." },
                  "pageno": { "type": "integer", "description": "Optional page number for PDF-like targets." }
                },
                "required": ["ref_id"],
                "additionalProperties": false
              }
            },
            "finance": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "ticker": { "type": "string" },
                  "type": { "type": "string", "enum": ["equity", "fund", "crypto", "index"] },
                  "market": { "type": "string" }
                },
                "required": ["ticker", "type"],
                "additionalProperties": false
              }
            },
            "weather": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "location": { "type": "string" },
                  "start": { "type": "string", "description": "Optional YYYY-MM-DD date." },
                  "duration": { "type": "integer", "description": "Optional number of days." }
                },
                "required": ["location"],
                "additionalProperties": false
              }
            },
            "sports": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "fn": { "type": "string", "enum": ["schedule", "standings"] },
                  "league": {
                    "type": "string",
                    "enum": ["nba", "wnba", "nfl", "nhl", "mlb", "epl", "ncaamb", "ncaawb", "ipl"]
                  },
                  "team": { "type": "string" },
                  "opponent": { "type": "string" },
                  "date_from": { "type": "string", "description": "Optional YYYY-MM-DD date." },
                  "date_to": { "type": "string", "description": "Optional YYYY-MM-DD date." },
                  "num_games": { "type": "integer" },
                  "locale": { "type": "string" }
                },
                "required": ["fn", "league"],
                "additionalProperties": false
              }
            },
            "time": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "utc_offset": { "type": "string", "description": "UTC offset such as +03:00." }
                },
                "required": ["utc_offset"],
                "additionalProperties": false
              }
            },
            "response_length": {
              "type": "string",
              "enum": ["short", "medium", "long"],
              "description": "Optional response density hint. Defaults to medium."
            }
          },
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
            WebRunRequest request = new(
                responseLength.ToLowerInvariant(),
                ParseSearchQueries(context.Arguments, "search_query"),
                ParseSearchQueries(context.Arguments, "image_query"),
                ParseOpenRequests(context.Arguments),
                ParseFindRequests(context.Arguments),
                ParseScreenshotRequests(context.Arguments),
                ParseFinanceRequests(context.Arguments),
                ParseWeatherRequests(context.Arguments),
                ParseSportsRequests(context.Arguments),
                ParseTimeRequests(context.Arguments));

            if (GetOperationCount(request) == 0)
            {
                return InvalidArguments(
                    "Provide at least one operation array such as 'search_query', 'open', 'find', 'image_query', 'finance', 'weather', 'sports', or 'time'.");
            }

            WebRunResult result = await _webRunService.RunAsync(
                request,
                context.Session.SessionId,
                cancellationToken);

            return ToolResultFactory.Success(
                BuildSuccessMessage(result),
                result,
                ToolJsonContext.Default.WebRunResult,
                new ToolRenderPayload(
                    "web_run completed",
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
            "invalid_web_run_arguments",
            message,
            new ToolRenderPayload(
                "Invalid web_run arguments",
                message));
    }

    private static int GetOperationCount(WebRunRequest request)
    {
        return request.SearchQuery.Count +
               request.ImageQuery.Count +
               request.Open.Count +
               request.Find.Count +
               request.Screenshot.Count +
               request.Finance.Count +
               request.Weather.Count +
               request.Sports.Count +
               request.Time.Count;
    }

    private static IReadOnlyList<WebRunSearchQuery> ParseSearchQueries(
        JsonElement arguments,
        string propertyName)
    {
        return ParseArray(arguments, propertyName, item =>
        {
            string query = GetRequiredString(item, "q", propertyName);
            int? recency = GetOptionalInt(item, "recency");
            IReadOnlyList<string> domains = GetOptionalStringArray(item, "domains");
            return new WebRunSearchQuery(query, recency, domains);
        });
    }

    private static IReadOnlyList<WebRunOpenRequest> ParseOpenRequests(JsonElement arguments)
    {
        return ParseArray(arguments, "open", item =>
            new WebRunOpenRequest(
                GetRequiredString(item, "ref_id", "open"),
                GetOptionalInt(item, "lineno")));
    }

    private static IReadOnlyList<WebRunFindRequest> ParseFindRequests(JsonElement arguments)
    {
        return ParseArray(arguments, "find", item =>
            new WebRunFindRequest(
                GetRequiredString(item, "ref_id", "find"),
                GetRequiredString(item, "pattern", "find")));
    }

    private static IReadOnlyList<WebRunScreenshotRequest> ParseScreenshotRequests(JsonElement arguments)
    {
        return ParseArray(arguments, "screenshot", item =>
            new WebRunScreenshotRequest(
                GetRequiredString(item, "ref_id", "screenshot"),
                GetOptionalInt(item, "pageno")));
    }

    private static IReadOnlyList<WebRunFinanceRequest> ParseFinanceRequests(JsonElement arguments)
    {
        return ParseArray(arguments, "finance", item =>
            new WebRunFinanceRequest(
                GetRequiredString(item, "ticker", "finance"),
                GetRequiredString(item, "type", "finance"),
                ToolArguments.GetOptionalString(item, "market")));
    }

    private static IReadOnlyList<WebRunWeatherRequest> ParseWeatherRequests(JsonElement arguments)
    {
        return ParseArray(arguments, "weather", item =>
            new WebRunWeatherRequest(
                GetRequiredString(item, "location", "weather"),
                ToolArguments.GetOptionalString(item, "start"),
                GetOptionalInt(item, "duration")));
    }

    private static IReadOnlyList<WebRunSportsRequest> ParseSportsRequests(JsonElement arguments)
    {
        return ParseArray(arguments, "sports", item =>
            new WebRunSportsRequest(
                GetRequiredString(item, "fn", "sports"),
                GetRequiredString(item, "league", "sports"),
                ToolArguments.GetOptionalString(item, "team"),
                ToolArguments.GetOptionalString(item, "opponent"),
                ToolArguments.GetOptionalString(item, "date_from"),
                ToolArguments.GetOptionalString(item, "date_to"),
                GetOptionalInt(item, "num_games"),
                ToolArguments.GetOptionalString(item, "locale")));
    }

    private static IReadOnlyList<WebRunTimeRequest> ParseTimeRequests(JsonElement arguments)
    {
        return ParseArray(arguments, "time", item =>
            new WebRunTimeRequest(GetRequiredString(item, "utc_offset", "time")));
    }

    private static IReadOnlyList<T> ParseArray<T>(
        JsonElement arguments,
        string propertyName,
        Func<JsonElement, T> factory)
    {
        if (!arguments.TryGetProperty(propertyName, out JsonElement property))
        {
            return [];
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException($"Property '{propertyName}' must be an array.");
        }

        List<T> results = [];
        foreach (JsonElement item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException($"Each '{propertyName}' item must be an object.");
            }

            results.Add(factory(item));
        }

        return results;
    }

    private static string GetRequiredString(
        JsonElement element,
        string propertyName,
        string parentProperty)
    {
        if (!ToolArguments.TryGetNonEmptyString(element, propertyName, out string? value))
        {
            throw new ArgumentException(
                $"Each '{parentProperty}' item requires a non-empty '{propertyName}' string.");
        }

        return value!;
    }

    private static int? GetOptionalInt(
        JsonElement element,
        string propertyName)
    {
        return ToolArguments.TryGetInt32(element, propertyName, out int value)
            ? value
            : null;
    }

    private static IReadOnlyList<string> GetOptionalStringArray(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return [];
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException($"Property '{propertyName}' must be an array of strings.");
        }

        List<string> values = [];
        foreach (JsonElement item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new ArgumentException($"Property '{propertyName}' must be an array of strings.");
            }

            string? value = item.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static string BuildSuccessMessage(WebRunResult result)
    {
        int sections =
            CountNonEmpty(result.SearchQuery) +
            CountNonEmpty(result.ImageQuery) +
            CountNonEmpty(result.Open) +
            CountNonEmpty(result.Find) +
            CountNonEmpty(result.Screenshot) +
            CountNonEmpty(result.Finance) +
            CountNonEmpty(result.Weather) +
            CountNonEmpty(result.Sports) +
            CountNonEmpty(result.Time);

        return $"web_run completed {sections} {(sections == 1 ? "operation" : "operations")}.";
    }

    private static int CountNonEmpty<T>(IReadOnlyList<T> values)
    {
        return values.Count == 0 ? 0 : values.Count;
    }

    private static string BuildRenderText(WebRunResult result)
    {
        List<string> sections = [];

        if (result.SearchQuery.Count > 0)
        {
            sections.AddRange(result.SearchQuery.Select(static search =>
                $"Search '{search.Query}': {search.Results.Count} result(s)"));
        }

        if (result.ImageQuery.Count > 0)
        {
            sections.AddRange(result.ImageQuery.Select(static image =>
                $"Image search '{image.Query}': {image.Results.Count} result(s)"));
        }

        if (result.Open.Count > 0)
        {
            sections.AddRange(result.Open.Select(static open =>
                $"Open {open.ResolvedUrl}: {open.TotalLines} line(s)"));
        }

        if (result.Find.Count > 0)
        {
            sections.AddRange(result.Find.Select(static find =>
                $"Find '{find.Pattern}' in {find.RequestedRefId}: {find.Matches.Count} match(es)"));
        }

        if (result.Screenshot.Count > 0)
        {
            sections.AddRange(result.Screenshot.Select(static screenshot =>
                $"Screenshot {screenshot.ResolvedUrl}: {screenshot.ByteCount} byte(s)"));
        }

        if (result.Finance.Count > 0)
        {
            sections.AddRange(result.Finance.Select(static finance =>
                $"Finance {finance.Ticker}: {(finance.Price is null ? "n/a" : finance.Price.Value.ToString("0.####"))}"));
        }

        if (result.Weather.Count > 0)
        {
            sections.AddRange(result.Weather.Select(static weather =>
                $"Weather {weather.Location}: {weather.Condition ?? "n/a"}"));
        }

        if (result.Sports.Count > 0)
        {
            sections.AddRange(result.Sports.Select(static sports =>
                $"Sports {sports.League} {sports.Function}: {sports.Entries.Count} entrie(s)"));
        }

        if (result.Time.Count > 0)
        {
            sections.AddRange(result.Time.Select(static time =>
                $"Time {time.UtcOffset}: {time.DisplayTime}"));
        }

        if (result.Warnings.Count > 0)
        {
            sections.Add("Warnings:");
            sections.AddRange(result.Warnings);
        }

        return sections.Count == 0
            ? "No web_run output."
            : string.Join(Environment.NewLine, sections);
    }
}
