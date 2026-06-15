using FluentAssertions;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using NanoAgent.Domain.Services;
using NanoAgent.Sdk.Internal;

namespace NanoAgent.Tests.Sdk;

public sealed class InMemoryStoresTests
{
    [Fact]
    public async Task AgentConfigurationStore_Should_ReturnSeededConfiguration()
    {
        AgentProviderProfile profile = new AgentProviderProfileFactory().CreateAnthropic();
        AgentConfiguration configuration = new(profile, PreferredModelId: "claude-opus-4-8", ActiveProviderName: "Anthropic");
        InMemoryAgentConfigurationStore sut = new(configuration);

        AgentConfiguration? loaded = await sut.LoadAsync(CancellationToken.None);

        loaded.Should().BeSameAs(configuration);
    }

    [Fact]
    public async Task AgentConfigurationStore_SetActiveProvider_Should_UpdateInMemory()
    {
        AgentProviderProfile profile = new AgentProviderProfileFactory().CreateAnthropic();
        InMemoryAgentConfigurationStore sut = new(new AgentConfiguration(profile, null, ActiveProviderName: "Anthropic"));

        await sut.SetActiveProviderAsync("Custom", CancellationToken.None);
        AgentConfiguration? loaded = await sut.LoadAsync(CancellationToken.None);

        loaded!.ActiveProviderName.Should().Be("Custom");
    }

    [Fact]
    public async Task ApiKeySecretStore_Should_ReturnSeededKey_ForBothOverloads()
    {
        InMemoryApiKeySecretStore sut = new("  sk-test  ");

        (await sut.LoadAsync(CancellationToken.None)).Should().Be("sk-test");
        (await sut.LoadAsync("Anthropic", CancellationToken.None)).Should().Be("sk-test");
    }

    [Fact]
    public async Task ApiKeySecretStore_Save_Should_OverwriteKey()
    {
        InMemoryApiKeySecretStore sut = new(null);

        await sut.SaveAsync("sk-new", CancellationToken.None);

        (await sut.LoadAsync(CancellationToken.None)).Should().Be("sk-new");
    }
}
