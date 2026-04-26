using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Permissions;
using NanoAgent.Application.Tools.Services;
using NanoAgent.Infrastructure.CustomTools;
using NanoAgent.Infrastructure.Secrets;
using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;

namespace NanoAgent.Tests.Infrastructure.CustomTools;

public sealed class CustomToolDynamicProviderTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _userProfilePath;
    private readonly string _workspaceRoot;

    public CustomToolDynamicProviderTests()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"NanoAgent-CustomTools-{Guid.NewGuid():N}");
        _workspaceRoot = Path.Combine(_tempRoot, "workspace");
        _userProfilePath = Path.Combine(_tempRoot, "appdata", "NanoAgent", "agent-profile.json");
        Directory.CreateDirectory(_workspaceRoot);
    }

    [Fact]
    public void GetTools_Should_LoadConfiguredCustomTools()
    {
        WriteFile(
            Path.Combine(_workspaceRoot, ".nanoagent", "agent-profile.json"),
            """
            {
              "customTools": {
                "word_count": {
                  "description": "Count words.",
                  "command": "python",
                  "args": [".nanoagent/tools/word_count.py"],
                  "approvalMode": "auto",
                  "schema": {
                    "type": "object",
                    "properties": {
                      "text": { "type": "string" }
                    },
                    "required": ["text"],
                    "additionalProperties": false
                  }
                }
              }
            }
            """);

        CustomToolDynamicProvider provider = CreateProvider();
        ToolRegistry registry = new(
            [],
            new ToolPermissionParser(),
            [provider]);

        registry.TryResolve("custom__word_count", out ToolRegistration? registration)
            .Should()
            .BeTrue();
        registration!.Tool.Description.Should().Be("Count words.");
        registration.PermissionPolicy.ApprovalMode.Should().Be(ToolApprovalMode.Automatic);
        registration.PermissionPolicy.ToolTags.Should().Contain("custom_tool");
        provider.GetStatuses().Should().ContainSingle(status =>
            status.Name == "word_count" &&
            status.Kind == "custom" &&
            status.IsAvailable &&
            status.ToolCount == 1);
    }

    [Fact]
    public void GetTools_Should_SkipInvalidSchemaAndReportUnavailableStatus()
    {
        WriteFile(
            Path.Combine(_workspaceRoot, ".nanoagent", "agent-profile.json"),
            """
            {
              "customTools": {
                "bad": {
                  "command": "python",
                  "schema": []
                }
              }
            }
            """);

        CustomToolDynamicProvider provider = CreateProvider();

        provider.GetTools().Should().BeEmpty();
        provider.GetStatuses().Should().ContainSingle(status =>
            status.Name == "bad" &&
            status.Enabled &&
            !status.IsAvailable &&
            status.Details!.Contains("schema"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private CustomToolDynamicProvider CreateProvider()
    {
        return new CustomToolDynamicProvider(
            new StubUserDataPathProvider(_userProfilePath),
            new StubWorkspaceRootProvider(_workspaceRoot),
            new FakeProcessRunner(),
            NullLogger<CustomToolDynamicProvider>.Instance);
    }

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
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

    private sealed class FakeProcessRunner : IProcessRunner
    {
        public Task<ProcessExecutionResult> RunAsync(
            ProcessExecutionRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new ProcessExecutionResult(0, string.Empty, string.Empty));
        }
    }
}
