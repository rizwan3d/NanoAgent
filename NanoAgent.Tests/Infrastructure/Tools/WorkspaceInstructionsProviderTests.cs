using FluentAssertions;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using NanoAgent.Infrastructure.Tools;

namespace NanoAgent.Tests.Infrastructure.Tools;

public sealed class WorkspaceInstructionsProviderTests : IDisposable
{
    private readonly string _workspaceRoot;

    public WorkspaceInstructionsProviderTests()
    {
        _workspaceRoot = Path.Combine(
            Path.GetTempPath(),
            $"NanoAgent-Instructions-{Guid.NewGuid():N}");

        Directory.CreateDirectory(_workspaceRoot);
    }

    [Fact]
    public async Task LoadAsync_Should_ReturnNull_When_NoInstructionFilesExist()
    {
        WorkspaceInstructionsProvider sut = new();

        string? result = await sut.LoadAsync(
            CreateSession(),
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_Should_LoadRootAndAgentDirectoryInstructionFiles()
    {
        File.WriteAllText(
            Path.Combine(_workspaceRoot, "AGENTS.md"),
            "Use repo conventions.");
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, ".agent"));
        File.WriteAllText(
            Path.Combine(_workspaceRoot, ".agent", "AGENTS.md"),
            "Prefer focused changes.");

        WorkspaceInstructionsProvider sut = new();

        string? result = await sut.LoadAsync(
            CreateSession(),
            CancellationToken.None);

        result.Should().NotBeNull();
        result.Should().Contain("Workspace instructions:");
        result.Should().Contain("<workspace_instruction path=\"AGENTS.md\">");
        result.Should().Contain("Use repo conventions.");
        result.Should().Contain("<workspace_instruction path=\".agent/AGENTS.md\">");
        result.Should().Contain("Prefer focused changes.");
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
            "NanoAgent",
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "gpt-5-mini",
            ["gpt-5-mini"],
            workspacePath: _workspaceRoot);
    }
}
