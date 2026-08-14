using StemCode.Application.Tools.Models;

namespace StemCode.Application.Abstractions;

public interface IWebSearchService
{
    Task<WebSearchResult> RunAsync(
        WebSearchRequest request,
        string sessionId,
        CancellationToken cancellationToken);
}
