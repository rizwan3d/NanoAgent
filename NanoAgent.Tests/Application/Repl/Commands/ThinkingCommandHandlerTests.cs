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
    public async Task ExecuteAsync_Should_ShowCurrentThinkingEffort_When_ArgumentIsMissing()
    {
        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        ThinkingCommandHandler sut = new(configurationStore.Object);
        ReplSessionContext session = CreateSession();
        session.SetReasoningEffort("high");

        ReplCommandResult result = await sut.ExecuteAsync(
            new ReplCommandContext("thinking", string.Empty, [], "/thinking", session),
            CancellationToken.None);

        result.Message.Should().Contain("Thinking effort: high");
        configurationStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_Should_SetThinkingEffort_AndPersistConfiguration()
    {
        ReplSessionContext session = CreateSession();
        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore
            .Setup(store => store.SaveAsync(
                new AgentConfiguration(session.ProviderProfile, session.ActiveModelId, "xhigh"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        ThinkingCommandHandler sut = new(configurationStore.Object);

        ReplCommandResult result = await sut.ExecuteAsync(
            new ReplCommandContext("thinking", "xhigh", ["xhigh"], "/thinking xhigh", session),
            CancellationToken.None);

        session.ReasoningEffort.Should().Be("xhigh");
        session.IsPersistedStateDirty.Should().BeTrue();
        result.Message.Should().Be("Thinking effort set to 'xhigh'.");
        configurationStore.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_Should_ResetThinkingEffort_AndPersistProviderDefault()
    {
        ReplSessionContext session = CreateSession();
        session.SetReasoningEffort("medium");
        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore
            .Setup(store => store.SaveAsync(
                new AgentConfiguration(session.ProviderProfile, session.ActiveModelId, null),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        ThinkingCommandHandler sut = new(configurationStore.Object);

        ReplCommandResult result = await sut.ExecuteAsync(
            new ReplCommandContext("thinking", "default", ["default"], "/thinking default", session),
            CancellationToken.None);

        session.ReasoningEffort.Should().BeNull();
        result.Message.Should().Be("Thinking effort reset to provider default.");
        configurationStore.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnError_When_ThinkingEffortIsUnsupported()
    {
        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        ThinkingCommandHandler sut = new(configurationStore.Object);
        ReplSessionContext session = CreateSession();

        ReplCommandResult result = await sut.ExecuteAsync(
            new ReplCommandContext("thinking", "turbo", ["turbo"], "/thinking turbo", session),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Error);
        result.Message.Should().Contain("Unsupported thinking effort 'turbo'");
        result.Message.Should().Contain("none, minimal, low, medium, high, xhigh");
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
