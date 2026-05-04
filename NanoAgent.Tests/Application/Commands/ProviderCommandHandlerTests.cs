using FluentAssertions;
using Moq;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Commands;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;

namespace NanoAgent.Tests.Application.Commands;

public sealed class ProviderCommandHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_Should_SwitchToSavedProviderUsingProviderScopedSecret()
    {
        AgentProviderProfile openAiProfile = new(ProviderKind.OpenAi, null);
        SavedProviderConfiguration savedProvider = new("OpenAI", openAiProfile, "gpt-5.4");

        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore
            .Setup(store => store.ListProvidersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([savedProvider]);
        configurationStore
            .Setup(store => store.SetActiveProviderAsync("OpenAI", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        configurationStore
            .Setup(store => store.SaveAsync(
                new AgentConfiguration(openAiProfile, "gpt-5.4", "on", "OpenAI"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore
            .Setup(store => store.LoadAsync("OpenAI", It.IsAny<CancellationToken>()))
            .ReturnsAsync("openai-key");
        secretStore
            .Setup(store => store.SaveAsync("openai-key", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IModelDiscoveryService> modelDiscoveryService = new(MockBehavior.Strict);
        modelDiscoveryService
            .Setup(service => service.DiscoverAndSelectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModelDiscoveryResult(
                [new AvailableModel("gpt-5.4", 400_000)],
                "gpt-5.4",
                ModelSelectionSource.ConfiguredDefault,
                ConfiguredDefaultModelStatus.Matched,
                "gpt-5.4",
                HadDuplicateModelIds: false));

        ProviderCommandHandler sut = new(
            configurationStore.Object,
            secretStore.Object,
            modelDiscoveryService.Object,
            Mock.Of<ISelectionPrompt>());
        ReplSessionContext session = new(
            new AgentProviderProfile(ProviderKind.Anthropic, null),
            "claude-sonnet-4-6",
            ["claude-sonnet-4-6"],
            reasoningEffort: "on",
            activeProviderName: "Anthropic");

        ReplCommandResult result = await sut.ExecuteAsync(
            new ReplCommandContext(
                "provider",
                "OpenAI",
                ["OpenAI"],
                "/provider OpenAI",
                session),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
        session.ProviderProfile.Should().Be(openAiProfile);
        session.ActiveProviderName.Should().Be("OpenAI");
        session.ActiveModelId.Should().Be("gpt-5.4");
        session.ActiveModelContextWindowTokens.Should().Be(400_000);
        configurationStore.VerifyAll();
        secretStore.VerifyAll();
        modelDiscoveryService.VerifyAll();
    }
}
