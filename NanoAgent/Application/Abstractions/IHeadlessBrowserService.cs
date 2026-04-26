using NanoAgent.Application.Tools.Models;

namespace NanoAgent.Application.Abstractions;

public interface IHeadlessBrowserService
{
    Task<HeadlessBrowserResult> RunAsync(
        HeadlessBrowserRequest request,
        string sessionId,
        CancellationToken cancellationToken);
}
