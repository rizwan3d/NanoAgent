using StemCode.Application.Models;

namespace StemCode.Application.Abstractions;

public interface ILifecycleHookService
{
    Task<LifecycleHookRunResult> RunAsync(
        LifecycleHookContext context,
        CancellationToken cancellationToken);
}
