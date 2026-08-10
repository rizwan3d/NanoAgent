using StemCode.Application.Tools.Models;

namespace StemCode.Application.Abstractions;

public interface IHeadlessBrowserService
{
    Task<HeadlessBrowserResult> RunAsync(
        HeadlessBrowserRequest request,
        string sessionId,
        CancellationToken cancellationToken);
}
