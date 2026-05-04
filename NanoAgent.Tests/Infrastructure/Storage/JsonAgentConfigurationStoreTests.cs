using FluentAssertions;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using NanoAgent.Infrastructure.Storage;

namespace NanoAgent.Tests.Infrastructure.Storage;

public sealed class JsonAgentConfigurationStoreTests : IDisposable
{
    private readonly IReadOnlyList<EnvironmentVariableScope> _environmentVariables;
    private readonly string _tempRoot;

    public JsonAgentConfigurationStoreTests()
    {
        _environmentVariables =
        [
            new("NANOAGENT_BASE_URL", null),
            new("NANOAGENT_MODEL", null),
            new("NANOAGENT_PROVIDER", null),
            new("NANOAGENT_THINKING", null)
        ];

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
    public async Task SaveAsync_ThenLoadAsync_Should_RoundTripAnthropicClaudeAccountConfiguration()
    {
        StubUserDataPathProvider pathProvider = new(_tempRoot);
        JsonAgentConfigurationStore sut = new(pathProvider);
        AgentConfiguration configuration = new(
            new AgentProviderProfile(ProviderKind.AnthropicClaudeAccount, null),
            "claude-sonnet-4-6",
            "on");

        await sut.SaveAsync(configuration, CancellationToken.None);
        AgentConfiguration? loadedConfiguration = await sut.LoadAsync(CancellationToken.None);

        loadedConfiguration.Should().Be(configuration);
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_Should_RoundTripGitHubCopilotConfiguration()
    {
        StubUserDataPathProvider pathProvider = new(_tempRoot);
        JsonAgentConfigurationStore sut = new(pathProvider);
        AgentConfiguration configuration = new(
            new AgentProviderProfile(ProviderKind.GitHubCopilot, null),
            "gpt-5",
            "on");

        await sut.SaveAsync(configuration, CancellationToken.None);
        AgentConfiguration? loadedConfiguration = await sut.LoadAsync(CancellationToken.None);

        loadedConfiguration.Should().Be(configuration);
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_Should_RoundTripOpenRouterConfiguration()
    {
        StubUserDataPathProvider pathProvider = new(_tempRoot);
        JsonAgentConfigurationStore sut = new(pathProvider);
        AgentConfiguration configuration = new(
            new AgentProviderProfile(ProviderKind.OpenRouter, null),
            "openai/gpt-4o",
            "on");

        await sut.SaveAsync(configuration, CancellationToken.None);
        AgentConfiguration? loadedConfiguration = await sut.LoadAsync(CancellationToken.None);

        loadedConfiguration.Should().Be(configuration);
    }

    [Fact]
    public async Task LoadAsync_Should_PreferEnvironmentConfiguration_When_ProviderIsSet()
    {
        using EnvironmentVariableScope provider = new("NANOAGENT_PROVIDER", "openrouter");
        using EnvironmentVariableScope model = new("NANOAGENT_MODEL", " openai/gpt-4o ");
        using EnvironmentVariableScope thinking = new("NANOAGENT_THINKING", "on");
        StubUserDataPathProvider pathProvider = new(_tempRoot);
        await File.WriteAllTextAsync(
            pathProvider.GetConfigurationFilePath(),
            """
            {
              "providerProfile": {
                "providerKind": "OpenAi"
              },
              "preferredModelId": "gpt-5.4"
            }
            """,
            CancellationToken.None);
        JsonAgentConfigurationStore sut = new(pathProvider);

        AgentConfiguration? loadedConfiguration = await sut.LoadAsync(CancellationToken.None);

        loadedConfiguration.Should().Be(new AgentConfiguration(
            new AgentProviderProfile(ProviderKind.OpenRouter, null),
            "openai/gpt-4o",
            "on"));
    }

    [Fact]
    public async Task LoadAsync_Should_CreateCompatibleEnvironmentConfiguration_When_BaseUrlIsSet()
    {
        using EnvironmentVariableScope provider = new("NANOAGENT_PROVIDER", "openai-compatible");
        using EnvironmentVariableScope baseUrl = new("NANOAGENT_BASE_URL", "https://provider.example.com/");
        StubUserDataPathProvider pathProvider = new(_tempRoot);
        JsonAgentConfigurationStore sut = new(pathProvider);

        AgentConfiguration? loadedConfiguration = await sut.LoadAsync(CancellationToken.None);

        loadedConfiguration.Should().Be(new AgentConfiguration(
            new AgentProviderProfile(
                ProviderKind.OpenAiCompatible,
                "https://provider.example.com/v1"),
            PreferredModelId: null,
            ReasoningEffort: null));
    }

    [Fact]
    public async Task LoadAsync_Should_CreateAnthropicClaudeAccountEnvironmentConfiguration_When_ProviderIsSet()
    {
        using EnvironmentVariableScope provider = new("NANOAGENT_PROVIDER", "anthropic-claude-account");
        StubUserDataPathProvider pathProvider = new(_tempRoot);
        JsonAgentConfigurationStore sut = new(pathProvider);

        AgentConfiguration? loadedConfiguration = await sut.LoadAsync(CancellationToken.None);

        loadedConfiguration.Should().Be(new AgentConfiguration(
            new AgentProviderProfile(ProviderKind.AnthropicClaudeAccount, null),
            PreferredModelId: null,
            ReasoningEffort: null));
    }

    [Fact]
    public async Task LoadAsync_Should_CreateGitHubCopilotEnvironmentConfiguration_When_ProviderIsSet()
    {
        using EnvironmentVariableScope provider = new("NANOAGENT_PROVIDER", "github-copilot");
        StubUserDataPathProvider pathProvider = new(_tempRoot);
        JsonAgentConfigurationStore sut = new(pathProvider);

        AgentConfiguration? loadedConfiguration = await sut.LoadAsync(CancellationToken.None);

        loadedConfiguration.Should().Be(new AgentConfiguration(
            new AgentProviderProfile(ProviderKind.GitHubCopilot, null),
            PreferredModelId: null,
            ReasoningEffort: null));
    }

    [Fact]
    public async Task SaveAsync_Should_PreserveMemoryAndMcpProfileSections()
    {
        StubUserDataPathProvider pathProvider = new(_tempRoot);
        await File.WriteAllTextAsync(
            pathProvider.GetConfigurationFilePath(),
            """
            {
              "memory": {
                "requireApprovalForWrites": true,
                "allowAutoFailureObservation": true,
                "allowAutoManualLessons": false,
                "redactSecrets": true,
                "maxEntries": 500,
                "maxPromptChars": 12000,
                "disabled": false
              },
              "mcpServers": {
                "context7": {
                  "command": "npx",
                  "args": ["-y", "@upstash/context7-mcp"]
                }
              }
            }
            """,
            CancellationToken.None);
        JsonAgentConfigurationStore sut = new(pathProvider);
        AgentConfiguration configuration = new(
            new AgentProviderProfile(ProviderKind.OpenAi, null),
            "gpt-5.4",
            "on");

        await sut.SaveAsync(configuration, CancellationToken.None);

        string savedJson = await File.ReadAllTextAsync(
            pathProvider.GetConfigurationFilePath(),
            CancellationToken.None);
        savedJson.Should().Contain("\"memory\"");
        savedJson.Should().Contain("\"mcpServers\"");
        savedJson.Should().Contain("\"context7\"");
        AgentConfiguration? loadedConfiguration = await sut.LoadAsync(CancellationToken.None);
        loadedConfiguration.Should().Be(configuration);
    }

    [Fact]
    public async Task SaveAsync_Should_PreserveUnknownDesktopProfileSection()
    {
        StubUserDataPathProvider pathProvider = new(_tempRoot);
        await File.WriteAllTextAsync(
            pathProvider.GetConfigurationFilePath(),
            """
            {
              "desktop": {
                "workspaces": [
                  {
                    "name": "FinalAgent",
                    "path": "C:\\src\\FinalAgent",
                    "lastOpened": "2026-04-27T10:00:00+05:00"
                  }
                ]
              }
            }
            """,
            CancellationToken.None);
        JsonAgentConfigurationStore sut = new(pathProvider);
        AgentConfiguration configuration = new(
            new AgentProviderProfile(ProviderKind.OpenAi, null),
            "gpt-5.4",
            "on");

        await sut.SaveAsync(configuration, CancellationToken.None);

        string savedJson = await File.ReadAllTextAsync(
            pathProvider.GetConfigurationFilePath(),
            CancellationToken.None);
        savedJson.Should().Contain("\"desktop\"");
        savedJson.Should().Contain("\"workspaces\"");
        savedJson.Should().Contain("\"FinalAgent\"");
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

        foreach (EnvironmentVariableScope environmentVariable in _environmentVariables)
        {
            environmentVariable.Dispose();
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

        public string GetMcpConfigurationFilePath()
        {
            return Path.Combine(_root, "mcp.toml");
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

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previousValue;

        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _previousValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _previousValue);
        }
    }
}
