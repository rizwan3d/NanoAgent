using FluentAssertions;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Permissions;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;
using NanoAgent.Application.Tools.Services;
using NanoAgent.Domain.Models;

namespace NanoAgent.Tests.Application.Tools.Services;

public sealed class RegistryBackedToolInvokerTests
{
    private static readonly PermissionSettings DefaultPermissionSettings = new()
    {
        DefaultMode = PermissionMode.Ask,
        Rules = []
    };
    private static readonly ReplSessionContext Session = new(
        new AgentProviderProfile(ProviderKind.OpenAi, null),
        "gpt-5-mini",
        ["gpt-5-mini"]);

    [Fact]
    public async Task InvokeAsync_Should_ReturnNotFoundResult_When_ToolIsUnknown()
    {
        RegistryBackedToolInvoker sut = new(
            new ToolRegistry([], new ToolPermissionParser()),
            new ToolPermissionEvaluator(new StubWorkspaceRootProvider(), DefaultPermissionSettings),
            new FixedPermissionApprovalPrompt(PermissionApprovalChoice.DenyOnce));

        ToolInvocationResult result = await sut.InvokeAsync(
            new ConversationToolCall("call_1", "missing_tool", "{}"),
            Session,
            ConversationExecutionPhase.Execution,
            CreateAllowedToolNames("missing_tool"),
            CancellationToken.None);

        result.Result.Status.Should().Be(ToolResultStatus.NotFound);
        result.Result.Message.Should().Contain("not registered");
        result.Result.JsonResult.Should().Contain("tool_not_found");
    }

    [Fact]
    public async Task InvokeAsync_Should_ReturnInvalidArguments_When_ToolArgumentsAreNotJsonObject()
    {
        RegistryBackedToolInvoker sut = new(new ToolRegistry([
            new EchoTool()
        ], new ToolPermissionParser()), new ToolPermissionEvaluator(new StubWorkspaceRootProvider(), DefaultPermissionSettings), new FixedPermissionApprovalPrompt(PermissionApprovalChoice.DenyOnce));

        ToolInvocationResult result = await sut.InvokeAsync(
            new ConversationToolCall("call_1", "echo_tool", "[]"),
            Session,
            ConversationExecutionPhase.Execution,
            CreateAllowedToolNames("echo_tool"),
            CancellationToken.None);

        result.Result.Status.Should().Be(ToolResultStatus.InvalidArguments);
        result.Result.Message.Should().Contain("JSON-object arguments");
    }

    [Fact]
    public async Task InvokeAsync_Should_ReturnExecutionErrorResult_When_ToolThrowsUnexpectedly()
    {
        RegistryBackedToolInvoker sut = new(new ToolRegistry([
            new ThrowingTool()
        ], new ToolPermissionParser()), new ToolPermissionEvaluator(new StubWorkspaceRootProvider(), DefaultPermissionSettings), new FixedPermissionApprovalPrompt(PermissionApprovalChoice.DenyOnce));

        ToolInvocationResult result = await sut.InvokeAsync(
            new ConversationToolCall("call_1", "exploding_tool", "{}"),
            Session,
            ConversationExecutionPhase.Execution,
            CreateAllowedToolNames("exploding_tool"),
            CancellationToken.None);

        result.Result.Status.Should().Be(ToolResultStatus.ExecutionError);
        result.Result.Message.Should().Contain("boom");
    }

    [Fact]
    public async Task InvokeAsync_Should_ReturnExecutionError_When_ToolTimesOut()
    {
        RegistryBackedToolInvoker sut = new(
            new ToolRegistry([new SlowTool()], new ToolPermissionParser()),
            new ToolPermissionEvaluator(new StubWorkspaceRootProvider(), DefaultPermissionSettings),
            new FixedPermissionApprovalPrompt(PermissionApprovalChoice.DenyOnce),
            TimeSpan.FromMilliseconds(50));

        ToolInvocationResult result = await sut.InvokeAsync(
            new ConversationToolCall("call_1", "slow_tool", "{}"),
            Session,
            ConversationExecutionPhase.Execution,
            CreateAllowedToolNames("slow_tool"),
            CancellationToken.None);

        result.Result.Status.Should().Be(ToolResultStatus.ExecutionError);
        result.Result.Message.Should().Contain("timed out");
    }

    [Fact]
    public async Task InvokeAsync_Should_ExecuteTool_When_ApprovalPromptAllowsOnce()
    {
        ApprovalTool tool = new();
        RegistryBackedToolInvoker sut = new(
            new ToolRegistry([tool], new ToolPermissionParser()),
            new ToolPermissionEvaluator(new StubWorkspaceRootProvider(), DefaultPermissionSettings),
            new FixedPermissionApprovalPrompt(PermissionApprovalChoice.AllowOnce));

        ToolInvocationResult result = await sut.InvokeAsync(
            new ConversationToolCall("call_1", "approval_tool", """{ "path": "src/app.cs" }"""),
            Session,
            ConversationExecutionPhase.Execution,
            CreateAllowedToolNames("approval_tool"),
            CancellationToken.None);

        result.Result.Status.Should().Be(ToolResultStatus.Success);
        tool.WasExecuted.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_Should_RunLifecycleHooksAroundToolCall()
    {
        RecordingLifecycleHookService hookService = new();
        RegistryBackedToolInvoker sut = new(
            new ToolRegistry([new EchoTool()], new ToolPermissionParser()),
            new ToolPermissionEvaluator(new StubWorkspaceRootProvider(), DefaultPermissionSettings),
            new FixedPermissionApprovalPrompt(PermissionApprovalChoice.DenyOnce),
            lifecycleHookService: hookService);

        ToolInvocationResult result = await sut.InvokeAsync(
            new ConversationToolCall("call_1", "echo_tool", "{}"),
            Session,
            ConversationExecutionPhase.Execution,
            CreateAllowedToolNames("echo_tool"),
            CancellationToken.None);

        result.Result.Status.Should().Be(ToolResultStatus.Success);
        hookService.Events.Should().Equal(
            LifecycleHookEvents.BeforeToolCall,
            LifecycleHookEvents.AfterToolCall);
    }

    [Fact]
    public async Task InvokeAsync_Should_BlockTool_When_BeforeHookBlocks()
    {
        HookVisibleTool tool = new(AgentToolNames.FileWrite);
        RecordingLifecycleHookService hookService = new(LifecycleHookEvents.BeforeFileWrite);
        RegistryBackedToolInvoker sut = new(
            new ToolRegistry([tool], new ToolPermissionParser()),
            new ToolPermissionEvaluator(new StubWorkspaceRootProvider(), DefaultPermissionSettings),
            new FixedPermissionApprovalPrompt(PermissionApprovalChoice.DenyOnce),
            lifecycleHookService: hookService);

        ToolInvocationResult result = await sut.InvokeAsync(
            new ConversationToolCall("call_1", AgentToolNames.FileWrite, """{ "path": "src/app.cs" }"""),
            Session,
            ConversationExecutionPhase.Execution,
            CreateAllowedToolNames(AgentToolNames.FileWrite),
            CancellationToken.None);

        result.Result.Status.Should().Be(ToolResultStatus.ExecutionError);
        result.Result.JsonResult.Should().Contain("lifecycle_hook_blocked");
        tool.WasExecuted.Should().BeFalse();
        hookService.Events.Should().Equal(
            LifecycleHookEvents.BeforeToolCall,
            LifecycleHookEvents.BeforeFileWrite);
    }

    [Fact]
    public async Task InvokeAsync_Should_RunShellFailureHook_When_ShellExitsNonZero()
    {
        RecordingLifecycleHookService hookService = new();
        RegistryBackedToolInvoker sut = new(
            new ToolRegistry([new ShellResultTool(exitCode: 2)], new ToolPermissionParser()),
            new ToolPermissionEvaluator(new StubWorkspaceRootProvider(), DefaultPermissionSettings),
            new FixedPermissionApprovalPrompt(PermissionApprovalChoice.DenyOnce),
            lifecycleHookService: hookService);

        ToolInvocationResult result = await sut.InvokeAsync(
            new ConversationToolCall("call_1", AgentToolNames.ShellCommand, """{ "command": "dotnet test" }"""),
            Session,
            ConversationExecutionPhase.Execution,
            CreateAllowedToolNames(AgentToolNames.ShellCommand),
            CancellationToken.None);

        result.Result.Status.Should().Be(ToolResultStatus.Success);
        hookService.Events.Should().Contain(LifecycleHookEvents.AfterShellFailure);
        hookService.Contexts.Should().Contain(context =>
            context.EventName == LifecycleHookEvents.AfterShellFailure &&
            context.ShellCommand == "dotnet test" &&
            context.ShellExitCode == 2);
    }

    [Fact]
    public async Task InvokeAsync_Should_RememberAllowOverride_When_ApprovalPromptAllowsForAgent()
    {
        ReplSessionContext session = new(
            new AgentProviderProfile(ProviderKind.OpenAi, null),
            "gpt-5-mini",
            ["gpt-5-mini"]);
        ApprovalTool tool = new();
        FixedPermissionApprovalPrompt prompt = new(PermissionApprovalChoice.AllowForAgent);
        RegistryBackedToolInvoker sut = new(
            new ToolRegistry([tool], new ToolPermissionParser()),
            new ToolPermissionEvaluator(new StubWorkspaceRootProvider(), DefaultPermissionSettings),
            prompt);

        ToolInvocationResult first = await sut.InvokeAsync(
            new ConversationToolCall("call_1", "approval_tool", """{ "path": "src/app.cs" }"""),
            session,
            ConversationExecutionPhase.Execution,
            CreateAllowedToolNames("approval_tool"),
            CancellationToken.None);
        ToolInvocationResult second = await sut.InvokeAsync(
            new ConversationToolCall("call_2", "approval_tool", """{ "path": "src/app.cs" }"""),
            session,
            ConversationExecutionPhase.Execution,
            CreateAllowedToolNames("approval_tool"),
            CancellationToken.None);

        first.Result.Status.Should().Be(ToolResultStatus.Success);
        second.Result.Status.Should().Be(ToolResultStatus.Success);
        prompt.PromptCount.Should().Be(1);
        session.PermissionOverrides.Should().ContainSingle();
    }

    [Fact]
    public async Task InvokeAsync_Should_ReturnPermissionDenied_When_ApprovalPromptDeniesForAgent()
    {
        ReplSessionContext session = new(
            new AgentProviderProfile(ProviderKind.OpenAi, null),
            "gpt-5-mini",
            ["gpt-5-mini"]);
        ApprovalTool tool = new();
        RegistryBackedToolInvoker sut = new(
            new ToolRegistry([tool], new ToolPermissionParser()),
            new ToolPermissionEvaluator(new StubWorkspaceRootProvider(), DefaultPermissionSettings),
            new FixedPermissionApprovalPrompt(PermissionApprovalChoice.DenyForAgent));

        ToolInvocationResult result = await sut.InvokeAsync(
            new ConversationToolCall("call_1", "approval_tool", """{ "path": "src/app.cs" }"""),
            session,
            ConversationExecutionPhase.Execution,
            CreateAllowedToolNames("approval_tool"),
            CancellationToken.None);

        result.Result.Status.Should().Be(ToolResultStatus.PermissionDenied);
        result.Result.Message.Should().ContainEquivalentOf("denied");
        tool.WasExecuted.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_Should_ReturnPermissionDenied_When_ShellCommandMatchesDenyRule()
    {
        PermissionSettings settings = new()
        {
            DefaultMode = PermissionMode.Ask,
            Rules =
            [
                new PermissionRule
                {
                    Tools = ["bash"],
                    Mode = PermissionMode.Deny,
                    Patterns = ["rm -rf*"]
                }
            ]
        };

        RegistryBackedToolInvoker sut = new(
            new ToolRegistry([new ShellRestrictedTool()], new ToolPermissionParser()),
            new ToolPermissionEvaluator(new StubWorkspaceRootProvider(), settings),
            new FixedPermissionApprovalPrompt(PermissionApprovalChoice.DenyOnce));

        ToolInvocationResult result = await sut.InvokeAsync(
            new ConversationToolCall("call_1", "shell_restricted_tool", """{ "command": "rm -rf ." }"""),
            Session,
            ConversationExecutionPhase.Execution,
            CreateAllowedToolNames("shell_restricted_tool"),
            CancellationToken.None);

        result.Result.Status.Should().Be(ToolResultStatus.PermissionDenied);
        result.Result.Message.Should().ContainEquivalentOf("denied");
    }

    [Fact]
    public async Task InvokeAsync_Should_ReturnPermissionDenied_When_ToolIsNotAvailableInPhase()
    {
        RegistryBackedToolInvoker sut = new(
            new ToolRegistry([new EchoTool()], new ToolPermissionParser()),
            new ToolPermissionEvaluator(new StubWorkspaceRootProvider(), DefaultPermissionSettings),
            new FixedPermissionApprovalPrompt(PermissionApprovalChoice.DenyOnce));

        ToolInvocationResult result = await sut.InvokeAsync(
            new ConversationToolCall("call_1", "echo_tool", "{}"),
            Session,
            ConversationExecutionPhase.Planning,
            CreateAllowedToolNames("file_read"),
            CancellationToken.None);

        result.Result.Status.Should().Be(ToolResultStatus.PermissionDenied);
        result.Result.JsonResult.Should().Contain("tool_not_available_in_phase");
        result.Result.Message.Should().Contain("planning phase");
    }

    private static IReadOnlySet<string> CreateAllowedToolNames(params string[] toolNames)
    {
        return new HashSet<string>(toolNames, StringComparer.Ordinal);
    }

    private sealed class EchoTool : ITool
    {
        public string Description => "Echo tool";

        public string Name => "echo_tool";

        public string PermissionRequirements => """{ "approvalMode": "Automatic" }""";

        public string Schema => """{ "type": "object", "properties": {}, "additionalProperties": false }""";

        public Task<ToolResult> ExecuteAsync(
            ToolExecutionContext context,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(ToolResultFactory.Success(
                "Echoed.",
                new ToolErrorPayload("echo", "ok"),
                ToolJsonContext.Default.ToolErrorPayload));
        }
    }

    private sealed class SlowTool : ITool
    {
        public string Description => "Slow tool";

        public string Name => "slow_tool";

        public string PermissionRequirements => """{ "approvalMode": "Automatic" }""";

        public string Schema => """{ "type": "object", "properties": {}, "additionalProperties": false }""";

        public async Task<ToolResult> ExecuteAsync(
            ToolExecutionContext context,
            CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return ToolResultFactory.Success(
                "Completed.",
                new ToolErrorPayload("slow", "done"),
                ToolJsonContext.Default.ToolErrorPayload);
        }
    }

    private sealed class ThrowingTool : ITool
    {
        public string Description => "Throwing tool";

        public string Name => "exploding_tool";

        public string PermissionRequirements => """{ "approvalMode": "Automatic" }""";

        public string Schema => """{ "type": "object", "properties": {}, "additionalProperties": false }""";

        public Task<ToolResult> ExecuteAsync(
            ToolExecutionContext context,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("boom");
        }
    }

    private sealed class ApprovalTool : ITool
    {
        public bool WasExecuted { get; private set; }

        public string Description => "Approval tool";

        public string Name => "approval_tool";

        public string PermissionRequirements => """
            {
              "approvalMode": "RequireApproval",
              "filePaths": [
                {
                  "argumentName": "path",
                  "kind": "Read",
                  "allowedRoots": ["src"]
                }
              ]
            }
            """;

        public string Schema => """{ "type": "object", "properties": { "path": { "type": "string" } }, "additionalProperties": false }""";

        public Task<ToolResult> ExecuteAsync(
            ToolExecutionContext context,
            CancellationToken cancellationToken)
        {
            WasExecuted = true;
            return Task.FromResult(ToolResultFactory.Success(
                "Executed.",
                new ToolErrorPayload("ok", "ok"),
                ToolJsonContext.Default.ToolErrorPayload));
        }
    }

    private sealed class ShellRestrictedTool : ITool
    {
        public string Description => "Shell restricted tool";

        public string Name => "shell_restricted_tool";

        public string PermissionRequirements => """
            {
              "approvalMode": "Automatic",
              "toolTags": ["bash"],
              "shell": {
                "commandArgumentName": "command"
              }
            }
            """;

        public string Schema => """{ "type": "object", "properties": { "command": { "type": "string" } }, "additionalProperties": false }""";

        public Task<ToolResult> ExecuteAsync(
            ToolExecutionContext context,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class HookVisibleTool : ITool
    {
        private readonly string _name;

        public HookVisibleTool(string name)
        {
            _name = name;
        }

        public bool WasExecuted { get; private set; }

        public string Description => "Hook visible tool";

        public string Name => _name;

        public string PermissionRequirements => """{ "approvalMode": "Automatic" }""";

        public string Schema => """{ "type": "object", "properties": { "path": { "type": "string" } }, "additionalProperties": false }""";

        public Task<ToolResult> ExecuteAsync(
            ToolExecutionContext context,
            CancellationToken cancellationToken)
        {
            WasExecuted = true;
            return Task.FromResult(ToolResultFactory.Success(
                "Executed.",
                new ToolErrorPayload("ok", "ok"),
                ToolJsonContext.Default.ToolErrorPayload));
        }
    }

    private sealed class ShellResultTool : ITool
    {
        private readonly int _exitCode;

        public ShellResultTool(int exitCode)
        {
            _exitCode = exitCode;
        }

        public string Description => "Shell result tool";

        public string Name => AgentToolNames.ShellCommand;

        public string PermissionRequirements => """
            {
              "approvalMode": "Automatic",
              "toolTags": ["bash"],
              "shell": {
                "commandArgumentName": "command"
              }
            }
            """;

        public string Schema => """{ "type": "object", "properties": { "command": { "type": "string" } }, "additionalProperties": false }""";

        public Task<ToolResult> ExecuteAsync(
            ToolExecutionContext context,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(ToolResultFactory.Success(
                "Shell completed.",
                new ShellCommandExecutionResult("dotnet test", ".", _exitCode, string.Empty, "failed"),
                ToolJsonContext.Default.ShellCommandExecutionResult));
        }
    }

    private sealed class RecordingLifecycleHookService : ILifecycleHookService
    {
        private readonly string? _blockedEvent;

        public RecordingLifecycleHookService(string? blockedEvent = null)
        {
            _blockedEvent = blockedEvent;
        }

        public List<LifecycleHookContext> Contexts { get; } = [];

        public IReadOnlyList<string> Events => Contexts.Select(static context => context.EventName).ToArray();

        public Task<LifecycleHookRunResult> RunAsync(
            LifecycleHookContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Contexts.Add(context);

            if (string.Equals(context.EventName, _blockedEvent, StringComparison.Ordinal))
            {
                return Task.FromResult(LifecycleHookRunResult.Blocked(
                    "test-hook",
                    $"Blocked {context.EventName}."));
            }

            return Task.FromResult(LifecycleHookRunResult.Allowed());
        }
    }

    private sealed class StubWorkspaceRootProvider : IWorkspaceRootProvider
    {
        public string GetWorkspaceRoot()
        {
            return Path.GetTempPath();
        }
    }

    private sealed class FixedPermissionApprovalPrompt : IPermissionApprovalPrompt
    {
        private readonly PermissionApprovalChoice _choice;

        public FixedPermissionApprovalPrompt(PermissionApprovalChoice choice)
        {
            _choice = choice;
        }

        public int PromptCount { get; private set; }

        public Task<PermissionApprovalChoice> PromptAsync(
            PermissionApprovalRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PromptCount++;
            return Task.FromResult(_choice);
        }
    }
}
