using FluentAssertions;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Profiles;
using NanoAgent.Domain.Models;
using NanoAgent.Infrastructure.Tools;

namespace NanoAgent.Tests.Infrastructure.Tools;

public sealed class WorkspaceAgentProfilePromptProviderTests : IDisposable
{
    private readonly string _workspaceRoot;

    public WorkspaceAgentProfilePromptProviderTests()
    {
        _workspaceRoot = Path.Combine(
            Path.GetTempPath(),
            $"NanoAgent-ProfilePrompt-{Guid.NewGuid():N}");

        Directory.CreateDirectory(_workspaceRoot);
    }

    [Fact]
    public async Task LoadAsync_Should_ReturnNull_When_ProfilePromptFileDoesNotExist()
    {
        WorkspaceAgentProfilePromptProvider sut = new();

        string? result = await sut.LoadAsync(
            CreateSession(BuiltInAgentProfiles.Build),
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_Should_LoadWorkspacePromptForActiveProfile()
    {
        string agentsDirectory = Path.Combine(_workspaceRoot, ".nanoagent", "agents");
        Directory.CreateDirectory(agentsDirectory);
        File.WriteAllText(
            Path.Combine(agentsDirectory, "build.md"),
            "  Prefer workspace build rules. api_key=test-secret-value  ");

        WorkspaceAgentProfilePromptProvider sut = new();

        string? result = await sut.LoadAsync(
            CreateSession(BuiltInAgentProfiles.Build),
            CancellationToken.None);

        result.Should().Be("Prefer workspace build rules. api_key=<redacted>");
    }

    [Fact]
    public async Task LoadAsync_Should_LoadPromptByFrontMatterName()
    {
        string agentsDirectory = Path.Combine(_workspaceRoot, ".nanoagent", "agents");
        Directory.CreateDirectory(agentsDirectory);
        File.WriteAllText(
            Path.Combine(agentsDirectory, "workspace-review.md"),
            """
            ---
            name: review
            ---
            Use workspace review standards.
            """);

        WorkspaceAgentProfilePromptProvider sut = new();

        string? result = await sut.LoadAsync(
            CreateSession(BuiltInAgentProfiles.Review),
            CancellationToken.None);

        result.Should().Be("Use workspace review standards.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
        {
            Directory.Delete(_workspaceRoot, recursive: true);
        }
    }

    private ReplSessionContext CreateSession(IAgentProfile profile)
    {
        return new ReplSessionContext(
            "NanoAgent",
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "gpt-5-mini",
            ["gpt-5-mini"],
            workspacePath: _workspaceRoot,
            agentProfile: profile);
    }
}
