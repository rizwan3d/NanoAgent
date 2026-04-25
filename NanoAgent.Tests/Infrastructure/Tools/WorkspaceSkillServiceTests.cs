using FluentAssertions;
using NanoAgent.Application.Models;
using NanoAgent.Infrastructure.Tools;
using NanoAgent.Domain.Models;

namespace NanoAgent.Tests.Infrastructure.Tools;

public sealed class WorkspaceSkillServiceTests : IDisposable
{
    private readonly string _workspaceRoot;

    public WorkspaceSkillServiceTests()
    {
        _workspaceRoot = Path.Combine(
            Path.GetTempPath(),
            $"NanoAgent-Skills-{Guid.NewGuid():N}");

        Directory.CreateDirectory(_workspaceRoot);
    }

    [Fact]
    public async Task CreateRoutingPromptAsync_Should_IncludeSkillNameAndDescriptionOnly()
    {
        string skillDirectory = Path.Combine(_workspaceRoot, ".nanoagent", "skills", "dotnet");
        Directory.CreateDirectory(skillDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(skillDirectory, "SKILL.md"),
            """
            ---
            name: dotnet-expert
            description: Use for .NET project build, test, and package guidance.
            ---
            This body instruction should load only after the skill triggers.
            """);

        WorkspaceSkillService sut = new();

        string? prompt = await sut.CreateRoutingPromptAsync(
            CreateSession(),
            CancellationToken.None);

        prompt.Should().NotBeNull();
        prompt.Should().Contain("Workspace skills:");
        prompt.Should().Contain("dotnet-expert");
        prompt.Should().Contain("Use for .NET project build, test, and package guidance.");
        prompt.Should().Contain("call `skill_load`");
        prompt.Should().NotContain("This body instruction should load only after the skill triggers.");
    }

    [Fact]
    public async Task LoadAsync_Should_ReturnBodyInstructions_When_SkillNameMatches()
    {
        string skillDirectory = Path.Combine(_workspaceRoot, ".nanoagent", "skills", "frontend");
        Directory.CreateDirectory(skillDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(skillDirectory, "SKILL.md"),
            """
            ---
            description: Use for frontend UX and styling tasks.
            ---
            Follow the app's existing design system and verify responsive layout.
            """);

        WorkspaceSkillService sut = new();

        var result = await sut.LoadAsync(
            CreateSession(),
            "frontend",
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.Name.Should().Be("frontend");
        result.Description.Should().Be("Use for frontend UX and styling tasks.");
        result.Path.Should().Be(".nanoagent/skills/frontend/SKILL.md");
        result.Instructions.Should().Be("Follow the app's existing design system and verify responsive layout.");
        result.WasTruncated.Should().BeFalse();
    }

    [Fact]
    public async Task ListAsync_Should_SupportTopLevelMarkdownSkillFiles()
    {
        string skillDirectory = Path.Combine(_workspaceRoot, ".nanoagent", "skills");
        Directory.CreateDirectory(skillDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(skillDirectory, "code-review.md"),
            """
            ---
            description: Use when reviewing code for regressions.
            ---
            Lead with findings.
            """);

        WorkspaceSkillService sut = new();

        IReadOnlyList<WorkspaceSkillDescriptor> skills = await sut.ListAsync(
            CreateSession(),
            CancellationToken.None);

        skills.Should().ContainSingle();
        skills[0].Name.Should().Be("code-review");
        skills[0].Description.Should().Be("Use when reviewing code for regressions.");
        skills[0].Path.Should().Be(".nanoagent/skills/code-review.md");
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
        {
            Directory.Delete(_workspaceRoot, recursive: true);
        }
    }

    private ReplSessionContext CreateSession()
    {
        return new ReplSessionContext(
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "gpt-5-mini",
            ["gpt-5-mini"],
            workspacePath: _workspaceRoot);
    }
}
