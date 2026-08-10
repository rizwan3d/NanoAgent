namespace StemCode.Application.Models;

public sealed record ProviderSetupResult(
    OnboardingResult OnboardingResult,
    ModelDiscoveryResult ModelDiscoveryResult);
