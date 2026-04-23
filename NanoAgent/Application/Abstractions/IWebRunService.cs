using NanoAgent.Application.Tools.Models;

namespace NanoAgent.Application.Abstractions;

public interface IWebRunService
{
    Task<WebRunResult> RunAsync(
        WebRunRequest request,
        string sessionId,
        CancellationToken cancellationToken);
}
