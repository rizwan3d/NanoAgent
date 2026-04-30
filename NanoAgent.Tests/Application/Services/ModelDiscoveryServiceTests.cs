using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Services;
using NanoAgent.Domain.Abstractions;
using NanoAgent.Domain.Models;
using NanoAgent.Domain.Services;
using NanoAgent.Infrastructure.Models;

namespace NanoAgent.Tests.Application.Services;

public sealed class ModelDiscoveryServiceTests
{
    [Fact]
    public async Task DiscoverAndSelectAsync_Should_UseConfiguredDefault_When_ItMatchesFetchedModels()
    {
        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore
            .Setup(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentConfiguration(
                new AgentProviderProfile(ProviderKind.OpenAi, null),
                "gpt-5"));

        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore
            .Setup(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-key");

        Mock<IModelProviderClient> providerClient = new(MockBehavior.Strict);
        providerClient
            .Setup(client => client.GetAvailableModelsAsync(
                It.IsAny<AgentProviderProfile>(),
                "test-key",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new AvailableModel("gpt-5-mini", 128_000),
                new AvailableModel("gpt-5", 400_000),
                new AvailableModel("gpt-5", 8_000)
            ]);

        ModelSelectionSettings settings = new(TimeSpan.FromMinutes(5));

        ModelDiscoveryService sut = CreateSut(
            configurationStore.Object,
            secretStore.Object,
            providerClient.Object,
            new InMemoryModelCache(),
            new ConfiguredOrFirstModelSelectionPolicy(),
            settings);

        ModelDiscoveryResult result = await sut.DiscoverAndSelectAsync(CancellationToken.None);

        result.SelectedModelId.Should().Be("gpt-5");
        result.SelectionSource.Should().Be(ModelSelectionSource.ConfiguredDefault);
        result.ConfiguredDefaultStatus.Should().Be(ConfiguredDefaultModelStatus.Matched);
        result.HadDuplicateModelIds.Should().BeTrue();
        result.AvailableModels.Select(model => model.Id).Should().Equal("gpt-5-mini", "gpt-5");
        result.AvailableModels.Select(model => model.ContextWindowTokens).Should().Equal(128_000, 400_000);
        configurationStore.Verify(store => store.SaveAsync(It.IsAny<AgentConfiguration>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DiscoverAndSelectAsync_Should_UseFirstReturnedModel_When_ConfiguredDefaultIsNotReturned()
    {
        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore
            .Setup(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentConfiguration(
                new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
                "gpt-5"));
        configurationStore
            .Setup(store => store.SaveAsync(
                new AgentConfiguration(
                    new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
                    "gpt-4.1"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore
            .Setup(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-key");

        Mock<IModelProviderClient> providerClient = new(MockBehavior.Strict);
        providerClient
            .Setup(client => client.GetAvailableModelsAsync(
                It.IsAny<AgentProviderProfile>(),
                "test-key",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new AvailableModel("gpt-4.1"),
                new AvailableModel("gpt-4.1-mini")
            ]);

        ModelSelectionSettings settings = new(TimeSpan.FromMinutes(5));

        ModelDiscoveryService sut = CreateSut(
            configurationStore.Object,
            secretStore.Object,
            providerClient.Object,
            new InMemoryModelCache(),
            new ConfiguredOrFirstModelSelectionPolicy(),
            settings);

        ModelDiscoveryResult result = await sut.DiscoverAndSelectAsync(CancellationToken.None);

        result.SelectedModelId.Should().Be("gpt-4.1");
        result.SelectionSource.Should().Be(ModelSelectionSource.FirstReturnedModel);
        result.ConfiguredDefaultStatus.Should().Be(ConfiguredDefaultModelStatus.NotFound);
        result.ConfiguredDefaultModel.Should().Be("gpt-5");
    }

    [Fact]
    public async Task DiscoverAndSelectAsync_Should_UseCachedModels_When_CalledRepeatedly()
    {
        int providerCallCount = 0;

        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore
            .Setup(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentConfiguration(
                new AgentProviderProfile(ProviderKind.OpenAi, null),
                null));
        configurationStore
            .Setup(store => store.SaveAsync(
                new AgentConfiguration(
                    new AgentProviderProfile(ProviderKind.OpenAi, null),
                    "gpt-5-mini"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore
            .Setup(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-key");

        Mock<IModelProviderClient> providerClient = new(MockBehavior.Strict);
        providerClient
            .Setup(client => client.GetAvailableModelsAsync(
                It.IsAny<AgentProviderProfile>(),
                "test-key",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                providerCallCount++;
                return [
                    new AvailableModel("gpt-5-mini"),
                    new AvailableModel("gpt-4.1")
                ];
            });

        ModelSelectionSettings settings = new(TimeSpan.FromMinutes(5));

        ModelDiscoveryService sut = CreateSut(
            configurationStore.Object,
            secretStore.Object,
            providerClient.Object,
            new InMemoryModelCache(),
            new ConfiguredOrFirstModelSelectionPolicy(),
            settings);

        await sut.DiscoverAndSelectAsync(CancellationToken.None);
        await sut.DiscoverAndSelectAsync(CancellationToken.None);

        providerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task DiscoverAndSelectAsync_Should_ThrowModelDiscoveryException_When_ProviderReturnsNoUsableModels()
    {
        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore
            .Setup(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentConfiguration(
                new AgentProviderProfile(ProviderKind.OpenAi, null),
                null));

        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore
            .Setup(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-key");

        Mock<IModelProviderClient> providerClient = new(MockBehavior.Strict);
        providerClient
            .Setup(client => client.GetAvailableModelsAsync(
                It.IsAny<AgentProviderProfile>(),
                "test-key",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new AvailableModel(" "),
                new AvailableModel("")
            ]);

        ModelSelectionSettings settings = new(TimeSpan.FromMinutes(5));

        ModelDiscoveryService sut = CreateSut(
            configurationStore.Object,
            secretStore.Object,
            providerClient.Object,
            new InMemoryModelCache(),
            new ConfiguredOrFirstModelSelectionPolicy(),
            settings);

        Func<Task> action = () => sut.DiscoverAndSelectAsync(CancellationToken.None);

        await action.Should().ThrowAsync<ModelDiscoveryException>()
            .WithMessage("*no usable models*");
    }

    private static ModelDiscoveryService CreateSut(
        IAgentConfigurationStore configurationStore,
        IApiKeySecretStore secretStore,
        IModelProviderClient providerClient,
        IModelCache modelCache,
        IModelSelectionPolicy selectionPolicy,
        ModelSelectionSettings settings)
    {
        return new ModelDiscoveryService(
            configurationStore,
            secretStore,
            providerClient,
            modelCache,
            selectionPolicy,
            settings,
            NullLogger<ModelDiscoveryService>.Instance);
    }
}
