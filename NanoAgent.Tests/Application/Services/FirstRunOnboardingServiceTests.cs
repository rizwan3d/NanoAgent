using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Services;
using NanoAgent.Domain.Abstractions;
using NanoAgent.Domain.Models;

namespace NanoAgent.Tests.Application.Services;

public sealed class FirstRunOnboardingServiceTests
{
    [Fact]
    public async Task EnsureOnboardedAsync_Should_SkipPrompts_When_ConfigurationAndSecretAlreadyExist()
    {
        AgentProviderProfile existingProfile = new(ProviderKind.OpenAi, null);

        Mock<ISelectionPrompt> selectionPrompt = new(MockBehavior.Strict);
        Mock<ITextPrompt> textPrompt = new(MockBehavior.Strict);
        Mock<ISecretPrompt> secretPrompt = new(MockBehavior.Strict);
        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);
        Mock<IStatusMessageWriter> statusMessageWriter = new(MockBehavior.Strict);
        statusMessageWriter
            .Setup(writer => writer.ShowInfoAsync(
                "Using existing provider configuration: OpenAI.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IOnboardingInputValidator> inputValidator = new(MockBehavior.Strict);
        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentConfiguration(existingProfile, null));
        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync("existing-key");
        Mock<IAgentProviderProfileFactory> profileFactory = new(MockBehavior.Strict);

        FirstRunOnboardingService sut = CreateSut(
            selectionPrompt.Object,
            textPrompt.Object,
            secretPrompt.Object,
            confirmationPrompt.Object,
            statusMessageWriter.Object,
            inputValidator.Object,
            configurationStore.Object,
            secretStore.Object,
            profileFactory.Object);

        OnboardingResult result = await sut.EnsureOnboardedAsync(CancellationToken.None);

        result.Should().Be(new OnboardingResult(existingProfile, false));
        selectionPrompt.VerifyNoOtherCalls();
        textPrompt.VerifyNoOtherCalls();
        secretPrompt.VerifyNoOtherCalls();
        confirmationPrompt.VerifyNoOtherCalls();
        statusMessageWriter.VerifyAll();
        configurationStore.Verify(store => store.SaveAsync(It.IsAny<AgentConfiguration>(), It.IsAny<CancellationToken>()), Times.Never);
        secretStore.Verify(store => store.SaveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnsureOnboardedAsync_Should_UseProviderScopedSecret_When_ActiveProviderNameExists()
    {
        AgentProviderProfile existingProfile = new(ProviderKind.OpenAi, null);

        Mock<ISelectionPrompt> selectionPrompt = new(MockBehavior.Strict);
        Mock<ITextPrompt> textPrompt = new(MockBehavior.Strict);
        Mock<ISecretPrompt> secretPrompt = new(MockBehavior.Strict);
        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);
        Mock<IStatusMessageWriter> statusMessageWriter = new(MockBehavior.Strict);
        statusMessageWriter
            .Setup(writer => writer.ShowInfoAsync(
                "Using existing provider configuration: OpenAI.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IOnboardingInputValidator> inputValidator = new(MockBehavior.Strict);
        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentConfiguration(
                existingProfile,
                null,
                ReasoningEffort: null,
                ActiveProviderName: "OpenAI",
                ThinkingMode: "on"));
        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore.Setup(store => store.LoadAsync("OpenAI", It.IsAny<CancellationToken>()))
            .ReturnsAsync("existing-key");
        Mock<IAgentProviderProfileFactory> profileFactory = new(MockBehavior.Strict);

        FirstRunOnboardingService sut = CreateSut(
            selectionPrompt.Object,
            textPrompt.Object,
            secretPrompt.Object,
            confirmationPrompt.Object,
            statusMessageWriter.Object,
            inputValidator.Object,
            configurationStore.Object,
            secretStore.Object,
            profileFactory.Object);

        OnboardingResult result = await sut.EnsureOnboardedAsync(CancellationToken.None);

        result.Should().Be(new OnboardingResult(
            existingProfile,
            false,
            ReasoningEffort: null,
            ActiveProviderName: "OpenAI",
            ThinkingMode: "on"));
        secretStore.Verify(store => store.LoadAsync(It.IsAny<CancellationToken>()), Times.Never);
        selectionPrompt.VerifyNoOtherCalls();
        textPrompt.VerifyNoOtherCalls();
        secretPrompt.VerifyNoOtherCalls();
        confirmationPrompt.VerifyNoOtherCalls();
        configurationStore.Verify(store => store.SaveAsync(It.IsAny<AgentConfiguration>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnsureOnboardedAsync_Should_SaveOpenAiConfiguration_When_OpenAiIsSelected()
    {
        AgentProviderProfile openAiProfile = new(ProviderKind.OpenAi, null);

        Mock<ISelectionPrompt> selectionPrompt = new(MockBehavior.Strict);
        SetupProviderSelection(selectionPrompt, OnboardingProviderChoice.OpenAi);

        Mock<ITextPrompt> textPrompt = new(MockBehavior.Strict);

        Mock<ISecretPrompt> secretPrompt = new(MockBehavior.Strict);
        secretPrompt
            .Setup(prompt => prompt.PromptAsync(It.IsAny<SecretPromptRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("  sk-openai  ");

        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);

        Mock<IStatusMessageWriter> statusMessageWriter = new(MockBehavior.Strict);
        statusMessageWriter
            .Setup(writer => writer.ShowInfoAsync(
                "Welcome to NanoAgent. Let's configure your provider for first run.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        statusMessageWriter
            .Setup(writer => writer.ShowSuccessAsync(
                "Onboarding complete. Provider: OpenAI.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IOnboardingInputValidator> inputValidator = new(MockBehavior.Strict);
        inputValidator
            .Setup(validator => validator.ValidateApiKey("  sk-openai  "))
            .Returns(InputValidationResult.Success("sk-openai"));

        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((AgentConfiguration?)null);
        configurationStore
            .Setup(store => store.SaveAsync(
                new AgentConfiguration(openAiProfile, null, null, "OpenAI"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        secretStore.Setup(store => store.SaveAsync("sk-openai", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        secretStore.Setup(store => store.SaveAsync(It.IsAny<string?>(), "sk-openai", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        Mock<IAgentProviderProfileFactory> profileFactory = new(MockBehavior.Strict);
        profileFactory.Setup(factory => factory.CreateOpenAi()).Returns(openAiProfile);

        FirstRunOnboardingService sut = CreateSut(
            selectionPrompt.Object,
            textPrompt.Object,
            secretPrompt.Object,
            confirmationPrompt.Object,
            statusMessageWriter.Object,
            inputValidator.Object,
            configurationStore.Object,
            secretStore.Object,
            profileFactory.Object);

        OnboardingResult result = await sut.EnsureOnboardedAsync(CancellationToken.None);

        result.Should().Be(new OnboardingResult(
            openAiProfile,
            true,
            ActiveProviderName: "OpenAI"));
        profileFactory.Verify(factory => factory.CreateOpenAi(), Times.Once);
        textPrompt.VerifyNoOtherCalls();
        configurationStore.VerifyAll();
        secretStore.VerifyAll();
        statusMessageWriter.VerifyAll();
    }

    [Fact]
    public async Task EnsureOnboardedAsync_Should_SaveNanoAgentEnterpriseConfiguration_When_Selected()
    {
        AgentProviderProfile enterpriseProfile = new(ProviderKind.OpenAiCompatible, "https://localhost:7180/v1");

        Mock<ISelectionPrompt> selectionPrompt = new(MockBehavior.Strict);
        SetupProviderSelection(selectionPrompt, OnboardingProviderChoice.NanoAgentEnterprise);

        Mock<ITextPrompt> textPrompt = new(MockBehavior.Strict);
        Mock<ISecretPrompt> secretPrompt = new(MockBehavior.Strict);
        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);

        Mock<IStatusMessageWriter> statusMessageWriter = new(MockBehavior.Strict);
        statusMessageWriter
            .Setup(writer => writer.ShowInfoAsync(
                "Welcome to NanoAgent. Let's configure your provider for first run.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        statusMessageWriter
            .Setup(writer => writer.ShowSuccessAsync(
                "Onboarding complete. Provider: NanoAgent Enterprise.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IOnboardingInputValidator> inputValidator = new(MockBehavior.Strict);

        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((AgentConfiguration?)null);
        configurationStore
            .Setup(store => store.SaveAsync(
                new AgentConfiguration(enterpriseProfile, null, null, "NanoAgent Enterprise"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        secretStore.Setup(store => store.SaveAsync("enterprise-credential-json", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        secretStore
            .Setup(store => store.SaveAsync("NanoAgent Enterprise", "enterprise-credential-json", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IAgentProviderProfileFactory> profileFactory = new(MockBehavior.Strict);
        profileFactory
            .Setup(factory => factory.CreateCompatible("https://localhost:7180/v1"))
            .Returns(enterpriseProfile);

        Mock<INanoAgentEnterpriseAuthenticator> authenticator = new(MockBehavior.Strict);
        authenticator
            .Setup(service => service.AuthenticateAsync("https://localhost:7180/v1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("enterprise-credential-json");

        FirstRunOnboardingService sut = CreateSut(
            selectionPrompt.Object,
            textPrompt.Object,
            secretPrompt.Object,
            confirmationPrompt.Object,
            statusMessageWriter.Object,
            inputValidator.Object,
            configurationStore.Object,
            secretStore.Object,
            profileFactory.Object,
            nanoAgentEnterpriseAuthenticator: authenticator.Object);

        OnboardingResult result = await sut.EnsureOnboardedAsync(CancellationToken.None);

        result.Should().Be(new OnboardingResult(
            enterpriseProfile,
            true,
            ActiveProviderName: "NanoAgent Enterprise"));
        profileFactory.Verify(factory => factory.CreateCompatible("https://localhost:7180/v1"), Times.Once);
        authenticator.Verify(service => service.AuthenticateAsync("https://localhost:7180/v1", It.IsAny<CancellationToken>()), Times.Once);
        configurationStore.VerifyAll();
        secretStore.VerifyAll();
        statusMessageWriter.VerifyAll();
    }

    [Fact]
    public async Task EnsureOnboardedAsync_Should_UseProviderSetupSubmenus()
    {
        AgentProviderProfile openAiProfile = new(ProviderKind.OpenAi, null);

        Mock<ISelectionPrompt> selectionPrompt = new(MockBehavior.Strict);
        selectionPrompt
            .Setup(prompt => prompt.PromptAsync(
                It.IsAny<SelectionPromptRequest<OnboardingProviderSetupChoice>>(),
                It.IsAny<CancellationToken>()))
            .Returns<SelectionPromptRequest<OnboardingProviderSetupChoice>, CancellationToken>((request, _) =>
            {
                request.Title.Should().Be("Choose provider setup type");
                request.Options.Select(option => (option.Label, option.Value))
                    .Should()
                    .Equal(
                        ("NanoAgent Enterprise", OnboardingProviderSetupChoice.NanoAgentEnterprise),
                        ("Subscription accounts", OnboardingProviderSetupChoice.SubscriptionAccount),
                        ("API key providers", OnboardingProviderSetupChoice.ApiKey),
                        ("OpenAI-compatible provider", OnboardingProviderSetupChoice.OpenAiCompatible),
                        ("Local providers", OnboardingProviderSetupChoice.LocalProvider));

                return Task.FromResult(OnboardingProviderSetupChoice.ApiKey);
            });

        selectionPrompt
            .Setup(prompt => prompt.PromptAsync(
                It.IsAny<SelectionPromptRequest<OnboardingProviderChoice>>(),
                It.IsAny<CancellationToken>()))
            .Returns<SelectionPromptRequest<OnboardingProviderChoice>, CancellationToken>((request, _) =>
            {
                request.Title.Should().Be("Choose API key provider");
                request.Description.Should().Be("Esc returns to provider setup type.");
                request.Options.Select(option => option.Label)
                    .Should()
                    .Equal(
                        "OpenAI",
                        "Anthropic",
                        "Google AI Studio",
                        "OpenRouter",
                        "Kilo Code",
                        "Cerebras",
                        "Groq",
                        "DeepSeek",
                        "OpenCode Zen",
                        "Ollama Cloud");

                return Task.FromResult(OnboardingProviderChoice.OpenAi);
            });

        Mock<ITextPrompt> textPrompt = new(MockBehavior.Strict);

        Mock<ISecretPrompt> secretPrompt = new(MockBehavior.Strict);
        secretPrompt
            .Setup(prompt => prompt.PromptAsync(It.IsAny<SecretPromptRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("  sk-openai  ");

        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);

        Mock<IStatusMessageWriter> statusMessageWriter = new(MockBehavior.Strict);
        statusMessageWriter
            .Setup(writer => writer.ShowInfoAsync(
                "Welcome to NanoAgent. Let's configure your provider for first run.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        statusMessageWriter
            .Setup(writer => writer.ShowSuccessAsync(
                "Onboarding complete. Provider: OpenAI.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IOnboardingInputValidator> inputValidator = new(MockBehavior.Strict);
        inputValidator
            .Setup(validator => validator.ValidateApiKey("  sk-openai  "))
            .Returns(InputValidationResult.Success("sk-openai"));

        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((AgentConfiguration?)null);
        configurationStore
            .Setup(store => store.SaveAsync(
                new AgentConfiguration(openAiProfile, null, null, "OpenAI"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        secretStore.Setup(store => store.SaveAsync("sk-openai", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        secretStore.Setup(store => store.SaveAsync(It.IsAny<string?>(), "sk-openai", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        Mock<IAgentProviderProfileFactory> profileFactory = new(MockBehavior.Strict);
        profileFactory.Setup(factory => factory.CreateOpenAi()).Returns(openAiProfile);

        FirstRunOnboardingService sut = CreateSut(
            selectionPrompt.Object,
            textPrompt.Object,
            secretPrompt.Object,
            confirmationPrompt.Object,
            statusMessageWriter.Object,
            inputValidator.Object,
            configurationStore.Object,
            secretStore.Object,
            profileFactory.Object);

        OnboardingResult result = await sut.EnsureOnboardedAsync(CancellationToken.None);

        result.Should().Be(new OnboardingResult(
            openAiProfile,
            true,
            ActiveProviderName: "OpenAI"));
    }

    [Fact]
    public async Task EnsureOnboardedAsync_Should_SaveOpenAiChatGptAccountConfiguration_When_AccountProviderIsSelected()
    {
        AgentProviderProfile profile = new(ProviderKind.OpenAiChatGptAccount, null);

        Mock<ISelectionPrompt> selectionPrompt = new(MockBehavior.Strict);
        SetupProviderSelection(selectionPrompt, OnboardingProviderChoice.OpenAiChatGptAccount);

        Mock<ITextPrompt> textPrompt = new(MockBehavior.Strict);
        Mock<ISecretPrompt> secretPrompt = new(MockBehavior.Strict);
        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);

        Mock<IStatusMessageWriter> statusMessageWriter = new(MockBehavior.Strict);
        statusMessageWriter
            .Setup(writer => writer.ShowInfoAsync(
                "Welcome to NanoAgent. Let's configure your provider for first run.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        statusMessageWriter
            .Setup(writer => writer.ShowSuccessAsync(
                "Onboarding complete. Provider: OpenAI ChatGPT Plus/Pro.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IOnboardingInputValidator> inputValidator = new(MockBehavior.Strict);

        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((AgentConfiguration?)null);
        configurationStore
            .Setup(store => store.SaveAsync(
                new AgentConfiguration(profile, null, null, "OpenAI ChatGPT Plus/Pro"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        secretStore.Setup(store => store.SaveAsync("credential-json", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        secretStore.Setup(store => store.SaveAsync(It.IsAny<string?>(), "credential-json", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        Mock<IAgentProviderProfileFactory> profileFactory = new(MockBehavior.Strict);
        profileFactory.Setup(factory => factory.CreateOpenAiChatGptAccount()).Returns(profile);

        Mock<IOpenAiChatGptAccountAuthenticator> authenticator = new(MockBehavior.Strict);
        authenticator
            .Setup(service => service.AuthenticateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("credential-json");

        FirstRunOnboardingService sut = CreateSut(
            selectionPrompt.Object,
            textPrompt.Object,
            secretPrompt.Object,
            confirmationPrompt.Object,
            statusMessageWriter.Object,
            inputValidator.Object,
            configurationStore.Object,
            secretStore.Object,
            profileFactory.Object,
            authenticator.Object);

        OnboardingResult result = await sut.EnsureOnboardedAsync(CancellationToken.None);

        result.Should().Be(new OnboardingResult(
            profile,
            true,
            ActiveProviderName: "OpenAI ChatGPT Plus/Pro"));
        profileFactory.Verify(factory => factory.CreateOpenAiChatGptAccount(), Times.Once);
        authenticator.Verify(service => service.AuthenticateAsync(It.IsAny<CancellationToken>()), Times.Once);
        textPrompt.VerifyNoOtherCalls();
        secretPrompt.VerifyNoOtherCalls();
        inputValidator.VerifyNoOtherCalls();
        configurationStore.VerifyAll();
        secretStore.VerifyAll();
        statusMessageWriter.VerifyAll();
    }

    [Fact]
    public async Task EnsureOnboardedAsync_Should_SaveAnthropicClaudeAccountConfiguration_When_AccountProviderIsSelected()
    {
        AgentProviderProfile profile = new(ProviderKind.AnthropicClaudeAccount, null);

        Mock<ISelectionPrompt> selectionPrompt = new(MockBehavior.Strict);
        SetupProviderSelection(selectionPrompt, OnboardingProviderChoice.AnthropicClaudeAccount);

        Mock<ITextPrompt> textPrompt = new(MockBehavior.Strict);
        Mock<ISecretPrompt> secretPrompt = new(MockBehavior.Strict);
        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);

        Mock<IStatusMessageWriter> statusMessageWriter = new(MockBehavior.Strict);
        statusMessageWriter
            .Setup(writer => writer.ShowInfoAsync(
                "Welcome to NanoAgent. Let's configure your provider for first run.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        statusMessageWriter
            .Setup(writer => writer.ShowSuccessAsync(
                "Onboarding complete. Provider: Anthropic Claude Pro/Max.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IOnboardingInputValidator> inputValidator = new(MockBehavior.Strict);

        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((AgentConfiguration?)null);
        configurationStore
            .Setup(store => store.SaveAsync(
                new AgentConfiguration(profile, null, null, "Anthropic Claude Pro/Max"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        secretStore.Setup(store => store.SaveAsync("anthropic-credential-json", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        secretStore.Setup(store => store.SaveAsync(It.IsAny<string?>(), "anthropic-credential-json", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        Mock<IAgentProviderProfileFactory> profileFactory = new(MockBehavior.Strict);
        profileFactory.Setup(factory => factory.CreateAnthropicClaudeAccount()).Returns(profile);

        Mock<IAnthropicClaudeAccountAuthenticator> authenticator = new(MockBehavior.Strict);
        authenticator
            .Setup(service => service.AuthenticateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("anthropic-credential-json");

        FirstRunOnboardingService sut = CreateSut(
            selectionPrompt.Object,
            textPrompt.Object,
            secretPrompt.Object,
            confirmationPrompt.Object,
            statusMessageWriter.Object,
            inputValidator.Object,
            configurationStore.Object,
            secretStore.Object,
            profileFactory.Object,
            anthropicClaudeAccountAuthenticator: authenticator.Object);

        OnboardingResult result = await sut.EnsureOnboardedAsync(CancellationToken.None);

        result.Should().Be(new OnboardingResult(
            profile,
            true,
            ActiveProviderName: "Anthropic Claude Pro/Max"));
        profileFactory.Verify(factory => factory.CreateAnthropicClaudeAccount(), Times.Once);
        authenticator.Verify(service => service.AuthenticateAsync(It.IsAny<CancellationToken>()), Times.Once);
        textPrompt.VerifyNoOtherCalls();
        secretPrompt.VerifyNoOtherCalls();
        inputValidator.VerifyNoOtherCalls();
        configurationStore.VerifyAll();
        secretStore.VerifyAll();
        statusMessageWriter.VerifyAll();
    }

    [Fact]
    public async Task EnsureOnboardedAsync_Should_SaveGitHubCopilotConfiguration_When_CopilotProviderIsSelected()
    {
        AgentProviderProfile profile = new(ProviderKind.GitHubCopilot, null);

        Mock<ISelectionPrompt> selectionPrompt = new(MockBehavior.Strict);
        SetupProviderSelection(selectionPrompt, OnboardingProviderChoice.GitHubCopilot);

        Mock<ITextPrompt> textPrompt = new(MockBehavior.Strict);
        Mock<ISecretPrompt> secretPrompt = new(MockBehavior.Strict);
        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);

        Mock<IStatusMessageWriter> statusMessageWriter = new(MockBehavior.Strict);
        statusMessageWriter
            .Setup(writer => writer.ShowInfoAsync(
                "Welcome to NanoAgent. Let's configure your provider for first run.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        statusMessageWriter
            .Setup(writer => writer.ShowSuccessAsync(
                "Onboarding complete. Provider: GitHub Copilot.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IOnboardingInputValidator> inputValidator = new(MockBehavior.Strict);

        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((AgentConfiguration?)null);
        configurationStore
            .Setup(store => store.SaveAsync(
                new AgentConfiguration(profile, null, null, "GitHub Copilot"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        secretStore.Setup(store => store.SaveAsync("github-copilot-credential-json", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        secretStore.Setup(store => store.SaveAsync(It.IsAny<string?>(), "github-copilot-credential-json", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        Mock<IAgentProviderProfileFactory> profileFactory = new(MockBehavior.Strict);
        profileFactory.Setup(factory => factory.CreateGitHubCopilot()).Returns(profile);

        Mock<IGitHubCopilotAuthenticator> authenticator = new(MockBehavior.Strict);
        authenticator
            .Setup(service => service.AuthenticateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("github-copilot-credential-json");

        FirstRunOnboardingService sut = CreateSut(
            selectionPrompt.Object,
            textPrompt.Object,
            secretPrompt.Object,
            confirmationPrompt.Object,
            statusMessageWriter.Object,
            inputValidator.Object,
            configurationStore.Object,
            secretStore.Object,
            profileFactory.Object,
            gitHubCopilotAuthenticator: authenticator.Object);

        OnboardingResult result = await sut.EnsureOnboardedAsync(CancellationToken.None);

        result.Should().Be(new OnboardingResult(
            profile,
            true,
            ActiveProviderName: "GitHub Copilot"));
        profileFactory.Verify(factory => factory.CreateGitHubCopilot(), Times.Once);
        authenticator.Verify(service => service.AuthenticateAsync(It.IsAny<CancellationToken>()), Times.Once);
        textPrompt.VerifyNoOtherCalls();
        secretPrompt.VerifyNoOtherCalls();
        inputValidator.VerifyNoOtherCalls();
        configurationStore.VerifyAll();
        secretStore.VerifyAll();
        statusMessageWriter.VerifyAll();
    }


    [Fact]
    public async Task EnsureOnboardedAsync_Should_SaveOpenRouterConfiguration_When_OpenRouterIsSelected()
    {
        AgentProviderProfile openRouterProfile = new(ProviderKind.OpenRouter, null);

        Mock<ISelectionPrompt> selectionPrompt = new(MockBehavior.Strict);
        SetupProviderSelection(selectionPrompt, OnboardingProviderChoice.OpenRouter);

        Mock<ITextPrompt> textPrompt = new(MockBehavior.Strict);

        Mock<ISecretPrompt> secretPrompt = new(MockBehavior.Strict);
        secretPrompt
            .Setup(prompt => prompt.PromptAsync(It.IsAny<SecretPromptRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("  sk-or-v1-key  ");

        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);

        Mock<IStatusMessageWriter> statusMessageWriter = new(MockBehavior.Strict);
        statusMessageWriter
            .Setup(writer => writer.ShowInfoAsync(
                "Welcome to NanoAgent. Let's configure your provider for first run.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        statusMessageWriter
            .Setup(writer => writer.ShowSuccessAsync(
                "Onboarding complete. Provider: OpenRouter.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IOnboardingInputValidator> inputValidator = new(MockBehavior.Strict);
        inputValidator
            .Setup(validator => validator.ValidateApiKey("  sk-or-v1-key  "))
            .Returns(InputValidationResult.Success("sk-or-v1-key"));

        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((AgentConfiguration?)null);
        configurationStore
            .Setup(store => store.SaveAsync(
                new AgentConfiguration(openRouterProfile, null, null, "OpenRouter"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        secretStore.Setup(store => store.SaveAsync("sk-or-v1-key", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        secretStore.Setup(store => store.SaveAsync(It.IsAny<string?>(), "sk-or-v1-key", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        Mock<IAgentProviderProfileFactory> profileFactory = new(MockBehavior.Strict);
        profileFactory.Setup(factory => factory.CreateOpenRouter()).Returns(openRouterProfile);

        FirstRunOnboardingService sut = CreateSut(
            selectionPrompt.Object,
            textPrompt.Object,
            secretPrompt.Object,
            confirmationPrompt.Object,
            statusMessageWriter.Object,
            inputValidator.Object,
            configurationStore.Object,
            secretStore.Object,
            profileFactory.Object);

        OnboardingResult result = await sut.EnsureOnboardedAsync(CancellationToken.None);

        result.Should().Be(new OnboardingResult(
            openRouterProfile,
            true,
            ActiveProviderName: "OpenRouter"));
        profileFactory.Verify(factory => factory.CreateOpenRouter(), Times.Once);
        textPrompt.VerifyNoOtherCalls();
        configurationStore.VerifyAll();
        secretStore.VerifyAll();
        statusMessageWriter.VerifyAll();
    }

    [Fact]
    public async Task EnsureOnboardedAsync_Should_SaveKiloCodeConfiguration_When_KiloCodeIsSelected()
    {
        AgentProviderProfile kiloCodeProfile = new(ProviderKind.KiloCode, null);

        Mock<ISelectionPrompt> selectionPrompt = new(MockBehavior.Strict);
        SetupProviderSelection(selectionPrompt, OnboardingProviderChoice.KiloCode);

        Mock<ITextPrompt> textPrompt = new(MockBehavior.Strict);

        Mock<ISecretPrompt> secretPrompt = new(MockBehavior.Strict);
        secretPrompt
            .Setup(prompt => prompt.PromptAsync(It.IsAny<SecretPromptRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("  kilo-key  ");

        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);

        Mock<IStatusMessageWriter> statusMessageWriter = new(MockBehavior.Strict);
        statusMessageWriter
            .Setup(writer => writer.ShowInfoAsync(
                "Welcome to NanoAgent. Let's configure your provider for first run.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        statusMessageWriter
            .Setup(writer => writer.ShowSuccessAsync(
                "Onboarding complete. Provider: Kilo Code.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IOnboardingInputValidator> inputValidator = new(MockBehavior.Strict);
        inputValidator
            .Setup(validator => validator.ValidateApiKey("  kilo-key  "))
            .Returns(InputValidationResult.Success("kilo-key"));

        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((AgentConfiguration?)null);
        configurationStore
            .Setup(store => store.SaveAsync(
                new AgentConfiguration(kiloCodeProfile, null, null, "Kilo Code"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        secretStore.Setup(store => store.SaveAsync("kilo-key", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        secretStore.Setup(store => store.SaveAsync(It.IsAny<string?>(), "kilo-key", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        Mock<IAgentProviderProfileFactory> profileFactory = new(MockBehavior.Strict);
        profileFactory.Setup(factory => factory.CreateKiloCode()).Returns(kiloCodeProfile);

        FirstRunOnboardingService sut = CreateSut(
            selectionPrompt.Object,
            textPrompt.Object,
            secretPrompt.Object,
            confirmationPrompt.Object,
            statusMessageWriter.Object,
            inputValidator.Object,
            configurationStore.Object,
            secretStore.Object,
            profileFactory.Object);

        OnboardingResult result = await sut.EnsureOnboardedAsync(CancellationToken.None);

        result.Should().Be(new OnboardingResult(
            kiloCodeProfile,
            true,
            ActiveProviderName: "Kilo Code"));
        profileFactory.Verify(factory => factory.CreateKiloCode(), Times.Once);
        textPrompt.VerifyNoOtherCalls();
        configurationStore.VerifyAll();
        secretStore.VerifyAll();
        statusMessageWriter.VerifyAll();
    }

    [Fact]
    public async Task EnsureOnboardedAsync_Should_SaveOllamaCloudConfiguration_When_OllamaCloudIsSelected()
    {
        AgentProviderProfile profile = new(ProviderKind.OllamaCloud, null);

        Mock<ISelectionPrompt> selectionPrompt = new(MockBehavior.Strict);
        SetupProviderSelection(selectionPrompt, OnboardingProviderChoice.OllamaCloud);

        Mock<ITextPrompt> textPrompt = new(MockBehavior.Strict);

        Mock<ISecretPrompt> secretPrompt = new(MockBehavior.Strict);
        secretPrompt
            .Setup(prompt => prompt.PromptAsync(It.IsAny<SecretPromptRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("  ollama-cloud-key  ");

        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);

        Mock<IStatusMessageWriter> statusMessageWriter = new(MockBehavior.Strict);
        statusMessageWriter
            .Setup(writer => writer.ShowInfoAsync(
                "Welcome to NanoAgent. Let's configure your provider for first run.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        statusMessageWriter
            .Setup(writer => writer.ShowSuccessAsync(
                "Onboarding complete. Provider: Ollama Cloud.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IOnboardingInputValidator> inputValidator = new(MockBehavior.Strict);
        inputValidator
            .Setup(validator => validator.ValidateApiKey("  ollama-cloud-key  "))
            .Returns(InputValidationResult.Success("ollama-cloud-key"));

        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((AgentConfiguration?)null);
        configurationStore
            .Setup(store => store.SaveAsync(
                new AgentConfiguration(profile, null, null, "Ollama Cloud"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        secretStore.Setup(store => store.SaveAsync("ollama-cloud-key", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        secretStore.Setup(store => store.SaveAsync(It.IsAny<string?>(), "ollama-cloud-key", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        Mock<IAgentProviderProfileFactory> profileFactory = new(MockBehavior.Strict);
        profileFactory.Setup(factory => factory.CreateOllamaCloud()).Returns(profile);

        FirstRunOnboardingService sut = CreateSut(
            selectionPrompt.Object,
            textPrompt.Object,
            secretPrompt.Object,
            confirmationPrompt.Object,
            statusMessageWriter.Object,
            inputValidator.Object,
            configurationStore.Object,
            secretStore.Object,
            profileFactory.Object);

        OnboardingResult result = await sut.EnsureOnboardedAsync(CancellationToken.None);

        result.Should().Be(new OnboardingResult(
            profile,
            true,
            ActiveProviderName: "Ollama Cloud"));
        profileFactory.Verify(factory => factory.CreateOllamaCloud(), Times.Once);
        textPrompt.VerifyNoOtherCalls();
        configurationStore.VerifyAll();
        secretStore.VerifyAll();
        statusMessageWriter.VerifyAll();
    }

    [Fact]
    public async Task EnsureOnboardedAsync_Should_SaveCerebrasConfiguration_When_CerebrasIsSelected()
    {
        AgentProviderProfile profile = new(ProviderKind.Cerebras, null);

        Mock<ISelectionPrompt> selectionPrompt = new(MockBehavior.Strict);
        SetupProviderSelection(selectionPrompt, OnboardingProviderChoice.Cerebras);

        Mock<ITextPrompt> textPrompt = new(MockBehavior.Strict);

        Mock<ISecretPrompt> secretPrompt = new(MockBehavior.Strict);
        secretPrompt
            .Setup(prompt => prompt.PromptAsync(It.IsAny<SecretPromptRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("  cerebras-key  ");

        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);

        Mock<IStatusMessageWriter> statusMessageWriter = new(MockBehavior.Strict);
        statusMessageWriter
            .Setup(writer => writer.ShowInfoAsync(
                "Welcome to NanoAgent. Let's configure your provider for first run.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        statusMessageWriter
            .Setup(writer => writer.ShowSuccessAsync(
                "Onboarding complete. Provider: Cerebras.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IOnboardingInputValidator> inputValidator = new(MockBehavior.Strict);
        inputValidator
            .Setup(validator => validator.ValidateApiKey("  cerebras-key  "))
            .Returns(InputValidationResult.Success("cerebras-key"));

        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((AgentConfiguration?)null);
        configurationStore
            .Setup(store => store.SaveAsync(
                new AgentConfiguration(profile, null, null, "Cerebras"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        secretStore.Setup(store => store.SaveAsync("cerebras-key", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        secretStore.Setup(store => store.SaveAsync(It.IsAny<string?>(), "cerebras-key", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        Mock<IAgentProviderProfileFactory> profileFactory = new(MockBehavior.Strict);
        profileFactory.Setup(factory => factory.CreateCerebras()).Returns(profile);

        FirstRunOnboardingService sut = CreateSut(
            selectionPrompt.Object,
            textPrompt.Object,
            secretPrompt.Object,
            confirmationPrompt.Object,
            statusMessageWriter.Object,
            inputValidator.Object,
            configurationStore.Object,
            secretStore.Object,
            profileFactory.Object);

        OnboardingResult result = await sut.EnsureOnboardedAsync(CancellationToken.None);

        result.Should().Be(new OnboardingResult(
            profile,
            true,
            ActiveProviderName: "Cerebras"));
        profileFactory.Verify(factory => factory.CreateCerebras(), Times.Once);
        textPrompt.VerifyNoOtherCalls();
        configurationStore.VerifyAll();
        secretStore.VerifyAll();
        statusMessageWriter.VerifyAll();
    }

    [Fact]
    public async Task EnsureOnboardedAsync_Should_SaveGroqConfiguration_When_GroqIsSelected()
    {
        AgentProviderProfile profile = new(ProviderKind.Groq, null);

        Mock<ISelectionPrompt> selectionPrompt = new(MockBehavior.Strict);
        SetupProviderSelection(selectionPrompt, OnboardingProviderChoice.Groq);

        Mock<ITextPrompt> textPrompt = new(MockBehavior.Strict);

        Mock<ISecretPrompt> secretPrompt = new(MockBehavior.Strict);
        secretPrompt
            .Setup(prompt => prompt.PromptAsync(It.IsAny<SecretPromptRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("  groq-key  ");

        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);

        Mock<IStatusMessageWriter> statusMessageWriter = new(MockBehavior.Strict);
        statusMessageWriter
            .Setup(writer => writer.ShowInfoAsync(
                "Welcome to NanoAgent. Let's configure your provider for first run.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        statusMessageWriter
            .Setup(writer => writer.ShowSuccessAsync(
                "Onboarding complete. Provider: Groq.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IOnboardingInputValidator> inputValidator = new(MockBehavior.Strict);
        inputValidator
            .Setup(validator => validator.ValidateApiKey("  groq-key  "))
            .Returns(InputValidationResult.Success("groq-key"));

        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((AgentConfiguration?)null);
        configurationStore
            .Setup(store => store.SaveAsync(
                new AgentConfiguration(profile, null, null, "Groq"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        secretStore.Setup(store => store.SaveAsync("groq-key", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        secretStore.Setup(store => store.SaveAsync(It.IsAny<string?>(), "groq-key", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        Mock<IAgentProviderProfileFactory> profileFactory = new(MockBehavior.Strict);
        profileFactory.Setup(factory => factory.CreateGroq()).Returns(profile);

        FirstRunOnboardingService sut = CreateSut(
            selectionPrompt.Object,
            textPrompt.Object,
            secretPrompt.Object,
            confirmationPrompt.Object,
            statusMessageWriter.Object,
            inputValidator.Object,
            configurationStore.Object,
            secretStore.Object,
            profileFactory.Object);

        OnboardingResult result = await sut.EnsureOnboardedAsync(CancellationToken.None);

        result.Should().Be(new OnboardingResult(
            profile,
            true,
            ActiveProviderName: "Groq"));
        profileFactory.Verify(factory => factory.CreateGroq(), Times.Once);
        textPrompt.VerifyNoOtherCalls();
        configurationStore.VerifyAll();
        secretStore.VerifyAll();
        statusMessageWriter.VerifyAll();
    }

    [Fact]
    public async Task EnsureOnboardedAsync_Should_SaveOpenCodeZenConfiguration_When_OpenCodeZenIsSelected()
    {
        AgentProviderProfile profile = new(ProviderKind.OpenCodeZen, null);

        Mock<ISelectionPrompt> selectionPrompt = new(MockBehavior.Strict);
        SetupProviderSelection(selectionPrompt, OnboardingProviderChoice.OpenCodeZen);

        Mock<ITextPrompt> textPrompt = new(MockBehavior.Strict);

        Mock<ISecretPrompt> secretPrompt = new(MockBehavior.Strict);
        secretPrompt
            .Setup(prompt => prompt.PromptAsync(It.IsAny<SecretPromptRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("  opencode-zen-key  ");

        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);

        Mock<IStatusMessageWriter> statusMessageWriter = new(MockBehavior.Strict);
        statusMessageWriter
            .Setup(writer => writer.ShowInfoAsync(
                "Welcome to NanoAgent. Let's configure your provider for first run.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        statusMessageWriter
            .Setup(writer => writer.ShowSuccessAsync(
                "Onboarding complete. Provider: OpenCode Zen.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IOnboardingInputValidator> inputValidator = new(MockBehavior.Strict);
        inputValidator
            .Setup(validator => validator.ValidateApiKey("  opencode-zen-key  "))
            .Returns(InputValidationResult.Success("opencode-zen-key"));

        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((AgentConfiguration?)null);
        configurationStore
            .Setup(store => store.SaveAsync(
                new AgentConfiguration(profile, null, null, "OpenCode Zen"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        secretStore.Setup(store => store.SaveAsync("opencode-zen-key", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        secretStore.Setup(store => store.SaveAsync(It.IsAny<string?>(), "opencode-zen-key", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        Mock<IAgentProviderProfileFactory> profileFactory = new(MockBehavior.Strict);
        profileFactory.Setup(factory => factory.CreateOpenCodeZen()).Returns(profile);

        FirstRunOnboardingService sut = CreateSut(
            selectionPrompt.Object,
            textPrompt.Object,
            secretPrompt.Object,
            confirmationPrompt.Object,
            statusMessageWriter.Object,
            inputValidator.Object,
            configurationStore.Object,
            secretStore.Object,
            profileFactory.Object);

        OnboardingResult result = await sut.EnsureOnboardedAsync(CancellationToken.None);

        result.Should().Be(new OnboardingResult(
            profile,
            true,
            ActiveProviderName: "OpenCode Zen"));
        profileFactory.Verify(factory => factory.CreateOpenCodeZen(), Times.Once);
        textPrompt.VerifyNoOtherCalls();
        configurationStore.VerifyAll();
        secretStore.VerifyAll();
        statusMessageWriter.VerifyAll();
    }

    [Fact]
    public async Task EnsureOnboardedAsync_Should_SaveOllamaConfiguration_When_OllamaIsSelected()
    {
        AgentProviderProfile profile = new(ProviderKind.Ollama, null);

        Mock<ISelectionPrompt> selectionPrompt = new(MockBehavior.Strict);
        SetupProviderSelection(selectionPrompt, OnboardingProviderChoice.Ollama);

        Mock<ITextPrompt> textPrompt = new(MockBehavior.Strict);
        Mock<ISecretPrompt> secretPrompt = new(MockBehavior.Strict);
        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);

        Mock<IStatusMessageWriter> statusMessageWriter = new(MockBehavior.Strict);
        statusMessageWriter
            .Setup(writer => writer.ShowInfoAsync(
                "Welcome to NanoAgent. Let's configure your provider for first run.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        statusMessageWriter
            .Setup(writer => writer.ShowSuccessAsync(
                "Onboarding complete. Provider: Ollama.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IOnboardingInputValidator> inputValidator = new(MockBehavior.Strict);

        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((AgentConfiguration?)null);
        configurationStore
            .Setup(store => store.SaveAsync(
                new AgentConfiguration(profile, null, null, "Ollama"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        secretStore.Setup(store => store.SaveAsync("ollama", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        secretStore.Setup(store => store.SaveAsync(It.IsAny<string?>(), "ollama", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        Mock<IAgentProviderProfileFactory> profileFactory = new(MockBehavior.Strict);
        profileFactory.Setup(factory => factory.CreateOllama()).Returns(profile);

        FirstRunOnboardingService sut = CreateSut(
            selectionPrompt.Object,
            textPrompt.Object,
            secretPrompt.Object,
            confirmationPrompt.Object,
            statusMessageWriter.Object,
            inputValidator.Object,
            configurationStore.Object,
            secretStore.Object,
            profileFactory.Object);

        OnboardingResult result = await sut.EnsureOnboardedAsync(CancellationToken.None);

        result.Should().Be(new OnboardingResult(
            profile,
            true,
            ActiveProviderName: "Ollama"));
        profileFactory.Verify(factory => factory.CreateOllama(), Times.Once);
        textPrompt.VerifyNoOtherCalls();
        secretPrompt.VerifyNoOtherCalls();
        inputValidator.VerifyNoOtherCalls();
        configurationStore.VerifyAll();
        secretStore.VerifyAll();
        statusMessageWriter.VerifyAll();
    }

    [Fact]
    public async Task EnsureOnboardedAsync_Should_SaveLmStudioConfiguration_When_LmStudioIsSelected()
    {
        AgentProviderProfile profile = new(ProviderKind.LmStudio, null);

        Mock<ISelectionPrompt> selectionPrompt = new(MockBehavior.Strict);
        SetupProviderSelection(selectionPrompt, OnboardingProviderChoice.LmStudio);

        Mock<ITextPrompt> textPrompt = new(MockBehavior.Strict);
        textPrompt
            .Setup(prompt => prompt.PromptAsync(
                It.Is<TextPromptRequest>(request =>
                    request.Label == "Base URL" &&
                    request.Description == "Enter the LM Studio base URL, or leave empty to use http://127.0.0.1:1234/v1."),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("  ");

        Mock<ISecretPrompt> secretPrompt = new(MockBehavior.Strict);
        secretPrompt
            .Setup(prompt => prompt.PromptAsync(It.IsAny<SecretPromptRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("  lm-studio-key  ");
        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);

        Mock<IStatusMessageWriter> statusMessageWriter = new(MockBehavior.Strict);
        statusMessageWriter
            .Setup(writer => writer.ShowInfoAsync(
                "Welcome to NanoAgent. Let's configure your provider for first run.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        statusMessageWriter
            .Setup(writer => writer.ShowSuccessAsync(
                "Onboarding complete. Provider: LM Studio.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IOnboardingInputValidator> inputValidator = new(MockBehavior.Strict);
        inputValidator
            .Setup(validator => validator.ValidateApiKey("  lm-studio-key  "))
            .Returns(InputValidationResult.Success("lm-studio-key"));

        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((AgentConfiguration?)null);
        configurationStore
            .Setup(store => store.SaveAsync(
                new AgentConfiguration(profile, null, null, "LM Studio"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        secretStore.Setup(store => store.SaveAsync("lm-studio-key", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        secretStore.Setup(store => store.SaveAsync(It.IsAny<string?>(), "lm-studio-key", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IAgentProviderProfileFactory> profileFactory = new(MockBehavior.Strict);
        profileFactory.Setup(factory => factory.CreateLmStudio(string.Empty)).Returns(profile);

        FirstRunOnboardingService sut = CreateSut(
            selectionPrompt.Object,
            textPrompt.Object,
            secretPrompt.Object,
            confirmationPrompt.Object,
            statusMessageWriter.Object,
            inputValidator.Object,
            configurationStore.Object,
            secretStore.Object,
            profileFactory.Object);

        OnboardingResult result = await sut.EnsureOnboardedAsync(CancellationToken.None);

        result.Should().Be(new OnboardingResult(
            profile,
            true,
            ActiveProviderName: "LM Studio"));
        profileFactory.Verify(factory => factory.CreateLmStudio(string.Empty), Times.Once);
        textPrompt.VerifyAll();
        secretPrompt.VerifyAll();
        inputValidator.VerifyAll();
        configurationStore.VerifyAll();
        secretStore.VerifyAll();
        statusMessageWriter.VerifyAll();
    }

    [Fact]
    public async Task EnsureOnboardedAsync_Should_SaveGoogleAiStudioConfiguration_When_GoogleAiStudioIsSelected()
    {
        AgentProviderProfile googleAiStudioProfile = new(ProviderKind.GoogleAiStudio, null);

        Mock<ISelectionPrompt> selectionPrompt = new(MockBehavior.Strict);
        SetupProviderSelection(selectionPrompt, OnboardingProviderChoice.GoogleAiStudio);

        Mock<ITextPrompt> textPrompt = new(MockBehavior.Strict);

        Mock<ISecretPrompt> secretPrompt = new(MockBehavior.Strict);
        secretPrompt
            .Setup(prompt => prompt.PromptAsync(It.IsAny<SecretPromptRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("  gemini-key  ");

        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);

        Mock<IStatusMessageWriter> statusMessageWriter = new(MockBehavior.Strict);
        statusMessageWriter
            .Setup(writer => writer.ShowInfoAsync(
                "Welcome to NanoAgent. Let's configure your provider for first run.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        statusMessageWriter
            .Setup(writer => writer.ShowSuccessAsync(
                "Onboarding complete. Provider: Google AI Studio.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IOnboardingInputValidator> inputValidator = new(MockBehavior.Strict);
        inputValidator
            .Setup(validator => validator.ValidateApiKey("  gemini-key  "))
            .Returns(InputValidationResult.Success("gemini-key"));

        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((AgentConfiguration?)null);
        configurationStore
            .Setup(store => store.SaveAsync(
                new AgentConfiguration(googleAiStudioProfile, null, null, "Google AI Studio"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        secretStore.Setup(store => store.SaveAsync("gemini-key", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        secretStore.Setup(store => store.SaveAsync(It.IsAny<string?>(), "gemini-key", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        Mock<IAgentProviderProfileFactory> profileFactory = new(MockBehavior.Strict);
        profileFactory.Setup(factory => factory.CreateGoogleAiStudio()).Returns(googleAiStudioProfile);

        FirstRunOnboardingService sut = CreateSut(
            selectionPrompt.Object,
            textPrompt.Object,
            secretPrompt.Object,
            confirmationPrompt.Object,
            statusMessageWriter.Object,
            inputValidator.Object,
            configurationStore.Object,
            secretStore.Object,
            profileFactory.Object);

        OnboardingResult result = await sut.EnsureOnboardedAsync(CancellationToken.None);

        result.Should().Be(new OnboardingResult(
            googleAiStudioProfile,
            true,
            ActiveProviderName: "Google AI Studio"));
        profileFactory.Verify(factory => factory.CreateGoogleAiStudio(), Times.Once);
        textPrompt.VerifyNoOtherCalls();
        configurationStore.VerifyAll();
        secretStore.VerifyAll();
        statusMessageWriter.VerifyAll();
    }

    [Fact]
    public async Task EnsureOnboardedAsync_Should_SaveAnthropicConfiguration_When_AnthropicIsSelected()
    {
        AgentProviderProfile anthropicProfile = new(ProviderKind.Anthropic, null);

        Mock<ISelectionPrompt> selectionPrompt = new(MockBehavior.Strict);
        SetupProviderSelection(selectionPrompt, OnboardingProviderChoice.Anthropic);

        Mock<ITextPrompt> textPrompt = new(MockBehavior.Strict);

        Mock<ISecretPrompt> secretPrompt = new(MockBehavior.Strict);
        secretPrompt
            .Setup(prompt => prompt.PromptAsync(It.IsAny<SecretPromptRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("  anthropic-key  ");

        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);

        Mock<IStatusMessageWriter> statusMessageWriter = new(MockBehavior.Strict);
        statusMessageWriter
            .Setup(writer => writer.ShowInfoAsync(
                "Welcome to NanoAgent. Let's configure your provider for first run.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        statusMessageWriter
            .Setup(writer => writer.ShowSuccessAsync(
                "Onboarding complete. Provider: Anthropic.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IOnboardingInputValidator> inputValidator = new(MockBehavior.Strict);
        inputValidator
            .Setup(validator => validator.ValidateApiKey("  anthropic-key  "))
            .Returns(InputValidationResult.Success("anthropic-key"));

        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((AgentConfiguration?)null);
        configurationStore
            .Setup(store => store.SaveAsync(
                new AgentConfiguration(anthropicProfile, null, null, "Anthropic"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        secretStore.Setup(store => store.SaveAsync("anthropic-key", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        secretStore.Setup(store => store.SaveAsync(It.IsAny<string?>(), "anthropic-key", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        Mock<IAgentProviderProfileFactory> profileFactory = new(MockBehavior.Strict);
        profileFactory.Setup(factory => factory.CreateAnthropic()).Returns(anthropicProfile);

        FirstRunOnboardingService sut = CreateSut(
            selectionPrompt.Object,
            textPrompt.Object,
            secretPrompt.Object,
            confirmationPrompt.Object,
            statusMessageWriter.Object,
            inputValidator.Object,
            configurationStore.Object,
            secretStore.Object,
            profileFactory.Object);

        OnboardingResult result = await sut.EnsureOnboardedAsync(CancellationToken.None);

        result.Should().Be(new OnboardingResult(
            anthropicProfile,
            true,
            ActiveProviderName: "Anthropic"));
        profileFactory.Verify(factory => factory.CreateAnthropic(), Times.Once);
        textPrompt.VerifyNoOtherCalls();
        configurationStore.VerifyAll();
        secretStore.VerifyAll();
        statusMessageWriter.VerifyAll();
    }

    [Fact]
    public async Task EnsureOnboardedAsync_Should_RePromptBaseUrl_When_InputIsInvalidForCompatibleProvider()
    {
        AgentProviderProfile compatibleProfile = new(ProviderKind.OpenAiCompatible, "https://compatible.example.com/v1");

        Mock<ISelectionPrompt> selectionPrompt = new(MockBehavior.Strict);
        SetupProviderSelection(selectionPrompt, OnboardingProviderChoice.OpenAiCompatible);

        Mock<ITextPrompt> textPrompt = new(MockBehavior.Strict);
        textPrompt
            .SetupSequence(prompt => prompt.PromptAsync(It.IsAny<TextPromptRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("not-a-url")
            .ReturnsAsync("https://compatible.example.com/v1/");

        Mock<ISecretPrompt> secretPrompt = new(MockBehavior.Strict);
        secretPrompt
            .Setup(prompt => prompt.PromptAsync(It.IsAny<SecretPromptRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("compatible-key");

        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);

        Mock<IStatusMessageWriter> statusMessageWriter = new(MockBehavior.Strict);
        statusMessageWriter
            .Setup(writer => writer.ShowInfoAsync(
                "Welcome to NanoAgent. Let's configure your provider for first run.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        statusMessageWriter
            .Setup(writer => writer.ShowErrorAsync(
                "Base URL must be an absolute URL.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        statusMessageWriter
            .Setup(writer => writer.ShowSuccessAsync(
                "Onboarding complete. Provider: OpenAI-compatible provider.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IOnboardingInputValidator> inputValidator = new(MockBehavior.Strict);
        inputValidator
            .SetupSequence(validator => validator.ValidateBaseUrl(It.IsAny<string?>()))
            .Returns(InputValidationResult.Failure("Base URL must be an absolute URL."))
            .Returns(InputValidationResult.Success("https://compatible.example.com/v1"));
        inputValidator
            .Setup(validator => validator.ValidateApiKey("compatible-key"))
            .Returns(InputValidationResult.Success("compatible-key"));

        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((AgentConfiguration?)null);
        configurationStore
            .Setup(store => store.SaveAsync(
                new AgentConfiguration(
                    compatibleProfile,
                    null,
                    null,
                    "OpenAI-compatible provider (compatible.example.com)"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        secretStore.Setup(store => store.SaveAsync("compatible-key", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        secretStore.Setup(store => store.SaveAsync(It.IsAny<string?>(), "compatible-key", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        Mock<IAgentProviderProfileFactory> profileFactory = new(MockBehavior.Strict);
        profileFactory
            .Setup(factory => factory.CreateCompatible("https://compatible.example.com/v1"))
            .Returns(compatibleProfile);

        FirstRunOnboardingService sut = CreateSut(
            selectionPrompt.Object,
            textPrompt.Object,
            secretPrompt.Object,
            confirmationPrompt.Object,
            statusMessageWriter.Object,
            inputValidator.Object,
            configurationStore.Object,
            secretStore.Object,
            profileFactory.Object);

        OnboardingResult result = await sut.EnsureOnboardedAsync(CancellationToken.None);

        result.Should().Be(new OnboardingResult(
            compatibleProfile,
            true,
            ActiveProviderName: "OpenAI-compatible provider (compatible.example.com)"));
        profileFactory.Verify(factory => factory.CreateCompatible("https://compatible.example.com/v1"), Times.Once);
        statusMessageWriter.VerifyAll();
    }

    [Fact]
    public async Task EnsureOnboardedAsync_Should_ThrowPromptCancelledException_When_UserDeclinesIncompleteSetupRecovery()
    {
        AgentProviderProfile existingProfile = new(ProviderKind.OpenAiCompatible, "https://compatible.example.com/v1");

        Mock<ISelectionPrompt> selectionPrompt = new(MockBehavior.Strict);
        Mock<ITextPrompt> textPrompt = new(MockBehavior.Strict);
        Mock<ISecretPrompt> secretPrompt = new(MockBehavior.Strict);

        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);
        confirmationPrompt
            .Setup(prompt => prompt.PromptAsync(It.IsAny<ConfirmationPromptRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        Mock<IStatusMessageWriter> statusMessageWriter = new(MockBehavior.Strict);
        statusMessageWriter
            .Setup(writer => writer.ShowErrorAsync(
                "Found incomplete local provider settings. NanoAgent needs to reconfigure them before continuing.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IOnboardingInputValidator> inputValidator = new(MockBehavior.Strict);
        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentConfiguration(existingProfile, null));

        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        Mock<IAgentProviderProfileFactory> profileFactory = new(MockBehavior.Strict);

        FirstRunOnboardingService sut = CreateSut(
            selectionPrompt.Object,
            textPrompt.Object,
            secretPrompt.Object,
            confirmationPrompt.Object,
            statusMessageWriter.Object,
            inputValidator.Object,
            configurationStore.Object,
            secretStore.Object,
            profileFactory.Object);

        Func<Task> action = () => sut.EnsureOnboardedAsync(CancellationToken.None);

        await action.Should().ThrowAsync<PromptCancelledException>();
        selectionPrompt.VerifyNoOtherCalls();
        textPrompt.VerifyNoOtherCalls();
        secretPrompt.VerifyNoOtherCalls();
    }

    private static void SetupProviderSelection(
        Mock<ISelectionPrompt> selectionPrompt,
        OnboardingProviderChoice providerChoice)
    {
        OnboardingProviderSetupChoice setupChoice = providerChoice switch
        {
            OnboardingProviderChoice.NanoAgentEnterprise => OnboardingProviderSetupChoice.NanoAgentEnterprise,
            OnboardingProviderChoice.OpenAiChatGptAccount or
                OnboardingProviderChoice.AnthropicClaudeAccount or
                OnboardingProviderChoice.GitHubCopilot => OnboardingProviderSetupChoice.SubscriptionAccount,
            OnboardingProviderChoice.OpenAiCompatible => OnboardingProviderSetupChoice.OpenAiCompatible,
            OnboardingProviderChoice.Ollama or
                OnboardingProviderChoice.LmStudio => OnboardingProviderSetupChoice.LocalProvider,
            _ => OnboardingProviderSetupChoice.ApiKey
        };

        selectionPrompt
            .Setup(prompt => prompt.PromptAsync(
                It.IsAny<SelectionPromptRequest<OnboardingProviderSetupChoice>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(setupChoice);

        if (setupChoice is OnboardingProviderSetupChoice.NanoAgentEnterprise or OnboardingProviderSetupChoice.OpenAiCompatible)
        {
            return;
        }

        selectionPrompt
            .Setup(prompt => prompt.PromptAsync(
                It.IsAny<SelectionPromptRequest<OnboardingProviderChoice>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(providerChoice);
    }

    private static FirstRunOnboardingService CreateSut(
        ISelectionPrompt selectionPrompt,
        ITextPrompt textPrompt,
        ISecretPrompt secretPrompt,
        IConfirmationPrompt confirmationPrompt,
        IStatusMessageWriter statusMessageWriter,
        IOnboardingInputValidator inputValidator,
        IAgentConfigurationStore configurationStore,
        IApiKeySecretStore secretStore,
        IAgentProviderProfileFactory profileFactory,
        IOpenAiChatGptAccountAuthenticator? openAiChatGptAccountAuthenticator = null,
        IAnthropicClaudeAccountAuthenticator? anthropicClaudeAccountAuthenticator = null,
        IGitHubCopilotAuthenticator? gitHubCopilotAuthenticator = null,
        INanoAgentEnterpriseAuthenticator? nanoAgentEnterpriseAuthenticator = null)
    {
        return new FirstRunOnboardingService(
            selectionPrompt,
            textPrompt,
            secretPrompt,
            confirmationPrompt,
            statusMessageWriter,
            inputValidator,
            configurationStore,
            secretStore,
            profileFactory,
            NullLogger<FirstRunOnboardingService>.Instance,
            openAiChatGptAccountAuthenticator,
            anthropicClaudeAccountAuthenticator,
            gitHubCopilotAuthenticator,
            nanoAgentEnterpriseAuthenticator);
    }
}
