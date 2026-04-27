using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;

namespace NanoAgent.Application.Services;

internal sealed class ProviderSetupService : IProviderSetupService
{
    private readonly IConfirmationPrompt _confirmationPrompt;
    private readonly IFirstRunOnboardingService _onboardingService;
    private readonly IModelDiscoveryService _modelDiscoveryService;
    private readonly IStatusMessageWriter _statusMessageWriter;

    public ProviderSetupService(
        IFirstRunOnboardingService onboardingService,
        IModelDiscoveryService modelDiscoveryService,
        IConfirmationPrompt confirmationPrompt,
        IStatusMessageWriter statusMessageWriter)
    {
        _onboardingService = onboardingService;
        _modelDiscoveryService = modelDiscoveryService;
        _confirmationPrompt = confirmationPrompt;
        _statusMessageWriter = statusMessageWriter;
    }

    public async Task<OnboardingResult> EnsureOnboardedAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await _onboardingService.EnsureOnboardedAsync(cancellationToken);
        }
        catch (Exception exception) when (ShouldOfferOnboardingRetry(exception))
        {
            await _statusMessageWriter.ShowErrorAsync(
                $"Provider setup could not be completed: {exception.Message}",
                cancellationToken);

            bool shouldReconfigure = await _confirmationPrompt.PromptAsync(
                new ConfirmationPromptRequest(
                    "Provider setup failed. Re-run onboarding?",
                    "Choose Yes to try provider setup again, or No to stop startup.",
                    DefaultValue: true),
                cancellationToken);

            if (!shouldReconfigure)
            {
                throw;
            }

            return await _onboardingService.ReconfigureAsync(cancellationToken);
        }
    }

    public async Task<ProviderSetupResult> EnsureConfiguredAsync(
        CancellationToken cancellationToken)
    {
        OnboardingResult onboardingResult = await EnsureOnboardedAsync(cancellationToken);

        try
        {
            return new ProviderSetupResult(
                onboardingResult,
                await _modelDiscoveryService.DiscoverAndSelectAsync(cancellationToken));
        }
        catch (Exception exception) when (ShouldOfferModelValidationRetry(exception))
        {
            await _statusMessageWriter.ShowErrorAsync(
                $"Provider setup could not be validated: {exception.Message}",
                cancellationToken);

            bool shouldReconfigure = await _confirmationPrompt.PromptAsync(
                new ConfirmationPromptRequest(
                    "Provider setup failed. Re-run onboarding?",
                    "Choose Yes to reconfigure provider credentials now, or No to stop startup.",
                    DefaultValue: true),
                cancellationToken);

            if (!shouldReconfigure)
            {
                throw;
            }

            onboardingResult = await _onboardingService.ReconfigureAsync(cancellationToken);
            return new ProviderSetupResult(
                onboardingResult,
                await _modelDiscoveryService.DiscoverAndSelectAsync(cancellationToken));
        }
    }

    private static bool ShouldOfferOnboardingRetry(Exception exception)
    {
        return exception is not OperationCanceledException and not PromptCancelledException;
    }

    private static bool ShouldOfferModelValidationRetry(Exception exception)
    {
        return exception is not OperationCanceledException &&
            (exception is ModelDiscoveryException ||
                exception is HttpRequestException ||
                exception is InvalidOperationException);
    }
}
