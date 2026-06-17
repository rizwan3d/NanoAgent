using FluentAssertions;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Profiles;

namespace NanoAgent.Tests.Application.Profiles;

public sealed class WorkspaceAgentProfileLoaderTests : IDisposable
{
    private readonly string _workspaceRoot;

    public WorkspaceAgentProfileLoaderTests()
    {
        _workspaceRoot = Path.Combine(
            Path.GetTempPath(),
            $"NanoAgent-ProfileLoader-{Guid.NewGuid():N}");

        Directory.CreateDirectory(_workspaceRoot);
    }

    [Theory]
    [InlineData("toolOutput", "full", true)]
    [InlineData("toolOutput", "complete", true)]
    [InlineData("toolOutput", "compact", false)]
    [InlineData("toolOutput", "preview", false)]
    [InlineData("fileOutput", "full", true)]
    [InlineData("fileOutput", "compact", false)]
    public void Load_Should_ParseToolOutputPreference(string key, string value, bool expected)
    {
        IAgentProfile profile = LoadSingleProfile(
            $"""
            ---
            name: reader
            mode: primary
            {key}: {value}
            ---
            Inspect carefully.
            """);

        profile.FullToolOutput.Should().Be(expected);
    }

    [Fact]
    public void Load_Should_DefaultToolOutputPreferenceToNull_WhenMetadataMissing()
    {
        IAgentProfile profile = LoadSingleProfile(
            """
            ---
            name: reader
            mode: primary
            ---
            Inspect carefully.
            """);

        profile.FullToolOutput.Should().BeNull();
    }

    private IAgentProfile LoadSingleProfile(string content)
    {
        string agentsDirectory = Path.Combine(_workspaceRoot, ".nanoagent", "agents");
        Directory.CreateDirectory(agentsDirectory);
        File.WriteAllText(Path.Combine(agentsDirectory, "reader.md"), content);

        IReadOnlyList<IAgentProfile> profiles = WorkspaceAgentProfileLoader.Load(_workspaceRoot);
        profiles.Should().ContainSingle();
        return profiles[0];
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
        {
            Directory.Delete(_workspaceRoot, recursive: true);
        }
    }
}
