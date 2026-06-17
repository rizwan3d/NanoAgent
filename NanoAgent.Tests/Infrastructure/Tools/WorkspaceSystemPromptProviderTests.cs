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
            configuredSystemPrompt: null,
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
            configuredSystemPrompt: "Base prompt",
            CancellationToken.None);

        result.Should().NotBeNull();
        result.Should().StartWith(ConversationOptions.IdentityDescription);
        result.Should().EndWith("Prefer repository-specific release rules.");
    }

    [Fact]
    public async Task LoadAsync_Should_AppendWorkspacePromptToConfiguredSystemPrompt()
    {
        string nanoAgentDirectory = Path.Combine(_workspaceRoot, ".nanoagent");
        Directory.CreateDirectory(nanoAgentDirectory);
        File.WriteAllText(
            Path.Combine(nanoAgentDirectory, "SystemPrompt-Append.md"),
            "  Follow the workspace deployment checklist.  ");

        WorkspaceSystemPromptProvider sut = new();

        string? result = await sut.LoadAsync(
            CreateSession(),
            configuredSystemPrompt: "Base prompt",
            CancellationToken.None);

        result.Should().Be($"Base prompt{Environment.NewLine}{Environment.NewLine}Follow the workspace deployment checklist.");
    }

    [Fact]
    public async Task LoadAsync_Should_CreateSystemPromptFromAppendFile_When_NoConfiguredPromptExists()
    {
        string nanoAgentDirectory = Path.Combine(_workspaceRoot, ".nanoagent");
        Directory.CreateDirectory(nanoAgentDirectory);
        File.WriteAllText(
            Path.Combine(nanoAgentDirectory, "SystemPrompt-Append.md"),
            "Prefer focused workspace rules.");

        WorkspaceSystemPromptProvider sut = new();

        string? result = await sut.LoadAsync(
            CreateSession(),
            configuredSystemPrompt: null,
            CancellationToken.None);

        result.Should().NotBeNull();
        result.Should().StartWith(ConversationOptions.IdentityDescription);
        result.Should().EndWith("Prefer focused workspace rules.");
    }

    [Fact]
    public async Task LoadAsync_Should_PreferOverrideFile_When_AppendFileAlsoExists()
    {
        string nanoAgentDirectory = Path.Combine(_workspaceRoot, ".nanoagent");
        Directory.CreateDirectory(nanoAgentDirectory);
        File.WriteAllText(
            Path.Combine(nanoAgentDirectory, "SystemPrompt.md"),
            "Use the full override.");
        File.WriteAllText(
            Path.Combine(nanoAgentDirectory, "SystemPrompt-Append.md"),
            "This should be ignored.");

        WorkspaceSystemPromptProvider sut = new();

        string? result = await sut.LoadAsync(
            CreateSession(),
            configuredSystemPrompt: "Base prompt",
            CancellationToken.None);

        result.Should().NotBeNull();
        result.Should().Contain("Use the full override.");
        result.Should().NotContain("This should be ignored.");
        result.Should().NotContain("Base prompt");
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
