using NanoAgent.Application.Models;

namespace NanoAgent.Application.Abstractions;

public interface IFirstRunOnboardingService
{
    Task<OnboardingResult> EnsureOnboardedAsync(CancellationToken cancellationToken);

    Task<OnboardingResult> ReconfigureAsync(CancellationToken cancellationToken);
}
