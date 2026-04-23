using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;

namespace NanoAgent.Application.Repl.Commands;

internal sealed class RedoCommandHandler : IReplCommandHandler
{
    private readonly IWorkspaceFileService _workspaceFileService;

    public RedoCommandHandler(IWorkspaceFileService workspaceFileService)
    {
        _workspaceFileService = workspaceFileService;
    }

    public string CommandName => "redo";

    public string Description => "Re-apply the most recently undone file edit transaction.";

    public string Usage => "/redo";

    public Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(context.ArgumentText))
        {
            return Task.FromResult(ReplCommandResult.Continue(
                "Usage: /redo",
                ReplFeedbackKind.Error));
        }

        if (!context.Session.TryGetPendingRedoFileEdit(out WorkspaceFileEditTransaction? transaction) ||
            transaction is null)
        {
            return Task.FromResult(ReplCommandResult.Continue(
                "Nothing to redo in file edit history.",
                ReplFeedbackKind.Warning));
        }

        return RedoAsync(context, transaction, cancellationToken);
    }

    private async Task<ReplCommandResult> RedoAsync(
        ReplCommandContext context,
        WorkspaceFileEditTransaction transaction,
        CancellationToken cancellationToken)
    {
        await _workspaceFileService.ApplyFileEditStatesAsync(
            transaction.AfterStates,
            cancellationToken);
        context.Session.CompleteRedoFileEdit();
        FileEditCommandStateRecorder.Record(
            context.Session,
            "redo",
            "Re-applied file edit",
            transaction);

        return ReplCommandResult.Continue(
            $"Re-applied the last undone file edit: {transaction.Description}.",
            ReplFeedbackKind.Info);
    }
}
