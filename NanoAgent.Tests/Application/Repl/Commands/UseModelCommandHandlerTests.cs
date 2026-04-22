using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Repl.Commands;
using NanoAgent.Application.Services;
using NanoAgent.Domain.Models;
using FluentAssertions;
using Moq;

namespace NanoAgent.Tests.Application.Repl.Commands;

public sealed class UseModelCommandHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_Should_ReturnUsageError_When_ModelArgumentIsMissing()
    {
        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        UseModelCommandHandler sut = new(new ModelActivationService(), configurationStore.Object);
        ReplSessionContext session = new(
            new AgentProviderProfile(ProviderKind.OpenAi, null),
            "gpt-5-mini",
            ["gpt-5-mini"]);

        ReplCommandResult result = await sut.ExecuteAsync(
            new ReplCommandContext("use", string.Empty, [], "/use", session),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Error);
        result.Message.Should().Be("Usage: /use <model>");
    }

    [Fact]
    public async Task ExecuteAsync_Should_SwitchActiveModel_When_ExactModelIdMatches()
    {
        ReplSessionContext session = new(
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "qwen/qwen3-coder-30b",
            ["qwen/qwen3-coder-30b", "openai/gpt-oss-20b"]);
        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore
            .Setup(store => store.SaveAsync(
                new AgentConfiguration(session.ProviderProfile, "openai/gpt-oss-20b"),
                It.IsAny<CancellationToken>()))
            .Returns(() => Task.CompletedTask);
        UseModelCommandHandler sut = new(new ModelActivationService(), configurationStore.Object);

        ReplCommandResult result = await sut.ExecuteAsync(
            new ReplCommandContext("use", "openai/gpt-oss-20b", ["openai/gpt-oss-20b"], "/use openai/gpt-oss-20b", session),
            CancellationToken.None);

        session.ActiveModelId.Should().Be("openai/gpt-oss-20b");
        result.Message.Should().Be("Active model switched to 'openai/gpt-oss-20b'.");
    }

    [Fact]
    public async Task ExecuteAsync_Should_SwitchActiveModel_When_UniqueTerminalSegmentMatches()
    {
        ReplSessionContext session = new(
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "qwen/qwen3-coder-30b",
            ["qwen/qwen3-coder-30b", "openai/gpt-oss-20b"]);
        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore
            .Setup(store => store.SaveAsync(
                new AgentConfiguration(session.ProviderProfile, "openai/gpt-oss-20b"),
                It.IsAny<CancellationToken>()))
            .Returns(() => Task.CompletedTask);
        UseModelCommandHandler sut = new(new ModelActivationService(), configurationStore.Object);

        ReplCommandResult result = await sut.ExecuteAsync(
            new ReplCommandContext("use", "gpt-oss-20b", ["gpt-oss-20b"], "/use gpt-oss-20b", session),
            CancellationToken.None);

        session.ActiveModelId.Should().Be("openai/gpt-oss-20b");
        result.Message.Should().Be("Active model switched to 'openai/gpt-oss-20b'.");
    }

    [Fact]
    public async Task ExecuteAsync_Should_PreserveThinkingEffort_When_ModelSwitchIsPersisted()
    {
        ReplSessionContext session = new(
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "qwen/qwen3-coder-30b",
            ["qwen/qwen3-coder-30b", "openai/gpt-oss-20b"],
            reasoningEffort: "high");
        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore
            .Setup(store => store.SaveAsync(
                new AgentConfiguration(session.ProviderProfile, "openai/gpt-oss-20b", "high"),
                It.IsAny<CancellationToken>()))
            .Returns(() => Task.CompletedTask);
        UseModelCommandHandler sut = new(new ModelActivationService(), configurationStore.Object);

        ReplCommandResult result = await sut.ExecuteAsync(
            new ReplCommandContext("use", "openai/gpt-oss-20b", ["openai/gpt-oss-20b"], "/use openai/gpt-oss-20b", session),
            CancellationToken.None);

        result.Message.Should().Be("Active model switched to 'openai/gpt-oss-20b'.");
        session.ReasoningEffort.Should().Be("high");
        configurationStore.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnError_When_ModelDoesNotExist()
    {
        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        UseModelCommandHandler sut = new(new ModelActivationService(), configurationStore.Object);
        ReplSessionContext session = new(
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "qwen/qwen3-coder-30b",
            ["qwen/qwen3-coder-30b", "openai/gpt-oss-20b"]);

        ReplCommandResult result = await sut.ExecuteAsync(
            new ReplCommandContext("use", "gpt-5-mini", ["gpt-5-mini"], "/use gpt-5-mini", session),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Error);
        result.Message.Should().Be("Model 'gpt-5-mini' is not available. Use /models to see valid choices.");
    }
}
