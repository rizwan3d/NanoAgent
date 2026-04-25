using NanoAgent.Application.Models;

namespace NanoAgent.Application.Abstractions;

public interface ILifecycleHookService
{
    Task<LifecycleHookRunResult> RunAsync(
        LifecycleHookContext context,
        CancellationToken cancellationToken);
}
