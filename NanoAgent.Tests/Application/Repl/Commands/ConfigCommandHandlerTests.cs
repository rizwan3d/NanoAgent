using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Profiles;
using NanoAgent.Application.Repl.Commands;
using NanoAgent.Domain.Models;
using FluentAssertions;
using Moq;

namespace NanoAgent.Tests.Application.Repl.Commands;

public sealed class ConfigCommandHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_Should_ReturnConfigurationSummary_When_CommandRuns()
    {
        Mock<IUserDataPathProvider> pathProvider = new(MockBehavior.Strict);
        pathProvider
            .Setup(provider => provider.GetConfigurationFilePath())
            .Returns("C:\\Users\\test\\AppData\\Roaming\\NanoAgent\\agent-profile.json");

        ConfigCommandHandler sut = new(pathProvider.Object);
        ReplSessionContext session = new(
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "openai/gpt-oss-20b",
            ["openai/gpt-oss-20b"],
            agentProfile: BuiltInAgentProfiles.Review);

        ReplCommandResult result = await sut.ExecuteAsync(
            new ReplCommandContext("config", string.Empty, [], "/config", session),
            CancellationToken.None);

        result.ExitRequested.Should().BeFalse();
        result.Message.Should().Contain($"Session: {session.SessionId}");
        result.Message.Should().Contain("Provider: OpenAI-compatible provider");
        result.Message.Should().Contain("Base URL: https://provider.example.com/v1");
        result.Message.Should().Contain("Configuration file:");
        result.Message.Should().Contain("Agent profile: review");
        result.Message.Should().Contain("Thinking effort: provider default");
        result.Message.Should().Contain("Active model: openai/gpt-oss-20b");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ShowGoogleAiStudioBaseUrl_When_GoogleAiStudioProviderIsConfigured()
    {
        Mock<IUserDataPathProvider> pathProvider = new(MockBehavior.Strict);
        pathProvider
            .Setup(provider => provider.GetConfigurationFilePath())
            .Returns("C:\\Users\\test\\AppData\\Roaming\\NanoAgent\\agent-profile.json");

        ConfigCommandHandler sut = new(pathProvider.Object);
        ReplSessionContext session = new(
            new AgentProviderProfile(ProviderKind.GoogleAiStudio, null),
            "gemini-2.5-flash",
            ["gemini-2.5-flash"],
            agentProfile: BuiltInAgentProfiles.Build);

        ReplCommandResult result = await sut.ExecuteAsync(
            new ReplCommandContext("config", string.Empty, [], "/config", session),
            CancellationToken.None);

        result.Message.Should().Contain("Provider: Google AI Studio");
        result.Message.Should().Contain("Base URL: https://generativelanguage.googleapis.com/v1beta/openai");
        result.Message.Should().Contain("Active model: gemini-2.5-flash");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ShowThinkingEffort_When_Configured()
    {
        Mock<IUserDataPathProvider> pathProvider = new(MockBehavior.Strict);
        pathProvider
            .Setup(provider => provider.GetConfigurationFilePath())
            .Returns("C:\\Users\\test\\AppData\\Roaming\\NanoAgent\\agent-profile.json");

        ConfigCommandHandler sut = new(pathProvider.Object);
        ReplSessionContext session = new(
            new AgentProviderProfile(ProviderKind.OpenAi, null),
            "gpt-5.4",
            ["gpt-5.4"],
            reasoningEffort: "high");

        ReplCommandResult result = await sut.ExecuteAsync(
            new ReplCommandContext("config", string.Empty, [], "/config", session),
            CancellationToken.None);

        result.Message.Should().Contain("Thinking effort: high");
    }
}
