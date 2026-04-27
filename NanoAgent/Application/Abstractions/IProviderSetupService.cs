using NanoAgent.Application.Models;

namespace NanoAgent.Application.Abstractions;

public interface IProviderSetupService
{
    Task<OnboardingResult> EnsureOnboardedAsync(CancellationToken cancellationToken);

    Task<ProviderSetupResult> EnsureConfiguredAsync(CancellationToken cancellationToken);
}
