using FluentAssertions;
using Moq;
using StemCode.Application.Abstractions;
using StemCode.Application.Commands;
using StemCode.Application.Models;
using StemCode.Domain.Models;

namespace StemCode.Tests.Application.Commands;

public sealed class ThinkingCommandHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_Should_TurnThinkingOff_WithoutClearingReasoningEffort()
    {
        AgentProviderProfile providerProfile = new(ProviderKind.OpenAi, null);
        ReplSessionContext session = new(
            providerProfile,
            "gpt-5.4",
            ["gpt-5.4"],
            thinkingMode: "on",
            reasoningEffort: "high");
        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore
            .Setup(store => store.SaveAsync(
                new AgentConfiguration(providerProfile, "gpt-5.4", "high", null, "off"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        ThinkingCommandHandler sut = new(configurationStore.Object, Mock.Of<ISelectionPrompt>());

        ReplCommandResult result = await sut.ExecuteAsync(
            new ReplCommandContext(
                "thinking",
                "off",
                ["off"],
                "/thinking off",
                session),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
        session.ThinkingMode.Should().Be("off");
        session.ReasoningEffort.Should().Be("high");
        configurationStore.VerifyAll();
    }
}
