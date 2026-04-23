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
            "on");

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
            "on");

        await sut.SaveAsync(configuration, CancellationToken.None);
        AgentConfiguration? loadedConfiguration = await sut.LoadAsync(CancellationToken.None);

        loadedConfiguration.Should().Be(configuration);
    }

    [Fact]
    public async Task LoadAsync_Should_NormalizeCompatibleProviderBaseUrl()
    {
        StubUserDataPathProvider pathProvider = new(_tempRoot);
        JsonAgentConfigurationStore sut = new(pathProvider);

        await File.WriteAllTextAsync(
            pathProvider.GetConfigurationFilePath(),
            """
            {
              "providerProfile": {
                "providerKind": 2,
                "baseUrl": "https://provider.example.com/"
              },
              "preferredModelId": "gpt-4.1"
            }
            """,
            CancellationToken.None);

        AgentConfiguration? loadedConfiguration = await sut.LoadAsync(CancellationToken.None);

        loadedConfiguration.Should().NotBeNull();
        loadedConfiguration!.ProviderProfile.Should().Be(
            new AgentProviderProfile(
                ProviderKind.OpenAiCompatible,
                "https://provider.example.com/v1"));
    }

    [Fact]
    public async Task LoadAsync_Should_ReturnNull_When_CompatibleProviderBaseUrlIsInvalid()
    {
        StubUserDataPathProvider pathProvider = new(_tempRoot);
        JsonAgentConfigurationStore sut = new(pathProvider);

        await File.WriteAllTextAsync(
            pathProvider.GetConfigurationFilePath(),
            """
            {
              "providerProfile": {
                "providerKind": 2,
                "baseUrl": "not-a-url"
              }
            }
            """,
            CancellationToken.None);

        AgentConfiguration? loadedConfiguration = await sut.LoadAsync(CancellationToken.None);

        loadedConfiguration.Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_Should_ClearLegacyThinkingValues()
    {
        StubUserDataPathProvider pathProvider = new(_tempRoot);
        JsonAgentConfigurationStore sut = new(pathProvider);

        await File.WriteAllTextAsync(
            pathProvider.GetConfigurationFilePath(),
            """
            {
              "providerProfile": {
                "providerKind": 1
              },
              "preferredModelId": "gpt-5.4",
              "reasoningEffort": "high"
            }
            """,
            CancellationToken.None);

        AgentConfiguration? loadedConfiguration = await sut.LoadAsync(CancellationToken.None);

        loadedConfiguration.Should().NotBeNull();
        loadedConfiguration!.ReasoningEffort.Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_Should_ReturnNull_When_ConfigurationJsonIsMalformed()
    {
        StubUserDataPathProvider pathProvider = new(_tempRoot);
        JsonAgentConfigurationStore sut = new(pathProvider);

        await File.WriteAllTextAsync(
            pathProvider.GetConfigurationFilePath(),
            "{",
            CancellationToken.None);

        AgentConfiguration? loadedConfiguration = await sut.LoadAsync(CancellationToken.None);

        loadedConfiguration.Should().BeNull();
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
