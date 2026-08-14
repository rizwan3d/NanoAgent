using FluentAssertions;
using StemCode.Application.Abstractions;
using StemCode.Application.Models;
using StemCode.Application.Profiles;
using StemCode.Application.Utilities;
using StemCode.Domain.Models;
using StemCode.Infrastructure.Tools;

namespace StemCode.Tests.Infrastructure.Tools;

[Collection(global::StemCode.Tests.TestCollections.SecretRedactorState)]
public sealed class WorkspaceAgentProfilePromptProviderTests : IDisposable
{
    private readonly string _workspaceRoot;

    public WorkspaceAgentProfilePromptProviderTests()
    {
        _workspaceRoot = Path.Combine(
            Path.GetTempPath(),
            $"StemCode-ProfilePrompt-{Guid.NewGuid():N}");

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
        bool originalValue = SecretRedactor.IsEnabled;
        SecretRedactor.IsEnabled = true;

        try
        {
            string agentsDirectory = Path.Combine(_workspaceRoot, ".stemcode", "agents");
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
        finally
        {
            SecretRedactor.IsEnabled = originalValue;
        }
    }

    [Fact]
    public async Task LoadAsync_Should_LoadPromptByFrontMatterName()
    {
        string agentsDirectory = Path.Combine(_workspaceRoot, ".stemcode", "agents");
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
            "StemCode",
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "gpt-5-mini",
            ["gpt-5-mini"],
            workspacePath: _workspaceRoot,
            agentProfile: profile);
    }
}
