using NanoAgent.Application.Models;
using NanoAgent.Presentation.Repl.Commands;
using NanoAgent.Domain.Models;
using FluentAssertions;

namespace NanoAgent.Tests.Application.Repl.Commands;

public sealed class PermissionCommandHandlerTests
{
    [Fact]
    public async Task PermissionsCommand_Should_ShowSummaryAndExamples()
    {
        PermissionsCommandHandler sut = new(new PermissionSettings
        {
            DefaultMode = PermissionMode.Ask,
            Rules =
            [
                new PermissionRule
                {
                    Mode = PermissionMode.Allow,
                    Tools = ["read"]
                }
            ]
        });
        ReplSessionContext session = CreateSession();
        session.AddPermissionOverride(new PermissionRule
        {
            Mode = PermissionMode.Deny,
            Tools = ["edit"],
            Patterns = ["src/**"]
        });

        ReplCommandResult result = await sut.ExecuteAsync(
            new ReplCommandContext("permissions", string.Empty, [], "/permissions", session),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
        result.Message.Should().Contain("Default mode: Ask");
        result.Message.Should().Contain("Built-in/configured rules: 1");
        result.Message.Should().Contain("Session overrides: 1");
        result.Message.Should().Contain("/allow edit src/**");
        result.Message.Should().Contain("/rules");
    }

    [Fact]
    public async Task RulesCommand_Should_ListConfiguredRulesAndSessionOverrides()
    {
        RulesCommandHandler sut = new(new PermissionSettings
        {
            DefaultMode = PermissionMode.Ask,
            Rules =
            [
                new PermissionRule
                {
                    Mode = PermissionMode.Allow,
                    Tools = ["read"]
                }
            ]
        });
        ReplSessionContext session = CreateSession();
        session.AddPermissionOverride(new PermissionRule
        {
            Mode = PermissionMode.Deny,
            Tools = ["edit"],
            Patterns = ["src/**"]
        });

        ReplCommandResult result = await sut.ExecuteAsync(
            new ReplCommandContext("rules", string.Empty, [], "/rules", session),
            CancellationToken.None);

        result.Message.Should().Contain("Built-in and configured rules:");
        result.Message.Should().Contain("Allow | tools: read | patterns: *");
        result.Message.Should().Contain("Session overrides:");
        result.Message.Should().Contain("Deny | tools: edit | patterns: src/**");
    }

    [Fact]
    public async Task AllowCommand_Should_AddPatternScopedSessionOverride()
    {
        AllowCommandHandler sut = new();
        ReplSessionContext session = CreateSession();

        ReplCommandResult result = await sut.ExecuteAsync(
            new ReplCommandContext("allow", "edit src/App.js", ["edit", "src/App.js"], "/allow edit src/App.js", session),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
        result.Message.Should().Contain("session allow rule");
        session.PermissionOverrides.Should().ContainSingle();
        session.PermissionOverrides[0].Mode.Should().Be(PermissionMode.Allow);
        session.PermissionOverrides[0].Tools.Should().Equal("edit");
        session.PermissionOverrides[0].Patterns.Should().Equal("src/App.js");
    }

    [Fact]
    public async Task DenyCommand_Should_AddToolWideSessionOverride_When_NoPatternIsProvided()
    {
        DenyCommandHandler sut = new();
        ReplSessionContext session = CreateSession();

        ReplCommandResult result = await sut.ExecuteAsync(
            new ReplCommandContext("deny", "bash", ["bash"], "/deny bash", session),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
        result.Message.Should().Contain("across all targets");
        session.PermissionOverrides.Should().ContainSingle();
        session.PermissionOverrides[0].Mode.Should().Be(PermissionMode.Deny);
        session.PermissionOverrides[0].Tools.Should().Equal("bash");
        session.PermissionOverrides[0].Patterns.Should().BeEmpty();
    }

    [Fact]
    public async Task AllowCommand_Should_ReturnUsageError_When_ToolPatternIsMissing()
    {
        AllowCommandHandler sut = new();
        ReplSessionContext session = CreateSession();

        ReplCommandResult result = await sut.ExecuteAsync(
            new ReplCommandContext("allow", string.Empty, [], "/allow", session),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Error);
        result.Message.Should().Be("Usage: /allow <tool-or-tag> [pattern]");
    }

    private static ReplSessionContext CreateSession()
    {
        return new ReplSessionContext(
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "gpt-5-mini",
            ["gpt-5-mini"]);
    }
}
