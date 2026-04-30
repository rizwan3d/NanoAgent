using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NanoAgent.Infrastructure.Tools;

internal sealed partial class WebRunService : IWebRunService
{
    private const int DefaultSearchResultsShort = 3;
    private const int DefaultSearchResultsMedium = 5;
    private const int DefaultSearchResultsLong = 8;
    private const int DefaultOpenExcerptRadius = 12;
    private const int DefaultOpenStartLineCount = 40;
    private const int MaxFindMatches = 25;
    private const int MaxTextLength = 24_000;
    private const int MaxLineLength = 240;
    private const int ScreenshotTimeoutSeconds = 10;

    private static readonly IReadOnlyDictionary<string, SportsLeagueMapping> SportsLeagueMappings =
        new Dictionary<string, SportsLeagueMapping>(StringComparer.OrdinalIgnoreCase)
        {
            ["nba"] = new("basketball", "nba"),
            ["wnba"] = new("basketball", "wnba"),
            ["nfl"] = new("football", "nfl"),
            ["nhl"] = new("hockey", "nhl"),
            ["mlb"] = new("baseball", "mlb"),
            ["epl"] = new("soccer", "eng.1"),
            ["ncaamb"] = new("basketball", "mens-college-basketball"),
            ["ncaawb"] = new("basketball", "womens-college-basketball"),
            ["ipl"] = new("cricket", "ipl")
        };

    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, WebRunSessionState> _sessionState = new(StringComparer.Ordinal);

    public WebRunService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<WebRunResult> RunAsync(
        WebRunRequest request,
        string sessionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        cancellationToken.ThrowIfCancellationRequested();

        int maxResults = GetDefaultResultCount(request.ResponseLength);
        List<string> warnings = [];

        IReadOnlyList<WebRunSearchResult> searchResults = await RunSearchQueriesAsync(
            request.SearchQuery,
            sessionId,
            maxResults,
            warnings,
            cancellationToken);
        IReadOnlyList<WebRunImageResult> imageResults = await RunImageQueriesAsync(
            request.ImageQuery,
            sessionId,
            maxResults,
            warnings,
            cancellationToken);
        IReadOnlyList<WebRunOpenResult> openResults = await RunOpenRequestsAsync(
            request.Open,
            sessionId,
            warnings,
            cancellationToken);
        IReadOnlyList<WebRunFindResult> findResults = await RunFindRequestsAsync(
            request.Find,
            sessionId,
            warnings,
            cancellationToken);
        IReadOnlyList<WebRunScreenshotResult> screenshotResults = await RunScreenshotRequestsAsync(
            request.Screenshot,
            sessionId,
            warnings,
            cancellationToken);
        IReadOnlyList<WebRunFinanceResult> financeResults = await RunFinanceRequestsAsync(
            request.Finance,
            warnings,
            cancellationToken);
        IReadOnlyList<WebRunWeatherResult> weatherResults = await RunWeatherRequestsAsync(
            request.Weather,
            warnings,
            cancellationToken);
        IReadOnlyList<WebRunSportsResult> sportsResults = await RunSportsRequestsAsync(
            request.Sports,
            warnings,
            cancellationToken);
        IReadOnlyList<WebRunTimeResult> timeResults = RunTimeRequests(request.Time, warnings);

        return new WebRunResult(
            request.ResponseLength,
            searchResults,
            imageResults,
            openResults,
            findResults,
            screenshotResults,
            financeResults,
            weatherResults,
            sportsResults,
            timeResults,
            warnings);
    }

    private async Task<IReadOnlyList<WebRunSearchResult>> RunSearchQueriesAsync(
        IReadOnlyList<WebRunSearchQuery> queries,
        string sessionId,
        int maxResults,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        List<WebRunSearchResult> results = [];

        foreach (WebRunSearchQuery query in queries)
        {
            try
            {
                IReadOnlyList<WebRunSearchItem> items = await SearchAsync(
                    query,
                    sessionId,
                    maxResults,
                    cancellationToken);
                results.Add(new WebRunSearchResult(query.Query, items));
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException)
            {
                string warning = $"Search '{query.Query}' failed: {exception.Message}";
                warnings.Add(warning);
                results.Add(new WebRunSearchResult(query.Query, [], warning));
            }
        }

        return results;
    }

    private async Task<IReadOnlyList<WebRunImageResult>> RunImageQueriesAsync(
        IReadOnlyList<WebRunSearchQuery> queries,
        string sessionId,
        int maxResults,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        List<WebRunImageResult> results = [];

        foreach (WebRunSearchQuery query in queries)
        {
            try
            {
                IReadOnlyList<WebRunImageItem> items = await ImageSearchAsync(
                    query,
                    sessionId,
                    maxResults,
                    cancellationToken);
                results.Add(new WebRunImageResult(query.Query, items));
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException)
            {
                string warning = $"Image search '{query.Query}' failed: {exception.Message}";
                warnings.Add(warning);
                results.Add(new WebRunImageResult(query.Query, [], warning));
            }
        }

        return results;
    }

    private async Task<IReadOnlyList<WebRunOpenResult>> RunOpenRequestsAsync(
        IReadOnlyList<WebRunOpenRequest> requests,
        string sessionId,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        List<WebRunOpenResult> results = [];

        foreach (WebRunOpenRequest request in requests)
        {
            try
            {
                results.Add(await OpenAsync(request, sessionId, cancellationToken));
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
            {
                string warning = $"Open '{request.RefId}' failed: {exception.Message}";
                warnings.Add(warning);
                results.Add(new WebRunOpenResult(
                    request.RefId,
                    request.RefId,
                    null,
                    null,
                    0,
                    0,
                    0,
                    string.Empty,
                    warning));
            }
        }

        return results;
    }

    private async Task<IReadOnlyList<WebRunFindResult>> RunFindRequestsAsync(
        IReadOnlyList<WebRunFindRequest> requests,
        string sessionId,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        List<WebRunFindResult> results = [];

        foreach (WebRunFindRequest request in requests)
        {
            try
            {
                results.Add(await FindAsync(request, sessionId, cancellationToken));
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
            {
                string warning = $"Find '{request.Pattern}' in '{request.RefId}' failed: {exception.Message}";
                warnings.Add(warning);
                results.Add(new WebRunFindResult(request.RefId, request.Pattern, [], warning));
            }
        }

        return results;
    }

    private async Task<IReadOnlyList<WebRunScreenshotResult>> RunScreenshotRequestsAsync(
        IReadOnlyList<WebRunScreenshotRequest> requests,
        string sessionId,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        List<WebRunScreenshotResult> results = [];

        foreach (WebRunScreenshotRequest request in requests)
        {
            try
            {
                results.Add(await ScreenshotAsync(request, sessionId, cancellationToken));
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or TaskCanceledException)
            {
                string warning = $"Screenshot '{request.RefId}' failed: {exception.Message}";
                warnings.Add(warning);
                results.Add(new WebRunScreenshotResult(
                    request.RefId,
                    request.RefId,
                    null,
                    0,
                    null,
                    warning));
            }
        }

        return results;
    }

    private async Task<IReadOnlyList<WebRunFinanceResult>> RunFinanceRequestsAsync(
        IReadOnlyList<WebRunFinanceRequest> requests,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        List<WebRunFinanceResult> results = [];

        foreach (WebRunFinanceRequest request in requests)
        {
            try
            {
                results.Add(await GetFinanceAsync(request, cancellationToken));
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException)
            {
                string warning = $"Finance '{request.Ticker}' failed: {exception.Message}";
                warnings.Add(warning);
                results.Add(new WebRunFinanceResult(
                    request.Ticker,
                    request.Type,
                    request.Market,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    warning));
            }
        }

        return results;
    }

    private async Task<IReadOnlyList<WebRunWeatherResult>> RunWeatherRequestsAsync(
        IReadOnlyList<WebRunWeatherRequest> requests,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        List<WebRunWeatherResult> results = [];

        foreach (WebRunWeatherRequest request in requests)
        {
            try
            {
                results.Add(await GetWeatherAsync(request, cancellationToken));
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException)
            {
                string warning = $"Weather '{request.Location}' failed: {exception.Message}";
                warnings.Add(warning);
                results.Add(new WebRunWeatherResult(
                    request.Location,
                    null,
                    null,
                    null,
                    [],
                    warning));
            }
        }

        return results;
    }

    private async Task<IReadOnlyList<WebRunSportsResult>> RunSportsRequestsAsync(
        IReadOnlyList<WebRunSportsRequest> requests,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        List<WebRunSportsResult> results = [];

        foreach (WebRunSportsRequest request in requests)
        {
            try
            {
                results.Add(await GetSportsAsync(request, cancellationToken));
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException)
            {
                string warning = $"Sports '{request.League}' '{request.Function}' failed: {exception.Message}";
                warnings.Add(warning);
                results.Add(new WebRunSportsResult(request.Function, request.League, [], warning));
            }
        }

        return results;
    }

    private static IReadOnlyList<WebRunTimeResult> RunTimeRequests(
        IReadOnlyList<WebRunTimeRequest> requests,
        List<string> warnings)
    {
        List<WebRunTimeResult> results = [];

        foreach (WebRunTimeRequest request in requests)
        {
            if (!TimeSpan.TryParseExact(request.UtcOffset, @"hh\:mm", CultureInfo.InvariantCulture, out TimeSpan offset) &&
                !TimeSpan.TryParseExact(request.UtcOffset, @"\-hh\:mm", CultureInfo.InvariantCulture, out offset) &&
                !TimeSpan.TryParse(request.UtcOffset, CultureInfo.InvariantCulture, out offset))
            {
                warnings.Add($"Time '{request.UtcOffset}' failed: invalid UTC offset.");
                continue;
            }

            DateTimeOffset localTime = DateTimeOffset.UtcNow.ToOffset(offset);
            results.Add(new WebRunTimeResult(
                request.UtcOffset,
                localTime.ToString("O", CultureInfo.InvariantCulture),
                localTime.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)));
        }

        return results;
    }

    private async Task<IReadOnlyList<WebRunSearchItem>> SearchAsync(
        WebRunSearchQuery query,
        string sessionId,
        int maxResults,
        CancellationToken cancellationToken)
    {
        List<WebRunSearchItem> items = [];
        HashSet<string> seenUrls = new(StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<string> domains = (query.Domains ?? [])
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (domains.Count == 0)
        {
            string html = await GetStringAsync(BuildSearchUri(query.Query, query.RecencyDays, domain: null), cancellationToken);
            foreach (ParsedSearchResult parsed in ParseSearchResults(html))
            {
                if (seenUrls.Add(parsed.Url))
                {
                    items.Add(CreateSearchItem(sessionId, parsed));
                }

                if (items.Count >= maxResults)
                {
                    break;
                }
            }

            return items;
        }

        foreach (string domain in domains)
        {
            string html = await GetStringAsync(BuildSearchUri(query.Query, query.RecencyDays, domain), cancellationToken);
            foreach (ParsedSearchResult parsed in ParseSearchResults(html))
            {
                if (!MatchesAnyDomain(parsed.Url, domains) || !seenUrls.Add(parsed.Url))
                {
                    continue;
                }

                items.Add(CreateSearchItem(sessionId, parsed));
                if (items.Count >= maxResults)
                {
                    return items;
                }
            }
        }

        return items;
    }

    private async Task<IReadOnlyList<WebRunImageItem>> ImageSearchAsync(
        WebRunSearchQuery query,
        string sessionId,
        int maxResults,
        CancellationToken cancellationToken)
    {
        string searchText = BuildSearchText(query.Query, query.Domains);
        string html = await GetStringAsync(BuildImageQueryPageUri(searchText), cancellationToken);
        string vqd = ExtractVqd(html);
        string json = await GetStringAsync(BuildImageSearchUri(searchText, vqd), cancellationToken);

        using JsonDocument document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("results", out JsonElement resultsElement) ||
            resultsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<WebRunImageItem> results = [];
        HashSet<string> seenImages = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement item in resultsElement.EnumerateArray())
        {
            string? imageUrl = GetOptionalString(item, "image");
            if (string.IsNullOrWhiteSpace(imageUrl) || !seenImages.Add(imageUrl))
            {
                continue;
            }

            string? sourcePageUrl = GetOptionalString(item, "url");
            if ((query.Domains?.Count ?? 0) > 0 &&
                !MatchesAnyDomain(sourcePageUrl, query.Domains!))
            {
                continue;
            }

            string title = GetOptionalString(item, "title") ?? imageUrl;
            string thumbnailUrl = GetOptionalString(item, "thumbnail") ?? imageUrl;
            int? width = GetOptionalInt(item, "width");
            int? height = GetOptionalInt(item, "height");

            string refId = StoreReference(sessionId, imageUrl, title, sourcePageUrl, textLines: null, contentType: "image");
            results.Add(new WebRunImageItem(
                refId,
                CleanupHtmlText(title),
                imageUrl,
                thumbnailUrl,
                sourcePageUrl,
                width,
                height));

            if (results.Count >= maxResults)
            {
                break;
            }
        }

        return results;
    }

    private async Task<WebRunOpenResult> OpenAsync(
        WebRunOpenRequest request,
        string sessionId,
        CancellationToken cancellationToken)
    {
        ResolvedWebReference reference = ResolveReference(sessionId, request.RefId);
        StoredPageContent pageContent = await GetPageContentAsync(reference.Url, cancellationToken);
        StoreReference(
            sessionId,
            reference.Url,
            pageContent.Title,
            sourceUrl: reference.Url,
            pageContent.Lines,
            pageContent.ContentType);

        IReadOnlyList<string> lines = pageContent.Lines;
        if (lines.Count == 0)
        {
            return new WebRunOpenResult(
                request.RefId,
                reference.Url,
                pageContent.Title,
                pageContent.ContentType,
                0,
                0,
                0,
                string.Empty,
                "No readable text content was extracted.");
        }

        (int startIndex, int endIndex) = GetExcerptWindow(lines.Count, request.LineNumber);
        string excerpt = FormatLines(lines, startIndex, endIndex);

        return new WebRunOpenResult(
            request.RefId,
            reference.Url,
            pageContent.Title,
            pageContent.ContentType,
            startIndex + 1,
            endIndex + 1,
            lines.Count,
            excerpt);
    }

    private async Task<WebRunFindResult> FindAsync(
        WebRunFindRequest request,
        string sessionId,
        CancellationToken cancellationToken)
    {
        ResolvedWebReference reference = ResolveReference(sessionId, request.RefId);
        StoredPageContent pageContent = await GetPageContentAsync(reference.Url, cancellationToken);
        List<WebRunFindMatch> matches = [];

        for (int index = 0; index < pageContent.Lines.Count; index++)
        {
            string line = pageContent.Lines[index];
            if (line.Contains(request.Pattern, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(new WebRunFindMatch(index + 1, line));
            }

            if (matches.Count >= MaxFindMatches)
            {
                break;
            }
        }

        return new WebRunFindResult(request.RefId, request.Pattern, matches);
    }

    private async Task<WebRunScreenshotResult> ScreenshotAsync(
        WebRunScreenshotRequest request,
        string sessionId,
        CancellationToken cancellationToken)
    {
        ResolvedWebReference reference = ResolveReference(sessionId, request.RefId);
        string? contentType = reference.ContentType;

        if (IsLikelyImageUrl(reference.Url, contentType))
        {
            byte[] bytes = await GetBytesAsync(reference.Url, cancellationToken);
            return new WebRunScreenshotResult(
                request.RefId,
                reference.Url,
                contentType ?? "image",
                bytes.LongLength,
                reference.Url,
                request.PageNumber is null ? null : "Screenshot page selection is ignored for direct image targets.");
        }

        string screenshotUrl = $"https://image.thum.io/get/png/noanimate/{reference.Url}";
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(ScreenshotTimeoutSeconds));
        byte[] screenshotBytes = await GetBytesAsync(screenshotUrl, timeoutSource.Token);

        return new WebRunScreenshotResult(
            request.RefId,
            reference.Url,
            "image/png",
            screenshotBytes.LongLength,
            screenshotUrl,
            request.PageNumber is null ? null : $"Requested page {request.PageNumber.Value}; screenshot providers may ignore page selection.");
    }

    private async Task<WebRunFinanceResult> GetFinanceAsync(
        WebRunFinanceRequest request,
        CancellationToken cancellationToken)
    {
        string symbol = NormalizeFinanceSymbol(request);
        string json = await GetStringAsync(
            new Uri($"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?interval=1d&range=1d", UriKind.Absolute),
            cancellationToken);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement result = document.RootElement.GetProperty("chart").GetProperty("result")[0];
        JsonElement meta = result.GetProperty("meta");

        decimal? price = GetOptionalDecimal(meta, "regularMarketPrice");
        decimal? previousClose = GetOptionalDecimal(meta, "chartPreviousClose") ?? GetOptionalDecimal(meta, "previousClose");
        decimal? changePercent = price is not null && previousClose is > 0
            ? Math.Round(((price.Value - previousClose.Value) / previousClose.Value) * 100m, 4)
            : null;
        string? quoteDate = GetOptionalUnixTime(meta, "regularMarketTime");

        return new WebRunFinanceResult(
            request.Ticker,
            request.Type,
            request.Market,
            price,
            GetOptionalString(meta, "currency"),
            GetOptionalString(meta, "fullExchangeName") ?? GetOptionalString(meta, "exchangeName"),
            GetOptionalString(meta, "marketState") ?? GetOptionalString(meta, "exchangeName"),
            quoteDate,
            changePercent);
    }

    private async Task<WebRunWeatherResult> GetWeatherAsync(
        WebRunWeatherRequest request,
        CancellationToken cancellationToken)
    {
        string json = await GetStringAsync(
            new Uri($"https://wttr.in/{Uri.EscapeDataString(request.Location)}?format=j1", UriKind.Absolute),
            cancellationToken);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        JsonElement current = root.GetProperty("current_condition")[0];
        JsonElement weather = root.GetProperty("weather");
        DateOnly? startDate = TryParseDateOnly(request.StartDate);
        int duration = Math.Max(1, request.Duration ?? weather.GetArrayLength());

        List<WebRunWeatherDay> forecast = [];
        foreach (JsonElement day in weather.EnumerateArray())
        {
            string date = GetOptionalString(day, "date") ?? string.Empty;
            if (startDate is not null &&
                (!DateOnly.TryParse(date, CultureInfo.InvariantCulture, out DateOnly parsedDate) || parsedDate < startDate.Value))
            {
                continue;
            }

            string? condition = null;
            if (day.TryGetProperty("hourly", out JsonElement hourly) &&
                hourly.ValueKind == JsonValueKind.Array &&
                hourly.GetArrayLength() > 0)
            {
                JsonElement firstHourly = hourly[0];
                if (firstHourly.TryGetProperty("weatherDesc", out JsonElement weatherDesc) &&
                    weatherDesc.ValueKind == JsonValueKind.Array &&
                    weatherDesc.GetArrayLength() > 0)
                {
                    condition = GetOptionalString(weatherDesc[0], "value");
                }
            }

            forecast.Add(new WebRunWeatherDay(
                date,
                GetOptionalString(day, "maxtempC"),
                GetOptionalString(day, "mintempC"),
                condition));

            if (forecast.Count >= duration)
            {
                break;
            }
        }

        return new WebRunWeatherResult(
            request.Location,
            GetOptionalString(current, "localObsDateTime"),
            GetNestedArrayValue(current, "weatherDesc", "value"),
            GetOptionalString(current, "temp_C"),
            forecast);
    }

    private async Task<WebRunSportsResult> GetSportsAsync(
        WebRunSportsRequest request,
        CancellationToken cancellationToken)
    {
        if (!SportsLeagueMappings.TryGetValue(request.League, out SportsLeagueMapping? mapping))
        {
            throw new InvalidOperationException($"Unsupported league '{request.League}'.");
        }

        return string.Equals(request.Function, "standings", StringComparison.OrdinalIgnoreCase)
            ? await GetSportsStandingsAsync(request, mapping, cancellationToken)
            : await GetSportsScheduleAsync(request, mapping, cancellationToken);
    }

    private async Task<WebRunSportsResult> GetSportsScheduleAsync(
        WebRunSportsRequest request,
        SportsLeagueMapping mapping,
        CancellationToken cancellationToken)
    {
        string? dates = BuildEspnDatesRange(request.DateFrom, request.DateTo);
        StringBuilder uriBuilder = new(
            $"https://site.api.espn.com/apis/site/v2/sports/{mapping.Sport}/{mapping.League}/scoreboard");
        if (!string.IsNullOrWhiteSpace(dates))
        {
            uriBuilder.Append(uriBuilder.ToString().Contains('?') ? '&' : '?');
            uriBuilder.Append("dates=").Append(dates);
        }

        int limit = Math.Clamp(request.NumGames ?? 20, 1, 50);
        uriBuilder.Append(uriBuilder.ToString().Contains('?') ? '&' : '?');
        uriBuilder.Append("limit=").Append(limit.ToString(CultureInfo.InvariantCulture));

        string json = await GetStringAsync(new Uri(uriBuilder.ToString(), UriKind.Absolute), cancellationToken);
        using JsonDocument document = JsonDocument.Parse(json);
        List<WebRunSportsEntry> entries = [];

        foreach (JsonElement eventElement in document.RootElement.GetProperty("events").EnumerateArray())
        {
            JsonElement competition = eventElement.GetProperty("competitions")[0];
            JsonElement competitors = competition.GetProperty("competitors");
            if (!MatchesCompetitors(competitors, request.Team, request.Opponent))
            {
                continue;
            }

            string title = GetOptionalString(eventElement, "shortName") ?? GetOptionalString(eventElement, "name") ?? "Game";
            string subtitle = BuildCompetitionSummary(competitors);
            string? status = competition.TryGetProperty("status", out JsonElement statusElement)
                ? GetNestedValue(statusElement, "type", "shortDetail")
                : null;
            string? date = GetOptionalString(eventElement, "date");
            entries.Add(new WebRunSportsEntry(title, subtitle, status, date));

            if (entries.Count >= limit)
            {
                break;
            }
        }

        return new WebRunSportsResult(request.Function, request.League, entries);
    }

    private async Task<WebRunSportsResult> GetSportsStandingsAsync(
        WebRunSportsRequest request,
        SportsLeagueMapping mapping,
        CancellationToken cancellationToken)
    {
        string json = await GetStringAsync(
            new Uri($"https://site.api.espn.com/apis/v2/sports/{mapping.Sport}/{mapping.League}/standings", UriKind.Absolute),
            cancellationToken);

        using JsonDocument document = JsonDocument.Parse(json);
        List<WebRunSportsEntry> entries = [];
        CollectStandingsEntries(document.RootElement, request.Team, entries);
        return new WebRunSportsResult(request.Function, request.League, entries);
    }

    private static void CollectStandingsEntries(
        JsonElement element,
        string? teamFilter,
        List<WebRunSportsEntry> entries)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("standings", out JsonElement standings) &&
                standings.ValueKind == JsonValueKind.Object &&
                standings.TryGetProperty("entries", out JsonElement standingsEntries) &&
                standingsEntries.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement entry in standingsEntries.EnumerateArray())
                {
                    JsonElement team = entry.GetProperty("team");
                    string teamName = GetOptionalString(team, "displayName") ?? GetOptionalString(team, "name") ?? "Team";
                    string abbreviation = GetOptionalString(team, "abbreviation") ?? teamName;
                    if (!MatchesTeam(teamFilter, teamName, abbreviation))
                    {
                        continue;
                    }

                    string title = $"{GetTeamRank(entry)}. {teamName}";
                    string subtitle = BuildStandingSummary(entry);
                    entries.Add(new WebRunSportsEntry(title, subtitle));
                }
            }

            foreach (JsonProperty property in element.EnumerateObject())
            {
                CollectStandingsEntries(property.Value, teamFilter, entries);
            }

            return;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement item in element.EnumerateArray())
        {
            CollectStandingsEntries(item, teamFilter, entries);
        }
    }

    private async Task<StoredPageContent> GetPageContentAsync(
        string url,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, url),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        string? contentType = response.Content.Headers.ContentType?.MediaType;
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (IsHtmlContentType(contentType) || LooksLikeHtml(body))
        {
            string? title = ExtractTitle(body);
            IReadOnlyList<string> lines = ExtractHtmlLines(body);
            return new StoredPageContent(title, contentType ?? "text/html", lines);
        }

        if (IsJsonContentType(contentType))
        {
            string formattedJson = FormatJson(body);
            return new StoredPageContent(null, contentType, SplitLines(formattedJson));
        }

        return new StoredPageContent(null, contentType, SplitLines(body));
    }

    private WebRunSearchItem CreateSearchItem(
        string sessionId,
        ParsedSearchResult result)
    {
        string refId = StoreReference(sessionId, result.Url, result.Title, result.Url, textLines: null, contentType: "text/html");
        return new WebRunSearchItem(refId, result.Title, result.Url, result.DisplayUrl, result.Snippet);
    }

    private ResolvedWebReference ResolveReference(
        string sessionId,
        string refIdOrUrl)
    {
        if (Uri.TryCreate(refIdOrUrl, UriKind.Absolute, out Uri? directUri))
        {
            return new ResolvedWebReference(refIdOrUrl, directUri.ToString(), null, null);
        }

        WebRunSessionState state = _sessionState.GetOrAdd(sessionId, static _ => new WebRunSessionState());
        if (!state.TryGet(refIdOrUrl, out StoredWebReference? reference) || reference is null)
        {
            throw new InvalidOperationException($"Unknown web reference '{refIdOrUrl}'.");
        }

        return new ResolvedWebReference(refIdOrUrl, reference.Url, reference.ContentType, reference.Lines);
    }

    private string StoreReference(
        string sessionId,
        string url,
        string? title,
        string? sourceUrl,
        IReadOnlyList<string>? textLines,
        string? contentType)
    {
        WebRunSessionState state = _sessionState.GetOrAdd(sessionId, static _ => new WebRunSessionState());
        string refId = state.CreateRefId();
        state.Store(new StoredWebReference(refId, url, title, sourceUrl, textLines?.ToArray(), contentType));
        return refId;
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

    private static Uri BuildSearchUri(
        string query,
        int? recencyDays,
        string? domain)
    {
        string searchText = BuildSearchText(query, string.IsNullOrWhiteSpace(domain) ? [] : [domain]);
        StringBuilder builder = new($"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(searchText)}");

        string? recencyToken = GetDuckDuckGoRecencyToken(recencyDays);
        if (recencyToken is not null)
        {
            builder.Append("&df=").Append(recencyToken);
        }

        return new Uri(builder.ToString(), UriKind.Absolute);
    }

    private static Uri BuildImageQueryPageUri(string searchText)
    {
        return new($"https://duckduckgo.com/?q={Uri.EscapeDataString(searchText)}&iax=images&ia=images", UriKind.Absolute);
    }

    private static Uri BuildImageSearchUri(
        string searchText,
        string vqd)
    {
        return new(
            $"https://duckduckgo.com/i.js?l=us-en&o=json&q={Uri.EscapeDataString(searchText)}&vqd={Uri.EscapeDataString(vqd)}&f=,,,&p=1",
            UriKind.Absolute);
    }

    private static string BuildSearchText(
        string query,
        IReadOnlyList<string>? domains)
    {
        StringBuilder builder = new(query.Trim());
        foreach (string domain in (domains ?? []).Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            builder.Append(" site:").Append(domain.Trim());
        }

        return builder.ToString();
    }

    private static string? GetDuckDuckGoRecencyToken(int? recencyDays)
    {
        return recencyDays switch
        {
            null => null,
            <= 1 => "d",
            <= 7 => "w",
            <= 31 => "m",
            _ => "y"
        };
    }

    private static IReadOnlyList<ParsedSearchResult> ParseSearchResults(string html)
    {
        List<ParsedSearchResult> results = [];
        HashSet<string> seenUrls = new(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in SearchResultRegex().Matches(html).Cast<Match>())
        {
            string url = NormalizeResultUrl(match.Groups["href"].Value);
            if (string.IsNullOrWhiteSpace(url) || !seenUrls.Add(url))
            {
                continue;
            }

            string title = CleanupHtmlText(match.Groups["title"].Value);
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            string rest = match.Groups["rest"].Value;
            string? displayUrl = TryMatchGroup(SearchResultUrlRegex(), rest, "displayUrl");
            string? snippet = TryMatchGroup(SearchResultSnippetRegex(), rest, "snippet");
            results.Add(new ParsedSearchResult(title, url, displayUrl, snippet));
        }

        return results;
    }

    private static string ExtractVqd(string html)
    {
        Match doubleQuoteMatch = DuckDuckGoVqdDoubleQuoteRegex().Match(html);
        if (doubleQuoteMatch.Success)
        {
            return doubleQuoteMatch.Groups["vqd"].Value;
        }

        Match singleQuoteMatch = DuckDuckGoVqdSingleQuoteRegex().Match(html);
        if (singleQuoteMatch.Success)
        {
            return singleQuoteMatch.Groups["vqd"].Value;
        }

        throw new InvalidOperationException("DuckDuckGo image token was not found.");
    }

    private static string? TryMatchGroup(
        Regex regex,
        string value,
        string groupName)
    {
        Match match = regex.Match(value);
        if (!match.Success)
        {
            return null;
        }

        return CleanupHtmlText(match.Groups[groupName].Value);
    }

    private static bool IsHtmlContentType(string? contentType)
    {
        return contentType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsJsonContentType(string? contentType)
    {
        return contentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool LooksLikeHtml(string value)
    {
        return value.Contains("<html", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("<body", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractTitle(string html)
    {
        Match match = HtmlTitleRegex().Match(html);
        return match.Success
            ? CleanupHtmlText(match.Groups["title"].Value)
            : null;
    }

    private static IReadOnlyList<string> ExtractHtmlLines(string html)
    {
        string withoutScripts = HtmlScriptRegex().Replace(html, Environment.NewLine);
        string withoutStyles = HtmlStyleRegex().Replace(withoutScripts, Environment.NewLine);
        string withLineBreaks = HtmlBlockBreakRegex().Replace(withoutStyles, Environment.NewLine);
        string withoutTags = HtmlTagRegex().Replace(withLineBreaks, string.Empty);
        string decoded = WebUtility.HtmlDecode(withoutTags);
        return SplitLines(decoded);
    }

    private static IReadOnlyList<string> SplitLines(string value)
    {
        string normalized = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        List<string> lines = [];
        foreach (string line in normalized.Split('\n', StringSplitOptions.None))
        {
            string collapsed = WhitespaceRegex().Replace(line, " ").Trim();
            if (!string.IsNullOrWhiteSpace(collapsed))
            {
                lines.Add(collapsed.Length > MaxLineLength
                    ? collapsed[..MaxLineLength]
                    : collapsed);
            }
        }

        int totalLength = 0;
        List<string> limited = [];
        foreach (string line in lines)
        {
            if (totalLength + line.Length > MaxTextLength)
            {
                break;
            }

            limited.Add(line);
            totalLength += line.Length;
        }

        return limited;
    }

    private static string FormatJson(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(
            document.RootElement,
            ToolJsonContext.Default.JsonElement);
    }

    private static (int StartIndex, int EndIndex) GetExcerptWindow(
        int lineCount,
        int? lineNumber)
    {
        if (lineCount == 0)
        {
            return (0, 0);
        }

        if (lineNumber is null || lineNumber.Value <= 0)
        {
            int initialEndIndex = Math.Min(lineCount, DefaultOpenStartLineCount) - 1;
            return (0, Math.Max(0, initialEndIndex));
        }

        int centerIndex = Math.Clamp(lineNumber.Value - 1, 0, lineCount - 1);
        int startIndex = Math.Max(0, centerIndex - DefaultOpenExcerptRadius);
        int endIndex = Math.Min(lineCount - 1, centerIndex + DefaultOpenExcerptRadius);
        return (startIndex, endIndex);
    }

    private static string FormatLines(
        IReadOnlyList<string> lines,
        int startIndex,
        int endIndex)
    {
        List<string> formatted = [];
        for (int index = startIndex; index <= endIndex && index < lines.Count; index++)
        {
            formatted.Add($"{index + 1}: {lines[index]}");
        }

        return string.Join(Environment.NewLine, formatted);
    }

    private static bool MatchesAnyDomain(
        string? url,
        IReadOnlyList<string> domains)
    {
        if (string.IsNullOrWhiteSpace(url) || domains.Count == 0)
        {
            return domains.Count == 0;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        return domains.Any(domain =>
            uri.Host.EndsWith(domain.Trim().TrimStart('.'), StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLikelyImageUrl(
        string url,
        string? contentType)
    {
        return contentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true ||
               url.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
               url.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
               url.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               url.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
               url.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeFinanceSymbol(WebRunFinanceRequest request)
    {
        string symbol = request.Ticker.Trim();
        if (string.Equals(request.Type, "crypto", StringComparison.OrdinalIgnoreCase) &&
            !symbol.Contains('-', StringComparison.Ordinal))
        {
            return symbol + "-USD";
        }

        return symbol;
    }

    private static string? BuildEspnDatesRange(
        string? dateFrom,
        string? dateTo)
    {
        string? start = NormalizeEspnDate(dateFrom);
        string? end = NormalizeEspnDate(dateTo);

        if (start is null && end is null)
        {
            return null;
        }

        if (start is null)
        {
            return end;
        }

        if (end is null || string.Equals(start, end, StringComparison.Ordinal))
        {
            return start;
        }

        return $"{start}-{end}";
    }

    private static string? NormalizeEspnDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateOnly.TryParse(value, CultureInfo.InvariantCulture, out DateOnly parsedDate)
            ? parsedDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
            : null;
    }

    private static bool MatchesCompetitors(
        JsonElement competitors,
        string? team,
        string? opponent)
    {
        if (competitors.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        List<(string Name, string Abbreviation)> values = [];
        foreach (JsonElement competitor in competitors.EnumerateArray())
        {
            JsonElement teamElement = competitor.GetProperty("team");
            values.Add((
                GetOptionalString(teamElement, "displayName") ?? GetOptionalString(teamElement, "name") ?? string.Empty,
                GetOptionalString(teamElement, "abbreviation") ?? string.Empty));
        }

        bool teamMatches = string.IsNullOrWhiteSpace(team) ||
                           values.Any(value => MatchesTeam(team, value.Name, value.Abbreviation));
        bool opponentMatches = string.IsNullOrWhiteSpace(opponent) ||
                               values.Any(value => MatchesTeam(opponent, value.Name, value.Abbreviation));

        return teamMatches && opponentMatches;
    }

    private static bool MatchesTeam(
        string? filter,
        string teamName,
        string abbreviation)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        string normalizedFilter = filter.Trim();
        return string.Equals(abbreviation, normalizedFilter, StringComparison.OrdinalIgnoreCase) ||
               teamName.Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildCompetitionSummary(JsonElement competitors)
    {
        List<string> parts = [];
        foreach (JsonElement competitor in competitors.EnumerateArray())
        {
            JsonElement team = competitor.GetProperty("team");
            string abbreviation = GetOptionalString(team, "abbreviation") ?? GetOptionalString(team, "shortDisplayName") ?? "Team";
            string? score = GetOptionalString(competitor, "score");
            parts.Add(string.IsNullOrWhiteSpace(score)
                ? abbreviation
                : $"{abbreviation} {score}");
        }

        return string.Join(" vs ", parts);
    }

    private static string GetTeamRank(JsonElement entry)
    {
        if (entry.TryGetProperty("stats", out JsonElement stats) &&
            stats.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement stat in stats.EnumerateArray())
            {
                string? name = GetOptionalString(stat, "name");
                if (string.Equals(name, "rank", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "playoffSeed", StringComparison.OrdinalIgnoreCase))
                {
                    return GetOptionalString(stat, "displayValue") ??
                           GetOptionalString(stat, "value") ??
                           "?";
                }
            }
        }

        return "?";
    }

    private static string BuildStandingSummary(JsonElement entry)
    {
        if (!entry.TryGetProperty("stats", out JsonElement stats) ||
            stats.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        string? wins = null;
        string? losses = null;
        string? percentage = null;
        foreach (JsonElement stat in stats.EnumerateArray())
        {
            string? name = GetOptionalString(stat, "name");
            string? value = GetOptionalString(stat, "displayValue") ?? GetOptionalString(stat, "value");
            switch (name)
            {
                case "wins":
                    wins = value;
                    break;
                case "losses":
                    losses = value;
                    break;
                case "winPercent":
                    percentage = value;
                    break;
            }
        }

        List<string> parts = [];
        if (!string.IsNullOrWhiteSpace(wins) && !string.IsNullOrWhiteSpace(losses))
        {
            parts.Add($"{wins}-{losses}");
        }

        if (!string.IsNullOrWhiteSpace(percentage))
        {
            parts.Add($"win% {percentage}");
        }

        return string.Join(" | ", parts);
    }

    private async Task<string> GetStringAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("Accept", "text/html,application/json;q=0.9,*/*;q=0.8");
        request.Headers.Referrer = new Uri("https://duckduckgo.com/", UriKind.Absolute);

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task<byte[]> GetBytesAsync(
        string url,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Accept", "*/*");

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private static string CleanupHtmlText(string value)
    {
        string withoutTags = HtmlTagRegex().Replace(value, string.Empty);
        string decoded = WebUtility.HtmlDecode(withoutTags);
        return WhitespaceRegex().Replace(decoded, " ").Trim();
    }

    private static string NormalizeResultUrl(string rawHref)
    {
        string value = WebUtility.HtmlDecode(rawHref).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (value.StartsWith("//", StringComparison.Ordinal))
        {
            value = "https:" + value;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
        {
            return value;
        }

        if (!uri.Host.Contains("duckduckgo.com", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.AbsolutePath, "/l/", StringComparison.Ordinal))
        {
            return uri.ToString();
        }

        string? redirectedUrl = TryGetQueryParameter(uri.Query, "uddg");
        return string.IsNullOrWhiteSpace(redirectedUrl)
            ? uri.ToString()
            : redirectedUrl;
    }

    private static string? TryGetQueryParameter(
        string query,
        string key)
    {
        string trimmedQuery = query.TrimStart('?');
        if (trimmedQuery.Length == 0)
        {
            return null;
        }

        foreach (string pair in trimmedQuery.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separatorIndex = pair.IndexOf('=');
            string encodedName = separatorIndex >= 0 ? pair[..separatorIndex] : pair;
            if (!string.Equals(WebUtility.UrlDecode(encodedName), key, StringComparison.Ordinal))
            {
                continue;
            }

            string encodedValue = separatorIndex >= 0 ? pair[(separatorIndex + 1)..] : string.Empty;
            return WebUtility.UrlDecode(encodedValue);
        }

        return null;
    }

    private static string? GetOptionalString(
        JsonElement element,
        string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static int? GetOptionalInt(
        JsonElement element,
        string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out int value)
            ? value
            : null;
    }

    private static decimal? GetOptionalDecimal(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return property.TryGetDecimal(out decimal value)
            ? value
            : null;
    }

    private static string? GetOptionalUnixTime(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt64(out long value))
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(value)
            .ToString("O", CultureInfo.InvariantCulture);
    }

    private static string? GetNestedValue(
        JsonElement element,
        string childProperty,
        string valueProperty)
    {
        return element.TryGetProperty(childProperty, out JsonElement child)
            ? GetOptionalString(child, valueProperty)
            : null;
    }

    private static string? GetNestedArrayValue(
        JsonElement element,
        string arrayProperty,
        string valueProperty)
    {
        if (!element.TryGetProperty(arrayProperty, out JsonElement array) ||
            array.ValueKind != JsonValueKind.Array ||
            array.GetArrayLength() == 0)
        {
            return null;
        }

        return GetOptionalString(array[0], valueProperty);
    }

    private static DateOnly? TryParseDateOnly(string? value)
    {
        return DateOnly.TryParse(value, CultureInfo.InvariantCulture, out DateOnly parsed)
            ? parsed
            : null;
    }

    [GeneratedRegex(
        "<h2 class=\"result__title\">\\s*<a[^>]*class=\"result__a\"[^>]*href=\"(?<href>[^\"]+)\"[^>]*>(?<title>.*?)</a>\\s*</h2>(?<rest>.*?)(?=<h2 class=\"result__title\">|<div class=\"nav-link\">|</body>)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex SearchResultRegex();

    [GeneratedRegex(
        "<a class=\"result__url\"[^>]*>\\s*(?<displayUrl>.*?)\\s*</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex SearchResultUrlRegex();

    [GeneratedRegex(
        "<a class=\"result__snippet\"[^>]*>(?<snippet>.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex SearchResultSnippetRegex();

    [GeneratedRegex(
        "<title[^>]*>(?<title>.*?)</title>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTitleRegex();

    [GeneratedRegex(
        "<script\\b.*?</script>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlScriptRegex();

    [GeneratedRegex(
        "<style\\b.*?</style>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlStyleRegex();

    [GeneratedRegex(
        "</?(?:p|br|div|li|tr|h1|h2|h3|h4|h5|h6|section|article|header|footer|main|aside|ul|ol|table)[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlBlockBreakRegex();

    [GeneratedRegex(
        "<.*?>",
        RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(
        "\\s+",
        RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(
        "vqd=\"(?<vqd>[^\"]+)\"",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DuckDuckGoVqdDoubleQuoteRegex();

    [GeneratedRegex(
        "vqd='(?<vqd>[^']+)'",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DuckDuckGoVqdSingleQuoteRegex();

    private sealed record ParsedSearchResult(
        string Title,
        string Url,
        string? DisplayUrl,
        string? Snippet);

    private sealed record StoredPageContent(
        string? Title,
        string? ContentType,
        IReadOnlyList<string> Lines);

    private sealed record ResolvedWebReference(
        string RequestedRefId,
        string Url,
        string? ContentType,
        IReadOnlyList<string>? Lines);

    private sealed record StoredWebReference(
        string RefId,
        string Url,
        string? Title,
        string? SourceUrl,
        IReadOnlyList<string>? Lines,
        string? ContentType);

    private sealed class WebRunSessionState
    {
        private readonly object _syncRoot = new();
        private readonly Dictionary<string, StoredWebReference> _references = new(StringComparer.Ordinal);
        private int _nextId = 1;

        public string CreateRefId()
        {
            lock (_syncRoot)
            {
                return $"web_run_{_nextId++}";
            }
        }

        public void Store(StoredWebReference reference)
        {
            lock (_syncRoot)
            {
                _references[reference.RefId] = reference;
            }
        }

        public bool TryGet(string refId, out StoredWebReference? reference)
        {
            lock (_syncRoot)
            {
                return _references.TryGetValue(refId, out reference);
            }
        }
    }

    private sealed record SportsLeagueMapping(string Sport, string League);
}
