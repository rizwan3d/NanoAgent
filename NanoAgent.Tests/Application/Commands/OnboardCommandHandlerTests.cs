using FluentAssertions;
using Moq;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Commands;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;

namespace NanoAgent.Tests.Application.Commands;

public sealed class OnboardCommandHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_Should_ReconfigureProviderAndSwitchActiveSession()
    {
        AgentProviderProfile providerProfile = new(ProviderKind.OpenRouter, null);

        Mock<IFirstRunOnboardingService> onboardingService = new(MockBehavior.Strict);
        onboardingService
            .Setup(service => service.ReconfigureAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OnboardingResult(providerProfile, WasOnboardedDuringCurrentRun: true));

        Mock<IModelDiscoveryService> modelDiscoveryService = new(MockBehavior.Strict);
        modelDiscoveryService
            .Setup(service => service.DiscoverAndSelectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModelDiscoveryResult(
                [
                    new AvailableModel("openai/gpt-5.4"),
                    new AvailableModel("anthropic/claude-sonnet-4.6")
                ],
                "openai/gpt-5.4",
                ModelSelectionSource.FirstReturnedModel,
                ConfiguredDefaultModelStatus.NotConfigured,
                null,
                HadDuplicateModelIds: false));

        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore
            .Setup(store => store.SaveAsync(
                new AgentConfiguration(providerProfile, "openai/gpt-5.4", "on"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        OnboardCommandHandler sut = new(
            onboardingService.Object,
            modelDiscoveryService.Object,
            configurationStore.Object);
        ReplSessionContext session = new(
            new AgentProviderProfile(ProviderKind.OpenAi, null),
            "gpt-5.4",
            ["gpt-5.4"],
            reasoningEffort: "on");

        ReplCommandResult result = await sut.ExecuteAsync(
            new ReplCommandContext(
                "onboard",
                string.Empty,
                [],
                "/onboard",
                session),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
        result.Message.Should().Contain("Provider onboarding complete.");
        session.ProviderProfile.Should().Be(providerProfile);
        session.ActiveModelId.Should().Be("openai/gpt-5.4");
        session.AvailableModelIds.Should().Equal("openai/gpt-5.4", "anthropic/claude-sonnet-4.6");
        configurationStore.VerifyAll();
    }
}
