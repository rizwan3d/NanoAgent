using StemCode.Application.Models;

namespace StemCode.Application.Abstractions;

public interface IProviderSetupService
{
    Task<OnboardingResult> EnsureOnboardedAsync(CancellationToken cancellationToken);

    Task<ProviderSetupResult> EnsureConfiguredAsync(CancellationToken cancellationToken);
}
