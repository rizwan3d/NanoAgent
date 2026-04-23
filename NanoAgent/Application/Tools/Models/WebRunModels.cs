namespace NanoAgent.Application.Tools.Models;

public sealed record WebRunRequest(
    string ResponseLength,
    IReadOnlyList<WebRunSearchQuery> SearchQuery,
    IReadOnlyList<WebRunSearchQuery> ImageQuery,
    IReadOnlyList<WebRunOpenRequest> Open,
    IReadOnlyList<WebRunFindRequest> Find,
    IReadOnlyList<WebRunScreenshotRequest> Screenshot,
    IReadOnlyList<WebRunFinanceRequest> Finance,
    IReadOnlyList<WebRunWeatherRequest> Weather,
    IReadOnlyList<WebRunSportsRequest> Sports,
    IReadOnlyList<WebRunTimeRequest> Time);

public sealed record WebRunSearchQuery(
    string Query,
    int? RecencyDays = null,
    IReadOnlyList<string>? Domains = null);

public sealed record WebRunOpenRequest(
    string RefId,
    int? LineNumber = null);

public sealed record WebRunFindRequest(
    string RefId,
    string Pattern);

public sealed record WebRunScreenshotRequest(
    string RefId,
    int? PageNumber = null);

public sealed record WebRunFinanceRequest(
    string Ticker,
    string Type,
    string? Market = null);

public sealed record WebRunWeatherRequest(
    string Location,
    string? StartDate = null,
    int? Duration = null);

public sealed record WebRunSportsRequest(
    string Function,
    string League,
    string? Team = null,
    string? Opponent = null,
    string? DateFrom = null,
    string? DateTo = null,
    int? NumGames = null,
    string? Locale = null);

public sealed record WebRunTimeRequest(string UtcOffset);

public sealed record WebRunResult(
    string ResponseLength,
    IReadOnlyList<WebRunSearchResult> SearchQuery,
    IReadOnlyList<WebRunImageResult> ImageQuery,
    IReadOnlyList<WebRunOpenResult> Open,
    IReadOnlyList<WebRunFindResult> Find,
    IReadOnlyList<WebRunScreenshotResult> Screenshot,
    IReadOnlyList<WebRunFinanceResult> Finance,
    IReadOnlyList<WebRunWeatherResult> Weather,
    IReadOnlyList<WebRunSportsResult> Sports,
    IReadOnlyList<WebRunTimeResult> Time,
    IReadOnlyList<string> Warnings);

public sealed record WebRunSearchResult(
    string Query,
    IReadOnlyList<WebRunSearchItem> Results,
    string? Warning = null);

public sealed record WebRunSearchItem(
    string RefId,
    string Title,
    string Url,
    string? DisplayUrl = null,
    string? Snippet = null);

public sealed record WebRunImageResult(
    string Query,
    IReadOnlyList<WebRunImageItem> Results,
    string? Warning = null);

public sealed record WebRunImageItem(
    string RefId,
    string Title,
    string ImageUrl,
    string? ThumbnailUrl = null,
    string? SourcePageUrl = null,
    int? Width = null,
    int? Height = null);

public sealed record WebRunOpenResult(
    string RequestedRefId,
    string ResolvedUrl,
    string? Title,
    string? ContentType,
    int StartLine,
    int EndLine,
    int TotalLines,
    string Text,
    string? Warning = null);

public sealed record WebRunFindResult(
    string RequestedRefId,
    string Pattern,
    IReadOnlyList<WebRunFindMatch> Matches,
    string? Warning = null);

public sealed record WebRunFindMatch(
    int LineNumber,
    string LineText);

public sealed record WebRunScreenshotResult(
    string RequestedRefId,
    string ResolvedUrl,
    string? ContentType,
    long ByteCount,
    string? ScreenshotUrl = null,
    string? Warning = null);

public sealed record WebRunFinanceResult(
    string Ticker,
    string Type,
    string? Market,
    decimal? Price,
    string? Currency,
    string? Exchange,
    string? MarketState,
    string? QuoteDate,
    decimal? ChangePercent = null,
    string? Warning = null);

public sealed record WebRunWeatherResult(
    string Location,
    string? ObservationTime,
    string? Condition,
    string? TemperatureC,
    IReadOnlyList<WebRunWeatherDay> Forecast,
    string? Warning = null);

public sealed record WebRunWeatherDay(
    string Date,
    string? MaxTempC,
    string? MinTempC,
    string? Condition);

public sealed record WebRunSportsResult(
    string Function,
    string League,
    IReadOnlyList<WebRunSportsEntry> Entries,
    string? Warning = null);

public sealed record WebRunSportsEntry(
    string Title,
    string? Subtitle = null,
    string? Status = null,
    string? Date = null);

public sealed record WebRunTimeResult(
    string UtcOffset,
    string IsoTime,
    string DisplayTime);
