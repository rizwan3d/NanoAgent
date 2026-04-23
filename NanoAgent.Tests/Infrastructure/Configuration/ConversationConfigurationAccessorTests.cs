using NanoAgent.Application.Models;
using NanoAgent.Infrastructure.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace NanoAgent.Tests.Infrastructure.Configuration;

public sealed class ConversationConfigurationAccessorTests
{
    [Fact]
    public void GetSettings_Should_ReturnInfiniteTimeout_When_RequestTimeoutSecondsIsZero()
    {
        IOptions<ApplicationOptions> options = Options.Create(new ApplicationOptions
        {
            Conversation = new ConversationOptions
            {
                RequestTimeoutSeconds = 0,
                SystemPrompt = "  test prompt  "
            }
        });

        ConversationConfigurationAccessor sut = new(options);

        ConversationSettings result = sut.GetSettings();

        result.SystemPrompt.Should().Be("test prompt");
        result.RequestTimeout.Should().Be(Timeout.InfiniteTimeSpan);
        result.MaxToolRoundsPerTurn.Should().Be(0);
    }

    [Fact]
    public void GetSettings_Should_ReturnConciseDefaultSystemPrompt()
    {
        IOptions<ApplicationOptions> options = Options.Create(new ApplicationOptions
        {
            Conversation = new ConversationOptions()
        });

        ConversationConfigurationAccessor sut = new(options);

        ConversationSettings result = sut.GetSettings();

        result.SystemPrompt.Should().Contain("SYSTEM NAME: NanoAgent");
        result.SystemPrompt.Should().Contain("Operating rules:");
        result.SystemPrompt.Should().Contain("Inspect the actual workspace before changing existing behavior.");
        result.SystemPrompt.Should().Contain("planning_mode");
        result.SystemPrompt.Should().Contain("update_plan");
        result.SystemPrompt.Should().Contain("at most one task `in_progress`");
        result.SystemPrompt.Should().Contain("shell_command");
        result.SystemPrompt.Should().Contain("dotnet build");
        result.SystemPrompt.Should().Contain("npm test");
        result.SystemPrompt.Should().Contain("python -m pytest");
        result.SystemPrompt.Should().Contain("fully specified, non-interactive scaffold");
        result.SystemPrompt.Should().Contain("apply_patch");
        result.SystemPrompt.Should().Contain("file_write");
        result.SystemPrompt.Should().Contain("web_search");
        result.SystemPrompt.Should().Contain("Do not revert unrelated workspace changes.");
        result.SystemPrompt.Should().Contain("For review requests, lead with bugs");
        result.SystemPrompt.Should().Contain("what changed, how it was validated, and any remaining risk");
        result.SystemPrompt.Should().NotContain("High-quality plan example:");
        result.SystemPrompt.Should().NotContain("Low-quality plan example:");
        result.SystemPrompt.Should().NotContain("You are NanoAgent in Planning Mode.");
        result.SystemPrompt.Should().NotContain("Do not write files.");
    }
}
