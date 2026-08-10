using FluentAssertions;
using Moq;
using StemCode.Application.Abstractions;
using StemCode.Application.Commands;
using StemCode.Application.Models;
using StemCode.Domain.Models;

namespace StemCode.Tests.Application.Commands;

public sealed class ReasoningCommandHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_Should_SetReasoningEffort_AndEnableThinking()
    {
        AgentProviderProfile providerProfile = new(ProviderKind.OpenAi, null);
        ReplSessionContext session = new(
            providerProfile,
            "gpt-5.4",
            ["gpt-5.4"],
            thinkingMode: "off");
        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore
            .Setup(store => store.SaveAsync(
                new AgentConfiguration(providerProfile, "gpt-5.4", "high", null, "on"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        ReasoningCommandHandler sut = new(configurationStore.Object, Mock.Of<IInteractiveReasoningSelectionService>());

        ReplCommandResult result = await sut.ExecuteAsync(
            new ReplCommandContext(
                "reasoning",
                "high",
                ["high"],
                "/reasoning high",
                session),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
        result.Message.Should().Contain("high");
        session.ThinkingMode.Should().Be("on");
        session.ReasoningEffort.Should().Be("high");
        configurationStore.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_Should_ShowCurrentReasoningSettings()
    {
        ReplSessionContext session = new(
            new AgentProviderProfile(ProviderKind.OpenAi, null),
            "gpt-5.4",
            ["gpt-5.4"],
            thinkingMode: "on",
            reasoningEffort: "medium");
        ReasoningCommandHandler sut = new(
            Mock.Of<IAgentConfigurationStore>(MockBehavior.Strict),
            Mock.Of<IInteractiveReasoningSelectionService>());

        ReplCommandResult result = await sut.ExecuteAsync(
            new ReplCommandContext(
                "reasoning",
                "show",
                ["show"],
                "/reasoning show",
                session),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
        result.Message.Should().Contain("medium");
        result.Message.Should().Contain("Thinking: on");
    }
}
