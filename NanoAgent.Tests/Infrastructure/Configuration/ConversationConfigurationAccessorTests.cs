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

        result.SystemPrompt.Should().Contain("Use `planning_mode` when you need a plan-first pass");
        result.SystemPrompt.Should().Contain("When you intentionally want a plan-first pass, call `planning_mode`");
        result.SystemPrompt.Should().Contain("use `update_plan` to publish a live task list");
        result.SystemPrompt.Should().Contain("Make reasonable assumptions when the safest path is clear");
        result.SystemPrompt.Should().Contain("Persist until the task is handled end-to-end when practical");
        result.SystemPrompt.Should().Contain("fully specified, non-interactive commands for project scaffolding tools");
        result.SystemPrompt.Should().Contain("Use `web_run` when current external facts or documentation are required");
        result.SystemPrompt.Should().Contain("Before using unfamiliar build tools, frameworks, libraries, SDKs, or APIs");
        result.SystemPrompt.Should().Contain("Use configured `mcp__*` tools");
        result.SystemPrompt.Should().Contain("Use configured `custom__*` tools");
        result.SystemPrompt.Should().Contain("official documentation or domain references");
        result.SystemPrompt.Should().Contain("Sandbox enforcement: unsupported");
        result.SystemPrompt.Should().Contain("without OS-level sandbox enforcement");
        result.SystemPrompt.Should().Contain("- planning_mode:");
        result.SystemPrompt.Should().Contain("- update_plan:");
        result.SystemPrompt.Should().Contain("- file_delete:");
        result.SystemPrompt.Should().Contain("- web_run:");
        result.SystemPrompt.Should().Contain("- mcp__*:");
        result.SystemPrompt.Should().Contain("- custom__*:");
        result.SystemPrompt.Should().Contain("- shell_command:");
        result.SystemPrompt.Should().Contain("- code_intelligence:");
        result.SystemPrompt.Should().Contain("document symbols, definitions, references, or hover details");
        result.SystemPrompt.Should().Contain("Developed by: Rizwan3D");
        result.SystemPrompt.Should().NotContain("Always use planning_mode for tasks.");
        result.SystemPrompt.Should().NotContain("You are NanoAgent in Planning Mode.");
        result.SystemPrompt.Should().NotContain("Do not write files.");
    }
}
