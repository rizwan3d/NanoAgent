using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;

namespace NanoAgent.Application.Services;

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
