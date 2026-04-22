using NanoAgent.Application.Models;
using NanoAgent.Application.Profiles;
using NanoAgent.Application.Repl.Commands;
using NanoAgent.Domain.Models;
using FluentAssertions;

namespace NanoAgent.Tests.Application.Repl.Commands;

public sealed class HelpCommandHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_Should_IncludeActiveProfileInHelpText()
    {
        HelpCommandHandler sut = new();
        ReplSessionContext session = new(
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "gpt-5-mini",
            ["gpt-5-mini"],
            agentProfile: BuiltInAgentProfiles.Plan);

        ReplCommandResult result = await sut.ExecuteAsync(
            new ReplCommandContext("help", string.Empty, [], "/help", session),
            CancellationToken.None);

        result.ExitRequested.Should().BeFalse();
        result.Message.Should().Contain("Active agent profile: plan");
        result.Message.Should().Contain("/profile <name>");
        result.Message.Should().Contain("/thinking [effort|default]");
        result.Message.Should().Contain("--thinking <effort>");
    }
}
