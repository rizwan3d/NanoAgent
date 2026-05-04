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
    private static readonly SelectionPromptOption<OnboardingProviderSetupChoice>[] ProviderSetupOptions =
    [
        new(
            "Subscription accounts",
            OnboardingProviderSetupChoice.SubscriptionAccount,
            "Sign in with an existing ChatGPT, Claude, or GitHub Copilot subscription."),
        new(
            "API key providers",
            OnboardingProviderSetupChoice.ApiKey,
            "Use a hosted provider with only an API key."),
        new(
            "OpenAI-compatible provider",
            OnboardingProviderSetupChoice.OpenAiCompatible,
            "Use a custom base URL and API key.")
    ];

    private static readonly SelectionPromptOption<OnboardingProviderChoice>[] SubscriptionProviderOptions =
    [
        new(
            "OpenAI ChatGPT Plus/Pro",
            OnboardingProviderChoice.OpenAiChatGptAccount,
            "Use browser sign-in for a ChatGPT Plus or Pro account."),
        new(
            "Anthropic Claude Pro/Max",
            OnboardingProviderChoice.AnthropicClaudeAccount,
            "Use browser sign-in for a Claude Pro or Max account."),
        new(
            "GitHub Copilot",
            OnboardingProviderChoice.GitHubCopilot,
            "Use browser device sign-in for GitHub Copilot, including GitHub Enterprise.")
    ];

    private static readonly SelectionPromptOption<OnboardingProviderChoice>[] ApiKeyProviderOptions =
    [
        new(
            "OpenAI",
            OnboardingProviderChoice.OpenAi,
            "Use the official OpenAI API with only an API key."),
        new(
            "Anthropic",
            OnboardingProviderChoice.Anthropic,
            "Use Claude through Anthropic with only an Anthropic API key."),
        new(
            "Google AI Studio",
            OnboardingProviderChoice.GoogleAiStudio,
            "Use Gemini through Google AI Studio with only a Gemini API key."),
        new(
            "OpenRouter",
            OnboardingProviderChoice.OpenRouter,
            "Use OpenRouter with only an OpenRouter API key.")
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
    private readonly IAnthropicClaudeAccountAuthenticator? _anthropicClaudeAccountAuthenticator;
    private readonly IGitHubCopilotAuthenticator? _gitHubCopilotAuthenticator;
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
        IOpenAiChatGptAccountAuthenticator? openAiChatGptAccountAuthenticator = null,
        IAnthropicClaudeAccountAuthenticator? anthropicClaudeAccountAuthenticator = null,
        IGitHubCopilotAuthenticator? gitHubCopilotAuthenticator = null)
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
        _anthropicClaudeAccountAuthenticator = anthropicClaudeAccountAuthenticator;
        _gitHubCopilotAuthenticator = gitHubCopilotAuthenticator;
        _logger = logger;
    }

    public async Task<OnboardingResult> EnsureOnboardedAsync(CancellationToken cancellationToken)
    {
        AgentConfiguration? existingConfiguration = await _configurationStore.LoadAsync(cancellationToken);
        AgentProviderProfile? existingProfile = existingConfiguration?.ProviderProfile;
        string? existingApiKey = await LoadProviderSecretAsync(existingConfiguration, cancellationToken);

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
                ReasoningEffortOptions.NormalizeOrNull(existingConfiguration?.ReasoningEffort),
                existingConfiguration?.ActiveProviderName);
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

        OnboardingProviderChoice providerChoice = await PromptForProviderChoiceAsync(cancellationToken);

        AgentProviderProfile profile = providerChoice switch
        {
            OnboardingProviderChoice.OpenAi => _profileFactory.CreateOpenAi(),
            OnboardingProviderChoice.OpenAiChatGptAccount => _profileFactory.CreateOpenAiChatGptAccount(),
            OnboardingProviderChoice.AnthropicClaudeAccount => _profileFactory.CreateAnthropicClaudeAccount(),
            OnboardingProviderChoice.GitHubCopilot => _profileFactory.CreateGitHubCopilot(),
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

        string providerSecret = providerChoice switch
        {
            OnboardingProviderChoice.OpenAiChatGptAccount =>
                await AuthenticateOpenAiChatGptAccountAsync(cancellationToken),
            OnboardingProviderChoice.AnthropicClaudeAccount =>
                await AuthenticateAnthropicClaudeAccountAsync(cancellationToken),
            OnboardingProviderChoice.GitHubCopilot =>
                await AuthenticateGitHubCopilotAsync(cancellationToken),
            _ => await PromptUntilValidAsync(
                    cancellationToken => _secretPrompt.PromptAsync(
                        new SecretPromptRequest(
                            "API key",
                            "Paste the API key for the selected provider."),
                        cancellationToken),
                    _inputValidator.ValidateApiKey,
                    cancellationToken)
        };

        string providerName = await CreateProviderNameAsync(profile, cancellationToken);
        await _secretStore.SaveAsync(providerName, providerSecret, cancellationToken);
        await _configurationStore.SaveAsync(
            new AgentConfiguration(
                profile,
                PreferredModelId: null,
                ActiveProviderName: providerName),
            cancellationToken);
        await _secretStore.SaveAsync(providerSecret, cancellationToken);

        await _statusMessageWriter.ShowSuccessAsync(
            $"Onboarding complete. Provider: {profile.ProviderKind.ToDisplayName()}.",
            cancellationToken);

        ApplicationLogMessages.OnboardingCompleted(_logger, profile.ProviderKind.ToDisplayName());

        return new OnboardingResult(
            profile,
            WasOnboardedDuringCurrentRun: true,
            ActiveProviderName: providerName);
    }

    private async Task<string?> LoadProviderSecretAsync(
        AgentConfiguration? configuration,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(configuration?.ActiveProviderName))
        {
            string? providerSecret = await _secretStore.LoadAsync(
                configuration.ActiveProviderName,
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(providerSecret))
            {
                return providerSecret;
            }
        }

        return await _secretStore.LoadAsync(cancellationToken);
    }

    private async Task<string> CreateProviderNameAsync(
        AgentProviderProfile profile,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        return CreateProviderName(profile);
    }

    private static string CreateProviderName(AgentProviderProfile profile)
    {
        string providerName = profile.ProviderKind.ToDisplayName();
        if (profile.ProviderKind == ProviderKind.OpenAiCompatible &&
            Uri.TryCreate(profile.ResolveBaseUrl(), UriKind.Absolute, out Uri? uri) &&
            !string.IsNullOrWhiteSpace(uri.Host))
        {
            return $"{providerName} ({uri.Host})";
        }

        return providerName;
    }

    private async Task<OnboardingProviderChoice> PromptForProviderChoiceAsync(
        CancellationToken cancellationToken)
    {
        while (true)
        {
            OnboardingProviderSetupChoice setupChoice = await _selectionPrompt.PromptAsync(
                new SelectionPromptRequest<OnboardingProviderSetupChoice>(
                    "Choose provider setup type",
                    ProviderSetupOptions,
                    "Pick the kind of provider setup you want to configure on this machine."),
                cancellationToken);

            if (setupChoice == OnboardingProviderSetupChoice.OpenAiCompatible)
            {
                return OnboardingProviderChoice.OpenAiCompatible;
            }

            try
            {
                return await _selectionPrompt.PromptAsync(
                    CreateProviderSubmenuRequest(setupChoice),
                    cancellationToken);
            }
            catch (PromptCancelledException)
            {
            }
        }
    }

    private static SelectionPromptRequest<OnboardingProviderChoice> CreateProviderSubmenuRequest(
        OnboardingProviderSetupChoice setupChoice)
    {
        return setupChoice switch
        {
            OnboardingProviderSetupChoice.SubscriptionAccount =>
                new SelectionPromptRequest<OnboardingProviderChoice>(
                    "Choose subscription provider",
                    SubscriptionProviderOptions,
                    "Esc returns to provider setup type."),
            OnboardingProviderSetupChoice.ApiKey =>
                new SelectionPromptRequest<OnboardingProviderChoice>(
                    "Choose API key provider",
                    ApiKeyProviderOptions,
                    "Esc returns to provider setup type."),
            _ => throw new InvalidOperationException($"Unsupported provider setup choice '{setupChoice}'.")
        };
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

    private async Task<string> AuthenticateAnthropicClaudeAccountAsync(CancellationToken cancellationToken)
    {
        if (_anthropicClaudeAccountAuthenticator is null)
        {
            throw new InvalidOperationException(
                "Anthropic Claude Pro/Max authentication is unavailable in this runtime.");
        }

        return await _anthropicClaudeAccountAuthenticator.AuthenticateAsync(cancellationToken);
    }

    private async Task<string> AuthenticateGitHubCopilotAsync(CancellationToken cancellationToken)
    {
        if (_gitHubCopilotAuthenticator is null)
        {
            throw new InvalidOperationException(
                "GitHub Copilot authentication is unavailable in this runtime.");
        }

        return await _gitHubCopilotAuthenticator.AuthenticateAsync(cancellationToken);
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
