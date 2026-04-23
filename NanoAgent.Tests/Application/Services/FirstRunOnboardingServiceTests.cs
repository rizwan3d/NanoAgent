using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Services;
using NanoAgent.Domain.Abstractions;
using NanoAgent.Domain.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

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
    public async Task EnsureOnboardedAsync_Should_SaveOpenAiConfiguration_When_OpenAiIsSelected()
    {
        AgentProviderProfile openAiProfile = new(ProviderKind.OpenAi, null);

        Mock<ISelectionPrompt> selectionPrompt = new(MockBehavior.Strict);
        selectionPrompt
            .Setup(prompt => prompt.PromptAsync(It.IsAny<SelectionPromptRequest<OnboardingProviderChoice>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OnboardingProviderChoice.OpenAi);

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
                new AgentConfiguration(openAiProfile, null),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        secretStore.Setup(store => store.SaveAsync("sk-openai", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

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

        result.Should().Be(new OnboardingResult(openAiProfile, true));
        profileFactory.Verify(factory => factory.CreateOpenAi(), Times.Once);
        textPrompt.VerifyNoOtherCalls();
        configurationStore.VerifyAll();
        secretStore.VerifyAll();
        statusMessageWriter.VerifyAll();
    }

    [Fact]
    public async Task EnsureOnboardedAsync_Should_SaveGoogleAiStudioConfiguration_When_GoogleAiStudioIsSelected()
    {
        AgentProviderProfile googleAiStudioProfile = new(ProviderKind.GoogleAiStudio, null);

        Mock<ISelectionPrompt> selectionPrompt = new(MockBehavior.Strict);
        selectionPrompt
            .Setup(prompt => prompt.PromptAsync(It.IsAny<SelectionPromptRequest<OnboardingProviderChoice>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OnboardingProviderChoice.GoogleAiStudio);

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
                new AgentConfiguration(googleAiStudioProfile, null),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        secretStore.Setup(store => store.SaveAsync("gemini-key", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

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

        result.Should().Be(new OnboardingResult(googleAiStudioProfile, true));
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
        selectionPrompt
            .Setup(prompt => prompt.PromptAsync(It.IsAny<SelectionPromptRequest<OnboardingProviderChoice>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OnboardingProviderChoice.Anthropic);

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
                new AgentConfiguration(anthropicProfile, null),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        secretStore.Setup(store => store.SaveAsync("anthropic-key", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

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

        result.Should().Be(new OnboardingResult(anthropicProfile, true));
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
        selectionPrompt
            .Setup(prompt => prompt.PromptAsync(It.IsAny<SelectionPromptRequest<OnboardingProviderChoice>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OnboardingProviderChoice.OpenAiCompatible);

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
                new AgentConfiguration(compatibleProfile, null),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        secretStore.Setup(store => store.SaveAsync("compatible-key", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

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

        result.Should().Be(new OnboardingResult(compatibleProfile, true));
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

    private static FirstRunOnboardingService CreateSut(
        ISelectionPrompt selectionPrompt,
        ITextPrompt textPrompt,
        ISecretPrompt secretPrompt,
        IConfirmationPrompt confirmationPrompt,
        IStatusMessageWriter statusMessageWriter,
        IOnboardingInputValidator inputValidator,
        IAgentConfigurationStore configurationStore,
        IApiKeySecretStore secretStore,
        IAgentProviderProfileFactory profileFactory)
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
            NullLogger<FirstRunOnboardingService>.Instance);
    }
}
