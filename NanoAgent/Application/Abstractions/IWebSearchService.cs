using NanoAgent.Application.Tools.Models;

namespace NanoAgent.Application.Abstractions;

public interface IWebSearchService
{
    Task<WebSearchResult> RunAsync(
        WebSearchRequest request,
        string sessionId,
        CancellationToken cancellationToken);
}
