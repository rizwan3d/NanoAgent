namespace NanoAgent.Application.Tools.Models;

public sealed record WebSearchRequest(
    string ResponseLength,
    IReadOnlyList<WebSearchQuery> SearchQuery);

public sealed record WebSearchQuery(
    string Query,
    int? NumResults = null);

public sealed record WebSearchResult(
    string ResponseLength,
    IReadOnlyList<WebSearchQueryResult> SearchQuery,
    IReadOnlyList<string> Warnings);

public sealed record WebSearchQueryResult(
    string Query,
    string Content,
    IReadOnlyList<WebSearchItem> Results,
    string? Warning = null);

public sealed record WebSearchItem(
    string Title,
    string Url,
    string? Published = null,
    string? Author = null);
