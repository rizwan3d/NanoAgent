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
        result.MaxToolRoundsPerTurn.Should().Be(32);
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

        result.SystemPrompt.Should().Contain("- planning_mode:");
        result.SystemPrompt.Should().Contain("Use planning_mode when the task is ambiguous");
        result.SystemPrompt.Should().Contain("- planning_mode");
        result.SystemPrompt.Should().Contain("- update_plan");
        result.SystemPrompt.Should().Contain("publish a live task list");
        result.SystemPrompt.Should().Contain("pending`, `in_progress`, and `completed");
        result.SystemPrompt.Should().Contain("check installed build tools, compilers, SDKs, package managers, or runtimes");
        result.SystemPrompt.Should().Contain("dotnet --info");
        result.SystemPrompt.Should().Contain("Collaboration style:");
        result.SystemPrompt.Should().Contain("Never make the user do work that you can do with the available tools");
        result.SystemPrompt.Should().Contain("Make reasonable assumptions when the safest path is clear");
        result.SystemPrompt.Should().Contain("Persist until the task is handled end-to-end when practical");
        result.SystemPrompt.Should().Contain("Communication style:");
        result.SystemPrompt.Should().Contain("If the user asks for a review, prioritize findings first");
        result.SystemPrompt.Should().Contain("project scaffolding, dependency restore/install");
        result.SystemPrompt.Should().Contain("dotnet build");
        result.SystemPrompt.Should().Contain("npm test");
        result.SystemPrompt.Should().Contain("python -m pytest");
        result.SystemPrompt.Should().Contain("fully specified, non-interactive commands for project scaffolding tools");
        result.SystemPrompt.Should().Contain("npm create vite@latest");
        result.SystemPrompt.Should().Contain("Plan quality standards:");
        result.SystemPrompt.Should().Contain("A Codex-style plan starts with evidence from the repo");
        result.SystemPrompt.Should().Contain("high-quality task list");
        result.SystemPrompt.Should().Contain("validation commands");
        result.SystemPrompt.Should().Contain("visible plan synchronized");
        result.SystemPrompt.Should().Contain("Keep exactly one meaningful task in_progress");
        result.SystemPrompt.Should().Contain("Distinguish verified facts from assumptions or open questions");
        result.SystemPrompt.Should().Contain("If status is InvalidArguments");
        result.SystemPrompt.Should().Contain("call the same tool again");
        result.SystemPrompt.Should().Contain("If apply_patch is rejected for malformed patch text");
        result.SystemPrompt.Should().Contain("final non-empty line is exactly `*** End Patch`");
        result.SystemPrompt.Should().Contain("If status is PermissionDenied");
        result.SystemPrompt.Should().Contain("update_plan: {\"plan\"");
        result.SystemPrompt.Should().Contain("Execution discipline:");
        result.SystemPrompt.Should().Contain("work through it one task at a time");
        result.SystemPrompt.Should().Contain("Codebase hygiene and safety:");
        result.SystemPrompt.Should().Contain("Do not revert unrelated changes you discover in the workspace");
        result.SystemPrompt.Should().Contain("Prefer non-interactive commands whenever possible");
        result.SystemPrompt.Should().Contain("If validation could not be run, say so explicitly");
        result.SystemPrompt.Should().Contain("High-quality plan example:");
        result.SystemPrompt.Should().Contain("Low-quality plan example:");
        result.SystemPrompt.Should().Contain("Never produce a low-quality plan");
        result.SystemPrompt.Should().Contain("Delivery standards:");
        result.SystemPrompt.Should().Contain("Finish with the clearest useful answer, not the longest one");
        result.SystemPrompt.Should().Contain("Do not tell the user to copy, save, or paste files");
        result.SystemPrompt.Should().NotContain("You are NanoAgent in Planning Mode.");
        result.SystemPrompt.Should().NotContain("Do not write files.");
    }
}
