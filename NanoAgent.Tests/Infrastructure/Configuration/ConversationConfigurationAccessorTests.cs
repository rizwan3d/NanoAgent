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
    public void GetSettings_Should_IncludePlanningModeGuidance_InDefaultSystemPrompt()
    {
        IOptions<ApplicationOptions> options = Options.Create(new ApplicationOptions
        {
            Conversation = new ConversationOptions()
        });

        ConversationConfigurationAccessor sut = new(options);

        ConversationSettings result = sut.GetSettings();

        result.SystemPrompt.Should().Contain("Use planning_mode when the task is ambiguous");
        result.SystemPrompt.Should().Contain("call `planning_mode` before implementation");
        result.SystemPrompt.Should().Contain("When you need to plan first, call `planning_mode`");
        result.SystemPrompt.Should().Contain("For plan-first work, start by calling `planning_mode`");
        result.SystemPrompt.Should().Contain("publish a live task list");
        result.SystemPrompt.Should().Contain("Make reasonable assumptions when the safest path is clear");
        result.SystemPrompt.Should().Contain("Persist until the task is handled end-to-end when practical");
        result.SystemPrompt.Should().Contain("fully specified, non-interactive commands for project scaffolding tools");
        result.SystemPrompt.Should().Contain("project scaffolding, dependency restore/install");
        result.SystemPrompt.Should().Contain("Use `web_run` when current external facts or documentation are required");
        result.SystemPrompt.Should().Contain("Before using unfamiliar build tools, frameworks, libraries, SDKs, or APIs");
        result.SystemPrompt.Should().Contain("official documentation or domain references");
        result.SystemPrompt.Should().Contain("- planning_mode:");
        result.SystemPrompt.Should().Contain("- update_plan:");
        result.SystemPrompt.Should().Contain("- web_run:");
        result.SystemPrompt.Should().Contain("- shell_command:");
        result.SystemPrompt.Should().NotContain("You are NanoAgent in Planning Mode.");
        result.SystemPrompt.Should().NotContain("Do not write files.");
    }
}
