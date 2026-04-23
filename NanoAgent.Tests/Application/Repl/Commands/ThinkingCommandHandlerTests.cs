using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Repl.Commands;
using NanoAgent.Domain.Models;
using FluentAssertions;
using Moq;

namespace NanoAgent.Tests.Application.Repl.Commands;

public sealed class ThinkingCommandHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_Should_ShowCurrentThinkingMode_When_ArgumentIsMissing()
    {
        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        ThinkingCommandHandler sut = new(configurationStore.Object);
        ReplSessionContext session = CreateSession();
        session.SetReasoningEffort("on");

        ReplCommandResult result = await sut.ExecuteAsync(
            new ReplCommandContext("thinking", string.Empty, [], "/thinking", session),
            CancellationToken.None);

        result.Message.Should().Contain("Thinking: on");
        configurationStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_Should_TurnThinkingOn_AndPersistConfiguration()
    {
        ReplSessionContext session = CreateSession();
        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore
            .Setup(store => store.SaveAsync(
                new AgentConfiguration(session.ProviderProfile, session.ActiveModelId, "on"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        ThinkingCommandHandler sut = new(configurationStore.Object);

        ReplCommandResult result = await sut.ExecuteAsync(
            new ReplCommandContext("thinking", "on", ["on"], "/thinking on", session),
            CancellationToken.None);

        session.ReasoningEffort.Should().Be("on");
        session.IsPersistedStateDirty.Should().BeTrue();
        result.Message.Should().Be("Thinking turned on.");
        configurationStore.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_Should_TurnThinkingOff_AndPersistConfiguration()
    {
        ReplSessionContext session = CreateSession();
        session.SetReasoningEffort("on");
        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore
            .Setup(store => store.SaveAsync(
                new AgentConfiguration(session.ProviderProfile, session.ActiveModelId, "off"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        ThinkingCommandHandler sut = new(configurationStore.Object);

        ReplCommandResult result = await sut.ExecuteAsync(
            new ReplCommandContext("thinking", "off", ["off"], "/thinking off", session),
            CancellationToken.None);

        session.ReasoningEffort.Should().Be("off");
        result.Message.Should().Be("Thinking turned off.");
        configurationStore.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnError_When_ThinkingModeIsUnsupported()
    {
        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        ThinkingCommandHandler sut = new(configurationStore.Object);
        ReplSessionContext session = CreateSession();

        ReplCommandResult result = await sut.ExecuteAsync(
            new ReplCommandContext("thinking", "turbo", ["turbo"], "/thinking turbo", session),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Error);
        result.Message.Should().Contain("Unsupported thinking mode 'turbo'");
        result.Message.Should().Contain("on, off");
        session.ReasoningEffort.Should().BeNull();
        configurationStore.VerifyNoOtherCalls();
    }

    private static ReplSessionContext CreateSession()
    {
        return new ReplSessionContext(
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "gpt-5.4",
            ["gpt-5.4"]);
    }
}
