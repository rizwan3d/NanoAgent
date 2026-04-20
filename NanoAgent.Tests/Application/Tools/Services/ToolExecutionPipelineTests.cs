using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Services;
using NanoAgent.Application.Tools.Serialization;
using NanoAgent.Domain.Models;
using FluentAssertions;
using Moq;

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
}
