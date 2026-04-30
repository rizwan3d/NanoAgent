using FluentAssertions;
using Moq;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Commands;
using NanoAgent.Application.Models;
using NanoAgent.Application.Services;
using NanoAgent.Domain.Models;

namespace NanoAgent.Tests.Application.Commands;

public sealed class UseModelCommandHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_Should_SwitchAndSaveModel_When_ModelIsAvailable()
    {
        AgentProviderProfile providerProfile = new(ProviderKind.OpenAi, null);
        ReplSessionContext session = new(
            providerProfile,
            "model-a",
            ["model-a", "model-b"],
            reasoningEffort: "on");
        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore
            .Setup(store => store.SaveAsync(
                new AgentConfiguration(providerProfile, "model-b", "on"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        UseModelCommandHandler sut = new(
            new ModelActivationService(),
            configurationStore.Object);

        ReplCommandResult result = await sut.ExecuteAsync(
            new ReplCommandContext(
                "use",
                "model-b",
                ["model-b"],
                "/use model-b",
                session),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
        result.Message.Should().Be("Active model switched to 'model-b'.");
        session.ActiveModelId.Should().Be("model-b");
        configurationStore.VerifyAll();
    }
}
