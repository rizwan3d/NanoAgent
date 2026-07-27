using FluentAssertions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Abstractions;
using NanoAgent.Infrastructure.CustomTools;
using NanoAgent.Infrastructure.Secrets;
using NanoAgent.Infrastructure.WindowsSandbox;
using System.Text.Json;

namespace NanoAgent.Tests.Infrastructure.CustomTools;

public sealed class CustomToolTests
{
    [Fact]
    public async Task ExecuteAsync_Should_SendArgumentsJsonOnStandardInput_AndReturnStructuredResult()
    {
        string workspacePath = Path.Combine(Path.GetTempPath(), "NanoAgent-CustomToolTest");
        FakeProcessRunner processRunner = new(request =>
        {
            request.FileName.Should().Be("python");
            request.Arguments.Should().Equal("tools/count.py");
            request.WorkingDirectory.Should().Be(workspacePath);
            request.EnvironmentVariables.Should().ContainKey("NANOAGENT_CUSTOM_TOOL_NAME");

            using JsonDocument input = JsonDocument.Parse(request.StandardInput!);
            input.RootElement.GetProperty("toolName").GetString().Should().Be("custom__word_count");
            input.RootElement.GetProperty("configuredName").GetString().Should().Be("word_count");
            input.RootElement.GetProperty("arguments").GetProperty("text").GetString().Should().Be("hello world");

            return new ProcessExecutionResult(
                0,
                """
                {
                  "status": "success",
                  "message": "Counted words.",
                  "data": {
                    "words": 2
                  },
                  "renderText": "2 words"
                }
                """,
                string.Empty);
        });
        CustomToolConfiguration configuration = new("word_count")
        {
            Command = "python",
            Cwd = workspacePath
        };
        configuration.Args.Add("tools/count.py");
        CustomTool sut = new(
            "custom__word_count",
            configuration,
            CreateExecutor(processRunner, workspacePath));

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "text": "hello world" }""", workspacePath),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        result.Message.Should().Be("Counted words.");
        result.JsonResult.Should().Contain("\"words\":2");
        result.RenderPayload!.Text.Should().Be("2 words");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnExecutionError_When_ProcessExitsNonZero()
    {
        FakeProcessRunner processRunner = new(_ => new ProcessExecutionResult(
            2,
            string.Empty,
            "bad input"));
        CustomToolConfiguration configuration = new("lint")
        {
            Command = "node"
        };
        CustomTool sut = new(
            "custom__lint",
            configuration,
            CreateExecutor(processRunner));

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("{}"),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.ExecutionError);
        result.Message.Should().Contain("exited with code 2");
        result.JsonResult.Should().Contain("\"exitCode\":2");
        result.RenderPayload!.Text.Should().Contain("bad input");
    }

    [Fact]
    public void PermissionRequirements_Should_DefaultToApprovalAndCustomTags()
    {
        CustomToolConfiguration configuration = new("lint")
        {
            Command = "node"
        };
        CustomTool sut = new(
            "custom__lint",
            configuration,
            CreateExecutor(new FakeProcessRunner(_ =>
                new ProcessExecutionResult(0, string.Empty, string.Empty))));

        sut.PermissionRequirements.Should().Contain("RequireApproval");
        sut.PermissionRequirements.Should().Contain("custom_tool");
        sut.PermissionRequirements.Should().Contain("custom:lint");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ExposeSandboxMetadataToProcessEnvironment()
    {
        string workspacePath = Path.Combine(Path.GetTempPath(), "NanoAgent-CustomToolTest");
        Directory.CreateDirectory(workspacePath);
        ProcessExecutionRequest? capturedRequest = null;
        FakeProcessRunner processRunner = new(request =>
        {
            capturedRequest = request;
            return new ProcessExecutionResult(0, "{}", string.Empty);
        });
        FakeWindowsSandboxProcessRunner windowsSandboxProcessRunner = new(request =>
        {
            capturedRequest = request;
            return new ProcessExecutionResult(0, "{}", string.Empty);
        });
        CustomToolConfiguration configuration = new("lint")
        {
            Command = "node",
            Cwd = workspacePath
        };
        CustomTool sut = new(
            "custom__lint",
            configuration,
            CreateExecutor(
                processRunner,
                workspacePath,
                new PermissionSettings
                {
                    SandboxMode = ToolSandboxMode.ReadOnly
                },
                windowsSandboxProcessRunner));

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("{}", workspacePath),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        capturedRequest.Should().NotBeNull();
        capturedRequest!.EnvironmentVariables.Should().NotBeNull();
        capturedRequest.EnvironmentVariables!["NANOAGENT_SANDBOX_MODE"].Should().Be("read-only");
        capturedRequest.EnvironmentVariables["NANOAGENT_SANDBOX_EFFECTIVE_MODE"].Should().Be("read-only");
        capturedRequest.EnvironmentVariables["NANOAGENT_SANDBOX_PERMISSIONS"].Should().Be("use_default");
        capturedRequest.EnvironmentVariables["NANOAGENT_CUSTOM_TOOL_TRUSTED"].Should().Be("1");
        capturedRequest.EnvironmentVariables["NANOAGENT_WORKSPACE_ROOT"].Should().Be(Path.GetFullPath(workspacePath));
    }

    private static ToolExecutionContext CreateContext(string argumentsJson)
    {
        return CreateContext(
            argumentsJson,
            Path.Combine(Path.GetTempPath(), "NanoAgent-CustomToolTest"));
    }

    private static CustomToolProcessExecutor CreateExecutor(
        IProcessRunner processRunner,
        string? workspacePath = null,
        PermissionSettings? permissionSettings = null,
        IWindowsSandboxProcessRunner? windowsSandboxProcessRunner = null)
    {
        string root = workspacePath ?? Path.Combine(Path.GetTempPath(), "NanoAgent-CustomToolTest");
        return new CustomToolProcessExecutor(
            processRunner,
            new StubWorkspaceRootProvider(root),
            permissionSettings ?? new PermissionSettings
            {
                SandboxMode = ToolSandboxMode.DangerFullAccess
            },
            windowsSandboxProcessRunner);
    }

    private static ToolExecutionContext CreateContext(
        string argumentsJson,
        string workspacePath)
    {
        using JsonDocument document = JsonDocument.Parse(argumentsJson);
        return new ToolExecutionContext(
            "call_1",
            "custom__word_count",
            document.RootElement.Clone(),
            new ReplSessionContext(
                new NanoAgent.Domain.Models.AgentProviderProfile(
                    NanoAgent.Domain.Models.ProviderKind.OpenAi,
                    null),
                "gpt-5-mini",
                ["gpt-5-mini"],
                workspacePath: workspacePath));
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        private readonly Func<ProcessExecutionRequest, ProcessExecutionResult> _handler;

        public FakeProcessRunner(Func<ProcessExecutionRequest, ProcessExecutionResult> handler)
        {
            _handler = handler;
        }

        public Task<ProcessExecutionResult> RunAsync(
            ProcessExecutionRequest request,
            CancellationToken cancellationToken)
        {
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

    private sealed class FakeWindowsSandboxProcessRunner : IWindowsSandboxProcessRunner
    {
        private readonly Func<ProcessExecutionRequest, ProcessExecutionResult> _handler;

        public FakeWindowsSandboxProcessRunner(Func<ProcessExecutionRequest, ProcessExecutionResult> handler)
        {
            _handler = handler;
        }

        public Task<ProcessExecutionResult> RunAsync(
            ProcessExecutionRequest request,
            WindowsSandboxExecutionContext context,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }

        public Task<IBackgroundProcessHandle> StartBackgroundAsync(
            ProcessExecutionRequest request,
            WindowsSandboxExecutionContext context,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
