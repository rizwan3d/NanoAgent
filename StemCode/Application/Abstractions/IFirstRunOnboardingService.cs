using StemCode.Application.Models;

namespace StemCode.Application.Abstractions;

public interface IFirstRunOnboardingService
{
    Task<OnboardingResult> EnsureOnboardedAsync(CancellationToken cancellationToken);

    Task<OnboardingResult> ReconfigureAsync(CancellationToken cancellationToken);
}
