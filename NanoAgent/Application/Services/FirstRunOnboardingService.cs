using Microsoft.Extensions.Logging;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Logging;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Abstractions;
using NanoAgent.Domain.Models;

namespace NanoAgent.Application.Services;

internal sealed class FirstRunOnboardingService : IFirstRunOnboardingService
{
    private static readonly SelectionPromptOption<OnboardingProviderChoice>[] ProviderOptions =
    [
        new(
            "OpenAI",
            OnboardingProviderChoice.OpenAi,
            "Use the official OpenAI API with only an API key."),
        new(
            "OpenAI ChatGPT Plus/Pro",
            OnboardingProviderChoice.OpenAiChatGptAccount,
            "Use browser sign-in for a ChatGPT Plus or Pro account."),
        new(
            "OpenRouter",
            OnboardingProviderChoice.OpenRouter,
            "Use OpenRouter with only an OpenRouter API key."),
        new(
            "Google AI Studio",
            OnboardingProviderChoice.GoogleAiStudio,
            "Use Gemini through Google AI Studio with only a Gemini API key."),
        new(
            "Anthropic",
            OnboardingProviderChoice.Anthropic,
            "Use Claude through Anthropic with only an Anthropic API key."),
        new(
            "OpenAI-compatible provider",
            OnboardingProviderChoice.OpenAiCompatible,
            "Use a custom base URL and API key.")
    ];

    private readonly ISelectionPrompt _selectionPrompt;
    private readonly ITextPrompt _textPrompt;
    private readonly ISecretPrompt _secretPrompt;
    private readonly IConfirmationPrompt _confirmationPrompt;
    private readonly IStatusMessageWriter _statusMessageWriter;
    private readonly IOnboardingInputValidator _inputValidator;
    private readonly IAgentConfigurationStore _configurationStore;
    private readonly IApiKeySecretStore _secretStore;
    private readonly IAgentProviderProfileFactory _profileFactory;
    private readonly IOpenAiChatGptAccountAuthenticator? _openAiChatGptAccountAuthenticator;
    private readonly ILogger<FirstRunOnboardingService> _logger;

    public FirstRunOnboardingService(
        ISelectionPrompt selectionPrompt,
        ITextPrompt textPrompt,
        ISecretPrompt secretPrompt,
        IConfirmationPrompt confirmationPrompt,
        IStatusMessageWriter statusMessageWriter,
        IOnboardingInputValidator inputValidator,
        IAgentConfigurationStore configurationStore,
        IApiKeySecretStore secretStore,
        IAgentProviderProfileFactory profileFactory,
        ILogger<FirstRunOnboardingService> logger,
        IOpenAiChatGptAccountAuthenticator? openAiChatGptAccountAuthenticator = null)
    {
        _selectionPrompt = selectionPrompt;
        _textPrompt = textPrompt;
        _secretPrompt = secretPrompt;
        _confirmationPrompt = confirmationPrompt;
        _statusMessageWriter = statusMessageWriter;
        _inputValidator = inputValidator;
        _configurationStore = configurationStore;
        _secretStore = secretStore;
        _profileFactory = profileFactory;
        _openAiChatGptAccountAuthenticator = openAiChatGptAccountAuthenticator;
        _logger = logger;
    }

    public async Task<OnboardingResult> EnsureOnboardedAsync(CancellationToken cancellationToken)
    {
        AgentConfiguration? existingConfiguration = await _configurationStore.LoadAsync(cancellationToken);
        AgentProviderProfile? existingProfile = existingConfiguration?.ProviderProfile;
        string? existingApiKey = await _secretStore.LoadAsync(cancellationToken);

        if (existingProfile is not null && !string.IsNullOrWhiteSpace(existingApiKey))
        {
            ApplicationLogMessages.ExistingOnboardingDetected(
                _logger,
                existingProfile.ProviderKind.ToDisplayName());

            await _statusMessageWriter.ShowInfoAsync(
                $"Using existing provider configuration: {existingProfile.ProviderKind.ToDisplayName()}.",
                cancellationToken);

            return new OnboardingResult(
                existingProfile,
                WasOnboardedDuringCurrentRun: false,
                ReasoningEffortOptions.NormalizeOrNull(existingConfiguration?.ReasoningEffort));
        }

        if (existingProfile is not null || !string.IsNullOrWhiteSpace(existingApiKey))
        {
            ApplicationLogMessages.IncompleteOnboardingDetected(_logger);

            await _statusMessageWriter.ShowErrorAsync(
                "Found incomplete local provider settings. NanoAgent needs to reconfigure them before continuing.",
                cancellationToken);

            bool shouldContinue = await _confirmationPrompt.PromptAsync(
                new ConfirmationPromptRequest(
                    "Continue and overwrite the incomplete local setup?",
                    "Choose Yes to enter a fresh provider configuration, or No to cancel startup."),
                cancellationToken);

            if (!shouldContinue)
            {
                throw new PromptCancelledException("The onboarding flow was cancelled before reconfiguration.");
            }
        }

        return await RunOnboardingAsync(
            "Welcome to NanoAgent. Let's configure your provider for first run.",
            cancellationToken);
    }

    public Task<OnboardingResult> ReconfigureAsync(CancellationToken cancellationToken)
    {
        return RunOnboardingAsync(
            "Let's reconfigure NanoAgent provider setup.",
            cancellationToken);
    }

    private async Task<OnboardingResult> RunOnboardingAsync(
        string introMessage,
        CancellationToken cancellationToken)
    {
        await _statusMessageWriter.ShowInfoAsync(
            introMessage,
            cancellationToken);

        OnboardingProviderChoice providerChoice = await _selectionPrompt.PromptAsync(
            new SelectionPromptRequest<OnboardingProviderChoice>(
                "Choose the provider you want to use",
                ProviderOptions,
                "Pick how NanoAgent should connect to your model provider on this machine."),
            cancellationToken);

        AgentProviderProfile profile = providerChoice switch
        {
            OnboardingProviderChoice.OpenAi => _profileFactory.CreateOpenAi(),
            OnboardingProviderChoice.OpenAiChatGptAccount => _profileFactory.CreateOpenAiChatGptAccount(),
            OnboardingProviderChoice.OpenRouter => _profileFactory.CreateOpenRouter(),
            OnboardingProviderChoice.GoogleAiStudio => _profileFactory.CreateGoogleAiStudio(),
            OnboardingProviderChoice.Anthropic => _profileFactory.CreateAnthropic(),
            OnboardingProviderChoice.OpenAiCompatible => _profileFactory.CreateCompatible(
                await PromptUntilValidAsync(
                    promptCancellationToken => _textPrompt.PromptAsync(
                        new TextPromptRequest(
                            "Base URL",
                            "Enter the OpenAI-compatible base URL, for example https://api.example.com/v1."),
                        promptCancellationToken),
                    _inputValidator.ValidateBaseUrl,
                    cancellationToken)),
            _ => throw new InvalidOperationException($"Unsupported provider choice '{providerChoice}'.")
        };

        string providerSecret = providerChoice == OnboardingProviderChoice.OpenAiChatGptAccount
            ? await AuthenticateOpenAiChatGptAccountAsync(cancellationToken)
            : await PromptUntilValidAsync(
                cancellationToken => _secretPrompt.PromptAsync(
                    new SecretPromptRequest(
                        "API key",
                        "Paste the API key for the selected provider."),
                    cancellationToken),
                _inputValidator.ValidateApiKey,
                cancellationToken);

        await _configurationStore.SaveAsync(
            new AgentConfiguration(profile, PreferredModelId: null),
            cancellationToken);
        await _secretStore.SaveAsync(providerSecret, cancellationToken);

        await _statusMessageWriter.ShowSuccessAsync(
            $"Onboarding complete. Provider: {profile.ProviderKind.ToDisplayName()}.",
            cancellationToken);

        ApplicationLogMessages.OnboardingCompleted(_logger, profile.ProviderKind.ToDisplayName());

        return new OnboardingResult(profile, WasOnboardedDuringCurrentRun: true);
    }

    private async Task<string> AuthenticateOpenAiChatGptAccountAsync(CancellationToken cancellationToken)
    {
        if (_openAiChatGptAccountAuthenticator is null)
        {
            throw new InvalidOperationException(
                "OpenAI ChatGPT Plus/Pro authentication is unavailable in this runtime.");
        }

        return await _openAiChatGptAccountAuthenticator.AuthenticateAsync(cancellationToken);
    }

    private async Task<string> PromptUntilValidAsync(
        Func<CancellationToken, Task<string>> promptValue,
        Func<string?, InputValidationResult> validate,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string rawValue = await promptValue(cancellationToken);

            InputValidationResult validationResult = validate(rawValue);
            if (validationResult.IsValid)
            {
                return validationResult.NormalizedValue!;
            }

            await _statusMessageWriter.ShowErrorAsync(validationResult.ErrorMessage!, cancellationToken);
        }
    }
}
