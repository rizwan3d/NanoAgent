using FluentAssertions;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using NanoAgent.Infrastructure.Configuration;
using NanoAgent.Infrastructure.Tools;

namespace NanoAgent.Tests.Infrastructure.Tools;

public sealed class WorkspaceSystemPromptProviderTests : IDisposable
{
    private readonly string _workspaceRoot;

    public WorkspaceSystemPromptProviderTests()
    {
        _workspaceRoot = Path.Combine(
            Path.GetTempPath(),
            $"NanoAgent-SystemPrompt-{Guid.NewGuid():N}");

        Directory.CreateDirectory(_workspaceRoot);
    }

    [Fact]
    public async Task LoadAsync_Should_ReturnNull_When_SystemPromptFileDoesNotExist()
    {
        WorkspaceSystemPromptProvider sut = new();

        string? result = await sut.LoadAsync(
            CreateSession(),
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_Should_LoadSystemPromptFileWithIdentityHeader()
    {
        string nanoAgentDirectory = Path.Combine(_workspaceRoot, ".nanoagent");
        Directory.CreateDirectory(nanoAgentDirectory);
        File.WriteAllText(
            Path.Combine(nanoAgentDirectory, "SystemPrompt.md"),
            "  Prefer repository-specific release rules.  ");

        WorkspaceSystemPromptProvider sut = new();

        string? result = await sut.LoadAsync(
            CreateSession(),
            CancellationToken.None);

        result.Should().NotBeNull();
        result.Should().StartWith(ConversationOptions.IdentityDescription);
        result.Should().EndWith("Prefer repository-specific release rules.");
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
