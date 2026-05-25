using FluentAssertions;
using NanoAgent.Application.Commands;
using NanoAgent.Application.Models;
using NanoAgent.Application.Profiles;
using NanoAgent.Domain.Models;

namespace NanoAgent.Tests.Application.Commands;

public sealed class AgentCommandHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_Should_ListAvailableSubagents()
    {
        AgentCommandHandler sut = new(new BuiltInAgentProfileResolver());
        ReplSessionContext session = CreateSession();

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext("agent", "/agent", session),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
        result.Message.Should().NotBeNull();
        result.Message.Should().Contain("Active agent profile: build");
        result.Message.Should().Contain("Available subagents (2):");
        result.Message.Should().Contain("* general");
        result.Message.Should().Contain("* explore");
        result.Message.Should().NotContain("* build");
        result.Message.Should().NotContain("* plan");
        result.Message.Should().NotContain("* review");
        result.Message.Should().Contain("Use @<subagent-name> for a one-turn handoff");
    }

    [Fact]
    public async Task ExecuteAsync_AliasShould_ReturnSameSummaryAsAgentCommand()
    {
        AgentCommandHandler commandHandler = new(new BuiltInAgentProfileResolver());
        AgentAliasCommandHandler aliasHandler = new(new BuiltInAgentProfileResolver());
        ReplSessionContext session = CreateSession();

        ReplCommandResult commandResult = await commandHandler.ExecuteAsync(
            CreateContext("agent", "/agent", session),
            CancellationToken.None);
        ReplCommandResult aliasResult = await aliasHandler.ExecuteAsync(
            CreateContext("a", "/a", session),
            CancellationToken.None);

        aliasResult.Message.Should().Be(commandResult.Message);
        aliasResult.FeedbackKind.Should().Be(commandResult.FeedbackKind);
    }

    private static ReplCommandContext CreateContext(
        string commandName,
        string rawText,
        ReplSessionContext session)
    {
        return new ReplCommandContext(
            commandName,
            string.Empty,
            [],
            rawText,
            session);
    }

    private static ReplSessionContext CreateSession()
    {
        return new ReplSessionContext(
            new AgentProviderProfile(ProviderKind.OpenAi, null),
            "gpt-5-mini",
            ["gpt-5-mini"],
            agentProfile: BuiltInAgentProfiles.Build);
    }
}
