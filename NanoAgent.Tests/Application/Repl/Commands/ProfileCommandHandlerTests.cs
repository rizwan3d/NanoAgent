using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Profiles;
using NanoAgent.Application.Repl.Commands;
using NanoAgent.Domain.Models;
using FluentAssertions;

namespace NanoAgent.Tests.Application.Repl.Commands;

public sealed class ProfileCommandHandlerTests
{
    private readonly ProfileCommandHandler _sut = new(new BuiltInAgentProfileResolver());

    [Fact]
    public async Task ExecuteAsync_Should_ShowCurrentAndAvailableProfiles_When_ProfileArgumentIsMissing()
    {
        ReplSessionContext session = CreateSession();

        ReplCommandResult result = await _sut.ExecuteAsync(
            new ReplCommandContext("profile", string.Empty, [], "/profile", session),
            CancellationToken.None);

        result.ExitRequested.Should().BeFalse();
        result.Message.Should().Contain("Active agent profile: build");
        result.Message.Should().Contain("Available profiles (5):");
        result.Message.Should().Contain("* build (active) [primary]");
        result.Message.Should().Contain("* plan");
        result.Message.Should().Contain("* review");
        result.Message.Should().Contain("* general");
        result.Message.Should().Contain("* explore");
        result.Message.Should().Contain("Use /profile <name> to switch profiles for this session");
        result.Message.Should().Contain("@general or @explore");
    }

    [Fact]
    public async Task ExecuteAsync_Should_SwitchActiveProfile_When_ProfileExists()
    {
        ReplSessionContext session = CreateSession();

        ReplCommandResult result = await _sut.ExecuteAsync(
            new ReplCommandContext("profile", "plan", ["plan"], "/profile plan", session),
            CancellationToken.None);

        session.AgentProfile.Name.Should().Be(BuiltInAgentProfiles.PlanName);
        session.IsPersistedStateDirty.Should().BeTrue();
        result.Message.Should().Be("Active agent profile switched to 'plan'. Subsequent prompts in this session will use the 'plan' profile.");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnAlreadyUsingMessage_When_ProfileIsAlreadyActive()
    {
        ReplSessionContext session = CreateSession(agentProfile: BuiltInAgentProfiles.Review);

        ReplCommandResult result = await _sut.ExecuteAsync(
            new ReplCommandContext("profile", "review", ["review"], "/profile review", session),
            CancellationToken.None);

        session.AgentProfile.Name.Should().Be(BuiltInAgentProfiles.ReviewName);
        session.IsPersistedStateDirty.Should().BeFalse();
        result.Message.Should().Be("Already using 'review'.");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnError_When_ProfileDoesNotExist()
    {
        ReplSessionContext session = CreateSession();

        ReplCommandResult result = await _sut.ExecuteAsync(
            new ReplCommandContext("profile", "ops", ["ops"], "/profile ops", session),
            CancellationToken.None);

        session.AgentProfile.Name.Should().Be(BuiltInAgentProfiles.BuildName);
        result.FeedbackKind.Should().Be(ReplFeedbackKind.Error);
        result.Message.Should().Be("Unknown agent profile 'ops'. Available profiles: build, plan, review, general, explore.");
    }

    private static ReplSessionContext CreateSession(IAgentProfile? agentProfile = null)
    {
        return new ReplSessionContext(
            new AgentProviderProfile(ProviderKind.OpenAi, null),
            "gpt-5-mini",
            ["gpt-5-mini"],
            agentProfile: agentProfile);
    }
}
