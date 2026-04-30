using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Infrastructure.Configuration;
using NanoAgent.Infrastructure.Hooks;
using NanoAgent.Infrastructure.Secrets;

namespace NanoAgent.Tests.Infrastructure.Hooks;

public sealed class ShellLifecycleHookServiceTests
{
    [Fact]
    public async Task RunAsync_Should_InvokeMatchingHookWithPayloadAndEnvironment()
    {
        RecordingProcessRunner processRunner = new(new ProcessExecutionResult(0, "ok", string.Empty));
        ShellLifecycleHookService sut = CreateService(
            processRunner,
            new LifecycleHookSettings
            {
                Rules =
                [
                    new LifecycleHookRule
                    {
                        Event = LifecycleHookEvents.BeforeToolCall,
                        Command = "hook.exe",
                        RunInShell = false,
                        ToolNames = ["file_write"]
                    }
                ]
            });

        LifecycleHookRunResult result = await sut.RunAsync(
            new LifecycleHookContext
            {
                EventName = LifecycleHookEvents.BeforeToolCall,
                SessionId = "sec_123",
                ToolCallId = "call_1",
                ToolName = "file_write",
                Path = "src/app.cs"
            },
            CancellationToken.None);

        result.IsAllowed.Should().BeTrue();
        processRunner.Requests.Should().ContainSingle();
        ProcessExecutionRequest request = processRunner.Requests[0];
        request.FileName.Should().Be("hook.exe");
        request.StandardInput.Should().Contain("\"eventName\": \"before_tool_call\"");
        request.StandardInput.Should().Contain("\"toolName\": \"file_write\"");
        request.EnvironmentVariables.Should().ContainKey("NANOAGENT_HOOK_EVENT")
            .WhoseValue.Should().Be("before_tool_call");
        request.EnvironmentVariables.Should().ContainKey("NANOAGENT_TOOL_NAME")
            .WhoseValue.Should().Be("file_write");
    }

    [Fact]
    public async Task RunAsync_Should_BlockBeforeHookFailureByDefault()
    {
        RecordingProcessRunner processRunner = new(new ProcessExecutionResult(7, string.Empty, "stop"));
        ShellLifecycleHookService sut = CreateService(
            processRunner,
            new LifecycleHookSettings
            {
                Rules =
                [
                    new LifecycleHookRule
                    {
                        Event = LifecycleHookEvents.BeforeFileWrite,
                        Command = "hook.exe",
                        RunInShell = false
                    }
                ]
            });

        LifecycleHookRunResult result = await sut.RunAsync(
            new LifecycleHookContext
            {
                EventName = LifecycleHookEvents.BeforeFileWrite,
                ToolName = "file_write"
            },
            CancellationToken.None);

        result.IsAllowed.Should().BeFalse();
        result.FailedHookName.Should().Be("hook.exe");
        result.Message.Should().Contain("stop");
    }

    [Fact]
    public async Task RunAsync_Should_AllowAfterHookFailureByDefault()
    {
        RecordingProcessRunner processRunner = new(new ProcessExecutionResult(7, string.Empty, "after failed"));
        ShellLifecycleHookService sut = CreateService(
            processRunner,
            new LifecycleHookSettings
            {
                Rules =
                [
                    new LifecycleHookRule
                    {
                        Event = LifecycleHookEvents.AfterFileWrite,
                        Command = "hook.exe",
                        RunInShell = false
                    }
                ]
            });

        LifecycleHookRunResult result = await sut.RunAsync(
            new LifecycleHookContext
            {
                EventName = LifecycleHookEvents.AfterFileWrite,
                ToolName = "file_write"
            },
            CancellationToken.None);

        result.IsAllowed.Should().BeTrue();
        processRunner.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task RunAsync_Should_FilterByShellCommandPattern()
    {
        RecordingProcessRunner processRunner = new(new ProcessExecutionResult(0, "ok", string.Empty));
        ShellLifecycleHookService sut = CreateService(
            processRunner,
            new LifecycleHookSettings
            {
                Rules =
                [
                    new LifecycleHookRule
                    {
                        Event = LifecycleHookEvents.AfterShellFailure,
                        Command = "hook.exe",
                        RunInShell = false,
                        ShellCommandPatterns = ["dotnet test*"]
                    }
                ]
            });

        await sut.RunAsync(
            new LifecycleHookContext
            {
                EventName = LifecycleHookEvents.AfterShellFailure,
                ToolName = "shell_command",
                ShellCommand = "npm test"
            },
            CancellationToken.None);
        await sut.RunAsync(
            new LifecycleHookContext
            {
                EventName = LifecycleHookEvents.AfterShellFailure,
                ToolName = "shell_command",
                ShellCommand = "dotnet test --filter Unit"
            },
            CancellationToken.None);

        processRunner.Requests.Should().ContainSingle();
    }

    private static ShellLifecycleHookService CreateService(
        RecordingProcessRunner processRunner,
        LifecycleHookSettings settings)
    {
        return new ShellLifecycleHookService(
            Options.Create(new ApplicationOptions
            {
                Hooks = settings
            }),
            processRunner,
            new StubWorkspaceRootProvider(),
            NullLogger<ShellLifecycleHookService>.Instance);
    }

    private sealed class RecordingProcessRunner : IProcessRunner
    {
        private readonly ProcessExecutionResult _result;

        public RecordingProcessRunner(ProcessExecutionResult result)
        {
            _result = result;
        }

        public List<ProcessExecutionRequest> Requests { get; } = [];

        public Task<ProcessExecutionResult> RunAsync(
            ProcessExecutionRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult(_result);
        }
    }

    private sealed class StubWorkspaceRootProvider : IWorkspaceRootProvider
    {
        public string GetWorkspaceRoot()
        {
            return Directory.GetCurrentDirectory();
        }
    }
}
