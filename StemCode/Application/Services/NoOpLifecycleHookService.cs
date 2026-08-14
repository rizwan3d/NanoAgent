using StemCode.Application.Abstractions;
using StemCode.Application.Models;

namespace StemCode.Application.Services;

internal sealed class NoOpLifecycleHookService : ILifecycleHookService
{
    public Task<LifecycleHookRunResult> RunAsync(
        LifecycleHookContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(LifecycleHookRunResult.Allowed());
    }
}
