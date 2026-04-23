using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using NanoAgent.Infrastructure.Storage;
using FluentAssertions;

namespace NanoAgent.Tests.Infrastructure.Storage;

public sealed class JsonAgentConfigurationStoreTests : IDisposable
{
    private readonly string _tempRoot;

    public JsonAgentConfigurationStoreTests()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"NanoAgent-Config-{Guid.NewGuid():N}");

        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_Should_RoundTripGoogleAiStudioConfiguration()
    {
        StubUserDataPathProvider pathProvider = new(_tempRoot);
        JsonAgentConfigurationStore sut = new(pathProvider);
        AgentConfiguration configuration = new(
            new AgentProviderProfile(ProviderKind.GoogleAiStudio, null),
            "gemini-2.5-flash",
            "high");

        await sut.SaveAsync(configuration, CancellationToken.None);
        AgentConfiguration? loadedConfiguration = await sut.LoadAsync(CancellationToken.None);

        loadedConfiguration.Should().Be(configuration);
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_Should_RoundTripAnthropicConfiguration()
    {
        StubUserDataPathProvider pathProvider = new(_tempRoot);
        JsonAgentConfigurationStore sut = new(pathProvider);
        AgentConfiguration configuration = new(
            new AgentProviderProfile(ProviderKind.Anthropic, null),
            "claude-sonnet-4-6",
            "high");

        await sut.SaveAsync(configuration, CancellationToken.None);
        AgentConfiguration? loadedConfiguration = await sut.LoadAsync(CancellationToken.None);

        loadedConfiguration.Should().Be(configuration);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private sealed class StubUserDataPathProvider : IUserDataPathProvider
    {
        private readonly string _root;

        public StubUserDataPathProvider(string root)
        {
            _root = root;
        }

        public string GetConfigurationFilePath()
        {
            return Path.Combine(_root, "agent-profile.json");
        }

        public string GetLogsDirectoryPath()
        {
            return Path.Combine(_root, "logs");
        }

        public string GetSectionsDirectoryPath()
        {
            return Path.Combine(_root, "sections");
        }
    }
}
