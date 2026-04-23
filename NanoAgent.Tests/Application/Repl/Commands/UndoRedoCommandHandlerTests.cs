using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Repl.Commands;
using NanoAgent.Domain.Models;
using FluentAssertions;
using Moq;

namespace NanoAgent.Tests.Application.Repl.Commands;

public sealed class UndoRedoCommandHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_Should_ReturnWarning_When_UndoHasNoTrackedFileEdits()
    {
        UndoCommandHandler sut = new(Mock.Of<IWorkspaceFileService>());
        ReplSessionContext session = CreateSession();

        ReplCommandResult result = await sut.ExecuteAsync(
            new ReplCommandContext("undo", string.Empty, [], "/undo", session),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Warning);
        result.Message.Should().Be("Nothing to undo in file edit history.");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnWarning_When_RedoHasNoTrackedFileEdits()
    {
        RedoCommandHandler sut = new(Mock.Of<IWorkspaceFileService>());
        ReplSessionContext session = CreateSession();

        ReplCommandResult result = await sut.ExecuteAsync(
            new ReplCommandContext("redo", string.Empty, [], "/redo", session),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Warning);
        result.Message.Should().Be("Nothing to redo in file edit history.");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ApplyBeforeStates_When_UndoRuns()
    {
        WorkspaceFileEditTransaction transaction = CreateTransaction();
        ReplSessionContext session = CreateSession();
        session.RecordFileEditTransaction(transaction);
        Mock<IWorkspaceFileService> workspaceFileService = new(MockBehavior.Strict);
        workspaceFileService
            .Setup(service => service.ApplyFileEditStatesAsync(
                It.Is<IReadOnlyList<WorkspaceFileEditState>>(states =>
                    states.Count == 1 &&
                    states[0].Path == "README.md" &&
                    !states[0].Exists),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        UndoCommandHandler sut = new(workspaceFileService.Object);

        ReplCommandResult result = await sut.ExecuteAsync(
            new ReplCommandContext("undo", string.Empty, [], "/undo", session),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
        result.Message.Should().Contain("Rolled back the last file edit");
        session.TryGetPendingUndoFileEdit(out _).Should().BeFalse();
        session.TryGetPendingRedoFileEdit(out WorkspaceFileEditTransaction? redoTransaction).Should().BeTrue();
        redoTransaction.Should().BeSameAs(transaction);
        session.SessionState.Edits.Should().Contain(edit => edit.Description == "undo (file_write (README.md))");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ApplyAfterStates_When_RedoRuns()
    {
        WorkspaceFileEditTransaction transaction = CreateTransaction();
        ReplSessionContext session = CreateSession();
        session.RecordFileEditTransaction(transaction);
        session.CompleteUndoFileEdit();
        Mock<IWorkspaceFileService> workspaceFileService = new(MockBehavior.Strict);
        workspaceFileService
            .Setup(service => service.ApplyFileEditStatesAsync(
                It.Is<IReadOnlyList<WorkspaceFileEditState>>(states =>
                    states.Count == 1 &&
                    states[0].Path == "README.md" &&
                    states[0].Exists &&
                    states[0].Content == "hello"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        RedoCommandHandler sut = new(workspaceFileService.Object);

        ReplCommandResult result = await sut.ExecuteAsync(
            new ReplCommandContext("redo", string.Empty, [], "/redo", session),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
        result.Message.Should().Contain("Re-applied the last undone file edit");
        session.TryGetPendingRedoFileEdit(out _).Should().BeFalse();
        session.TryGetPendingUndoFileEdit(out WorkspaceFileEditTransaction? undoTransaction).Should().BeTrue();
        undoTransaction.Should().BeSameAs(transaction);
        session.SessionState.Edits.Should().Contain(edit => edit.Description == "redo (file_write (README.md))");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnUsageError_When_ExtraArgumentsAreProvided()
    {
        UndoCommandHandler undoHandler = new(Mock.Of<IWorkspaceFileService>());
        RedoCommandHandler redoHandler = new(Mock.Of<IWorkspaceFileService>());
        ReplSessionContext session = CreateSession();

        ReplCommandResult undoResult = await undoHandler.ExecuteAsync(
            new ReplCommandContext("undo", "now", ["now"], "/undo now", session),
            CancellationToken.None);
        ReplCommandResult redoResult = await redoHandler.ExecuteAsync(
            new ReplCommandContext("redo", "now", ["now"], "/redo now", session),
            CancellationToken.None);

        undoResult.FeedbackKind.Should().Be(ReplFeedbackKind.Error);
        undoResult.Message.Should().Be("Usage: /undo");
        redoResult.FeedbackKind.Should().Be(ReplFeedbackKind.Error);
        redoResult.Message.Should().Be("Usage: /redo");
    }

    private static ReplSessionContext CreateSession()
    {
        return new ReplSessionContext(
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "gpt-5-mini",
            ["gpt-5-mini"]);
    }

    private static WorkspaceFileEditTransaction CreateTransaction()
    {
        return new WorkspaceFileEditTransaction(
            "file_write (README.md)",
            [new WorkspaceFileEditState("README.md", exists: false, content: null)],
            [new WorkspaceFileEditState("README.md", exists: true, content: "hello")]);
    }
}
