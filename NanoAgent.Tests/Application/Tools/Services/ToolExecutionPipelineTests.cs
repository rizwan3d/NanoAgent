using FluentAssertions;
using Moq;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Serialization;
using NanoAgent.Application.Tools.Services;
using NanoAgent.Domain.Models;

namespace NanoAgent.Tests.Application.Tools.Services;

public sealed class ToolExecutionPipelineTests
{
    private static readonly ReplSessionContext Session = new(
        new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
        "gpt-5-mini",
        ["gpt-5-mini", "gpt-4.1"]);

    [Fact]
    public async Task ExecuteAsync_Should_ReturnStructuredResultsInInputOrder()
    {
        IReadOnlySet<string> allowedToolNames = new HashSet<string>(
            ["file_read", "shell_command"],
            StringComparer.Ordinal);
        Mock<IToolInvoker> toolInvoker = new(MockBehavior.Strict);
        toolInvoker
            .SetupSequence(invoker => invoker.InvokeAsync(
                It.IsAny<ConversationToolCall>(),
                Session,
                ConversationExecutionPhase.Execution,
                allowedToolNames,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolInvocationResult(
                "call_1",
                "file_read",
                ToolResultFactory.Success(
                    "Read file 'README.md'.",
                    new ToolErrorPayload("info", "ok"),
                    ToolJsonContext.Default.ToolErrorPayload,
                    new ToolRenderPayload("File: README.md", "hello"))))
            .ReturnsAsync(new ToolInvocationResult(
                "call_2",
                "shell_command",
                ToolResultFactory.InvalidArguments(
                    "invalid_command",
                    "Tool 'shell_command' requires a non-empty 'command' string.",
                    new ToolRenderPayload("Invalid shell_command arguments", "Provide a non-empty command."))));

        ToolExecutionPipeline sut = new(toolInvoker.Object);

        ToolExecutionBatchResult result = await sut.ExecuteAsync(
            [
                new ConversationToolCall("call_1", "file_read", """{ "path": "README.md" }"""),
                new ConversationToolCall("call_2", "shell_command", "{}")
            ],
            Session,
            ConversationExecutionPhase.Execution,
            allowedToolNames,
            CancellationToken.None);

        result.Results.Select(item => item.ToolCallId).Should().Equal("call_1", "call_2");
        result.HasFailures.Should().BeTrue();
        result.ToDisplayText().Should().Contain("File: README.md");
        result.ToDisplayText().Should().Contain("Invalid shell_command arguments");
    }

    [Fact]
    public async Task ExecuteAsync_Should_RunParallelSafeToolCallsConcurrently()
    {
        IReadOnlySet<string> allowedToolNames = new HashSet<string>(
            ["file_read", "text_search"],
            StringComparer.Ordinal);
        ParallelGateToolInvoker toolInvoker = new(["call_1", "call_2"]);
        ToolExecutionPipeline sut = new(
            toolInvoker,
            maxParallelToolExecutions: 2);

        Task<ToolExecutionBatchResult> executionTask = sut.ExecuteAsync(
            [
                new ConversationToolCall("call_1", "file_read", """{ "path": "README.md" }"""),
                new ConversationToolCall("call_2", "text_search", """{ "query": "NanoAgent" }""")
            ],
            Session,
            ConversationExecutionPhase.Execution,
            allowedToolNames,
            CancellationToken.None);

        await toolInvoker.AllBlockedCallsStarted.WaitAsync(TimeSpan.FromSeconds(2));
        toolInvoker.ReleaseBlockedCalls();

        ToolExecutionBatchResult result = await executionTask;

        result.Results.Select(static item => item.ToolCallId).Should().Equal("call_1", "call_2");
        toolInvoker.StartedCalls.Should().ContainInOrder("call_1", "call_2");
    }

    [Fact]
    public async Task ExecuteAsync_Should_KeepUnsafeToolCallsAsSequentialBarriers()
    {
        IReadOnlySet<string> allowedToolNames = new HashSet<string>(
            ["file_read", "shell_command", "text_search"],
            StringComparer.Ordinal);
        SequentialBarrierToolInvoker toolInvoker = new();
        ToolExecutionPipeline sut = new(
            toolInvoker,
            maxParallelToolExecutions: 3);

        Task<ToolExecutionBatchResult> executionTask = sut.ExecuteAsync(
            [
                new ConversationToolCall("call_1", "file_read", """{ "path": "README.md" }"""),
                new ConversationToolCall("call_2", "shell_command", """{ "command": "cd src" }"""),
                new ConversationToolCall("call_3", "text_search", """{ "query": "NanoAgent" }""")
            ],
            Session,
            ConversationExecutionPhase.Execution,
            allowedToolNames,
            CancellationToken.None);

        await toolInvoker.ShellStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task firstCompleted = await Task.WhenAny(
            toolInvoker.TextSearchStarted.Task,
            Task.Delay(TimeSpan.FromMilliseconds(150)));
        firstCompleted.Should().NotBe(toolInvoker.TextSearchStarted.Task);

        toolInvoker.ReleaseShell();
        ToolExecutionBatchResult result = await executionTask;

        result.Results.Select(static item => item.ToolCallId).Should().Equal("call_1", "call_2", "call_3");
        toolInvoker.TextSearchStarted.Task.IsCompleted.Should().BeTrue();
        toolInvoker.TextSearchStartedAfterShellCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_Should_GroupTrackedFileEditsIntoOneUndoTransaction()
    {
        ReplSessionContext session = new(
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "gpt-5-mini",
            ["gpt-5-mini"]);
        IReadOnlySet<string> allowedToolNames = new HashSet<string>(
            ["file_write"],
            StringComparer.Ordinal);
        TrackingToolInvoker toolInvoker = new();
        ToolExecutionPipeline sut = new(toolInvoker);

        await sut.ExecuteAsync(
            [
                new ConversationToolCall("call_1", "file_write", """{ "path": "README.md" }"""),
                new ConversationToolCall("call_2", "file_write", """{ "path": "src/App.js" }""")
            ],
            session,
            ConversationExecutionPhase.Execution,
            allowedToolNames,
            CancellationToken.None);

        session.TryGetPendingUndoFileEdit(out WorkspaceFileEditTransaction? transaction).Should().BeTrue();
        transaction!.Description.Should().Be("tool round (2 edits across 2 files)");
        transaction.BeforeStates.Select(static state => state.Path).Should().Equal("README.md", "src/App.js");
        transaction.AfterStates.Select(static state => state.Path).Should().Equal("README.md", "src/App.js");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ObserveToolResultsWithLessonMemory()
    {
        IReadOnlySet<string> allowedToolNames = new HashSet<string>(
            ["shell_command"],
            StringComparer.Ordinal);
        ToolInvocationResult invocationResult = new(
            "call_1",
            "shell_command",
            ToolResultFactory.InvalidArguments(
                "missing_command",
                "Tool 'shell_command' requires a command."));
        Mock<IToolInvoker> toolInvoker = new(MockBehavior.Strict);
        toolInvoker
            .Setup(invoker => invoker.InvokeAsync(
                It.IsAny<ConversationToolCall>(),
                Session,
                ConversationExecutionPhase.Execution,
                allowedToolNames,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(invocationResult);
        RecordingLessonMemoryService lessonMemoryService = new();
        ToolExecutionPipeline sut = new(
            toolInvoker.Object,
            lessonMemoryService);

        await sut.ExecuteAsync(
            [new ConversationToolCall("call_1", "shell_command", "{}")],
            Session,
            ConversationExecutionPhase.Execution,
            allowedToolNames,
            CancellationToken.None);

        lessonMemoryService.ObservedResults.Should().ContainSingle();
        lessonMemoryService.ObservedResults[0].ToolCall.Should().BeEquivalentTo(
            new ConversationToolCall("call_1", "shell_command", "{}"));
        lessonMemoryService.ObservedResults[0].InvocationResult.Should().Be(invocationResult);
    }

    [Fact]
    public async Task ExecuteAsync_Should_RecordToolAuditResults()
    {
        IReadOnlySet<string> allowedToolNames = new HashSet<string>(
            ["file_read"],
            StringComparer.Ordinal);
        ToolInvocationResult invocationResult = new(
            "call_1",
            "file_read",
            ToolResultFactory.Success(
                "Read file 'README.md'.",
                new ToolErrorPayload("ok", "ok"),
                ToolJsonContext.Default.ToolErrorPayload));
        Mock<IToolInvoker> toolInvoker = new(MockBehavior.Strict);
        toolInvoker
            .Setup(invoker => invoker.InvokeAsync(
                It.IsAny<ConversationToolCall>(),
                Session,
                ConversationExecutionPhase.Execution,
                allowedToolNames,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(invocationResult);
        RecordingToolAuditLogService auditLogService = new();
        ToolExecutionPipeline sut = new(
            toolInvoker.Object,
            toolAuditLogService: auditLogService);

        await sut.ExecuteAsync(
            [new ConversationToolCall("call_1", "file_read", """{ "path": "README.md" }""")],
            Session,
            ConversationExecutionPhase.Execution,
            allowedToolNames,
            CancellationToken.None);

        auditLogService.Records.Should().ContainSingle();
        auditLogService.Records[0].ToolCall.Name.Should().Be("file_read");
        auditLogService.Records[0].InvocationResult.Should().Be(invocationResult);
        auditLogService.Records[0].Session.Should().Be(Session);
        auditLogService.Records[0].ExecutionPhase.Should().Be(ConversationExecutionPhase.Execution);
        auditLogService.Records[0].CompletedAtUtc.Should().BeOnOrAfter(auditLogService.Records[0].StartedAtUtc);
    }

    private sealed class TrackingToolInvoker : IToolInvoker
    {
        public Task<ToolInvocationResult> InvokeAsync(
            ConversationToolCall toolCall,
            ReplSessionContext session,
            ConversationExecutionPhase executionPhase,
            IReadOnlySet<string> allowedToolNames,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            executionPhase.Should().Be(ConversationExecutionPhase.Execution);
            allowedToolNames.Should().Contain("file_write");

            WorkspaceFileEditTransaction transaction = toolCall.Id switch
            {
                "call_1" => new WorkspaceFileEditTransaction(
                    "file_write (README.md)",
                    [new WorkspaceFileEditState("README.md", exists: false, content: null)],
                    [new WorkspaceFileEditState("README.md", exists: true, content: "hello")]),
                "call_2" => new WorkspaceFileEditTransaction(
                    "file_write (src/App.js)",
                    [new WorkspaceFileEditState("src/App.js", exists: true, content: "old")],
                    [new WorkspaceFileEditState("src/App.js", exists: true, content: "new")]),
                _ => throw new InvalidOperationException("Unexpected tool call.")
            };

            session.RecordFileEditTransaction(transaction);

            return Task.FromResult(new ToolInvocationResult(
                toolCall.Id,
                toolCall.Name,
                ToolResultFactory.Success(
                    "ok",
                    new ToolErrorPayload("ok", "ok"),
                    ToolJsonContext.Default.ToolErrorPayload)));
        }
    }

    private sealed class ParallelGateToolInvoker : IToolInvoker
    {
        private readonly HashSet<string> _blockedCallIds;
        private readonly TaskCompletionSource _allBlockedCallsStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseBlockedCalls = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _blockedStartedCount;

        public ParallelGateToolInvoker(IEnumerable<string> blockedCallIds)
        {
            _blockedCallIds = new HashSet<string>(blockedCallIds, StringComparer.Ordinal);
        }

        public Task AllBlockedCallsStarted => _allBlockedCallsStarted.Task;

        public List<string> StartedCalls { get; } = [];

        public void ReleaseBlockedCalls()
        {
            _releaseBlockedCalls.TrySetResult();
        }

        public async Task<ToolInvocationResult> InvokeAsync(
            ConversationToolCall toolCall,
            ReplSessionContext session,
            ConversationExecutionPhase executionPhase,
            IReadOnlySet<string> allowedToolNames,
            CancellationToken cancellationToken)
        {
            lock (StartedCalls)
            {
                StartedCalls.Add(toolCall.Id);
            }

            if (_blockedCallIds.Contains(toolCall.Id) &&
                Interlocked.Increment(ref _blockedStartedCount) == _blockedCallIds.Count)
            {
                _allBlockedCallsStarted.TrySetResult();
            }

            if (_blockedCallIds.Contains(toolCall.Id))
            {
                await _releaseBlockedCalls.Task.WaitAsync(cancellationToken);
            }

            return CreateSuccessResult(toolCall);
        }
    }

    private sealed class SequentialBarrierToolInvoker : IToolInvoker
    {
        private readonly TaskCompletionSource _releaseShell = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ShellStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource TextSearchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool TextSearchStartedAfterShellCompleted { get; private set; }

        private bool ShellCompleted { get; set; }

        public void ReleaseShell()
        {
            _releaseShell.TrySetResult();
        }

        public async Task<ToolInvocationResult> InvokeAsync(
            ConversationToolCall toolCall,
            ReplSessionContext session,
            ConversationExecutionPhase executionPhase,
            IReadOnlySet<string> allowedToolNames,
            CancellationToken cancellationToken)
        {
            if (string.Equals(toolCall.Name, "shell_command", StringComparison.Ordinal))
            {
                ShellStarted.TrySetResult();
                await _releaseShell.Task.WaitAsync(cancellationToken);
                ShellCompleted = true;
                return CreateSuccessResult(toolCall);
            }

            if (string.Equals(toolCall.Name, "text_search", StringComparison.Ordinal))
            {
                TextSearchStartedAfterShellCompleted = ShellCompleted;
                TextSearchStarted.TrySetResult();
            }

            return CreateSuccessResult(toolCall);
        }
    }

    private static ToolInvocationResult CreateSuccessResult(ConversationToolCall toolCall)
    {
        return new ToolInvocationResult(
            toolCall.Id,
            toolCall.Name,
            ToolResultFactory.Success(
                "ok",
                new ToolErrorPayload("ok", "ok"),
                ToolJsonContext.Default.ToolErrorPayload));
    }

    private sealed class RecordingLessonMemoryService : ILessonMemoryService
    {
        public List<(ConversationToolCall ToolCall, ToolInvocationResult InvocationResult)> ObservedResults { get; } = [];

        public Task<LessonMemoryEntry> SaveAsync(
            LessonMemorySaveRequest request,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<LessonMemoryEntry>> SearchAsync(
            string query,
            int limit,
            bool includeFixed,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<LessonMemoryEntry>>([]);
        }

        public Task<IReadOnlyList<LessonMemoryEntry>> ListAsync(
            int limit,
            bool includeFixed,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<LessonMemoryEntry>>([]);
        }

        public Task<LessonMemoryEntry?> EditAsync(
            LessonMemoryEditRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<LessonMemoryEntry?>(null);
        }

        public Task<bool> DeleteAsync(
            string id,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(false);
        }

        public Task<string?> CreatePromptAsync(
            string query,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<string?>(null);
        }

        public Task ObserveToolResultAsync(
            ConversationToolCall toolCall,
            ToolInvocationResult invocationResult,
            CancellationToken cancellationToken,
            ReplSessionContext? session = null)
        {
            ObservedResults.Add((toolCall, invocationResult));
            return Task.CompletedTask;
        }

        public string GetStoragePath()
        {
            return ".nanoagent/memory/lessons.jsonl";
        }
    }

    private sealed class RecordingToolAuditLogService : IToolAuditLogService
    {
        public List<AuditRecord> Records { get; } = [];

        public Task RecordAsync(
            ConversationToolCall toolCall,
            ToolInvocationResult invocationResult,
            ReplSessionContext session,
            ConversationExecutionPhase executionPhase,
            DateTimeOffset startedAtUtc,
            DateTimeOffset completedAtUtc,
            CancellationToken cancellationToken)
        {
            Records.Add(new AuditRecord(
                toolCall,
                invocationResult,
                session,
                executionPhase,
                startedAtUtc,
                completedAtUtc));
            return Task.CompletedTask;
        }

        public string GetStoragePath()
        {
            return ".nanoagent/logs/tool-audit.jsonl";
        }
    }

    private sealed record AuditRecord(
        ConversationToolCall ToolCall,
        ToolInvocationResult InvocationResult,
        ReplSessionContext Session,
        ConversationExecutionPhase ExecutionPhase,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset CompletedAtUtc);
}
