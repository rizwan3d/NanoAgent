using System.Net;
using System.Text.Json;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Permissions;
using NanoAgent.Application.Tools.Services;
using NanoAgent.Domain.Models;
using NanoAgent.Infrastructure.Plugins;
using NanoAgent.Infrastructure.Plugins.GitHub;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace NanoAgent.Tests.Infrastructure.Plugins.GitHub;

public sealed class GitHubPluginToolTests : IDisposable
{
    private const string TokenEnvironmentVariable = "NANOAGENT_TEST_GITHUB_TOKEN";
    private readonly string _tempRoot;

    public GitHubPluginToolTests()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"NanoAgent-GitHubPlugin-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public void Provider_Should_RegisterGitHubPluginToolsByDefault()
    {
        PluginDynamicProvider provider = CreateProvider(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)));

        provider.GetTools()
            .Select(static tool => tool.Name)
            .Should()
            .Equal(
                "plugin__github__repository",
                "plugin__github__issue",
                "plugin__github__pull_request");
        provider.GetStatuses().Should().ContainSingle(status =>
            status.Name == "github" &&
            status.Kind == "plugin" &&
            status.IsAvailable &&
            status.ToolCount == 3);

        ToolRegistry registry = new(
            [],
            new ToolPermissionParser(),
            [provider]);
        registry.TryResolve("plugin__github__repository", out ToolRegistration? registration)
            .Should()
            .BeTrue();
        registration!.PermissionPolicy.WebRequest!.RequestArgumentName.Should().Be("repository");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReadRepositoryMetadata()
    {
        StubHttpMessageHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "full_name": "octocat/Hello-World",
                  "description": "A test repository.",
                  "default_branch": "main",
                  "html_url": "https://github.com/octocat/Hello-World"
                }
                """)
        });
        GitHubPluginTool tool = CreateTool(handler, GitHubPluginToolKind.Repository);

        ToolResult result = await tool.ExecuteAsync(
            CreateContext(tool.Name, """{ "repository": "octocat/Hello-World" }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        result.Message.Should().Be("Loaded GitHub repository 'octocat/Hello-World'.");
        result.JsonResult.Should().Contain("\"full_name\":\"octocat/Hello-World\"");
        result.RenderPayload!.Text.Should().Contain("A test repository.");
        handler.LastRequest!.RequestUri!.AbsoluteUri.Should().Be("https://api.github.com/repos/octocat/Hello-World");
    }

    [Fact]
    public async Task ExecuteAsync_Should_UseConfiguredBaseUrlAndTokenEnvironmentVariable()
    {
        Environment.SetEnvironmentVariable(TokenEnvironmentVariable, "ghp_testtoken1234567890");
        StubHttpMessageHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "title": "Fix bug",
                  "state": "open",
                  "html_url": "https://github.example.com/acme/project/issues/12",
                  "user": {
                    "login": "mona"
                  }
                }
                """)
        });
        PluginConfiguration configuration = new("github")
        {
            ApprovalMode = "auto"
        };
        configuration.Settings["apiBaseUrl"] = "https://github.example.com/api/v3";
        configuration.Settings["tokenEnvVar"] = TokenEnvironmentVariable;
        GitHubPluginTool tool = CreateTool(handler, GitHubPluginToolKind.Issue, configuration);

        ToolResult result = await tool.ExecuteAsync(
            CreateContext(tool.Name, """{ "repository": "acme/project", "number": 12 }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        handler.LastRequest!.RequestUri!.AbsoluteUri.Should().Be("https://github.example.com/api/v3/repos/acme/project/issues/12");
        handler.LastRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.Should().Be("ghp_testtoken1234567890");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnInvalidArgumentsForBadRepository()
    {
        GitHubPluginTool tool = CreateTool(
            new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)),
            GitHubPluginToolKind.PullRequest);

        ToolResult result = await tool.ExecuteAsync(
            CreateContext(tool.Name, """{ "repository": "missing-owner", "number": 1 }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.InvalidArguments);
        result.Message.Should().Contain("owner/name");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(TokenEnvironmentVariable, null);

        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private PluginDynamicProvider CreateProvider(StubHttpMessageHandler handler)
    {
        return new PluginDynamicProvider(
            [new GitHubPluginToolFactory(new StubHttpClientFactory(handler))],
            new StubUserDataPathProvider(Path.Combine(_tempRoot, "appdata", "NanoAgent", "agent-profile.json")),
            new StubWorkspaceRootProvider(_tempRoot),
            NullLogger<PluginDynamicProvider>.Instance);
    }

    private GitHubPluginTool CreateTool(
        StubHttpMessageHandler handler,
        GitHubPluginToolKind kind,
        PluginConfiguration? configuration = null)
    {
        return new GitHubPluginTool(
            configuration ?? new PluginConfiguration("github"),
            new StubHttpClientFactory(handler),
            kind);
    }

    private ToolExecutionContext CreateContext(
        string toolName,
        string argumentsJson)
    {
        using JsonDocument document = JsonDocument.Parse(argumentsJson);
        return new ToolExecutionContext(
            "call_1",
            toolName,
            document.RootElement.Clone(),
            new ReplSessionContext(
                new AgentProviderProfile(
                    ProviderKind.OpenAi,
                    null),
                "gpt-5-mini",
                ["gpt-5-mini"],
                workspacePath: _tempRoot));
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            name.Should().Be("NanoAgent.Plugins.GitHub");
            return new HttpClient(_handler, disposeHandler: false);
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_handler(request));
        }
    }

    private sealed class StubWorkspaceRootProvider : IWorkspaceRootProvider
    {
        private readonly string _workspaceRoot;

        public StubWorkspaceRootProvider(string workspaceRoot)
        {
            _workspaceRoot = workspaceRoot;
        }

        public string GetWorkspaceRoot()
        {
            return _workspaceRoot;
        }
    }

    private sealed class StubUserDataPathProvider : IUserDataPathProvider
    {
        private readonly string _profilePath;

        public StubUserDataPathProvider(string profilePath)
        {
            _profilePath = profilePath;
        }

        public string GetConfigurationFilePath()
        {
            return _profilePath;
        }

        public string GetMcpConfigurationFilePath()
        {
            return _profilePath;
        }

        public string GetLogsDirectoryPath()
        {
            return Path.Combine(Path.GetDirectoryName(_profilePath)!, "logs");
        }

        public string GetSectionsDirectoryPath()
        {
            return Path.Combine(Path.GetDirectoryName(_profilePath)!, "sections");
        }
    }
}
