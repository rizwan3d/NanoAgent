using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;

namespace NanoAgent.Application.Commands;

internal sealed class UndoCommandHandler : IReplCommandHandler
{
    private readonly IWorkspaceFileService _workspaceFileService;

    public UndoCommandHandler(IWorkspaceFileService workspaceFileService)
    {
        _workspaceFileService = workspaceFileService;
    }

    public string CommandName => "undo";

    public string Description => "Roll back the most recent tracked file edit transaction.";

    public string Usage => "/undo";

    public Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(context.ArgumentText))
        {
            return Task.FromResult(ReplCommandResult.Continue(
                "Usage: /undo",
                ReplFeedbackKind.Error));
        }

        if (!context.Session.TryGetPendingUndoFileEdit(out WorkspaceFileEditTransaction? transaction) ||
            transaction is null)
        {
            return Task.FromResult(ReplCommandResult.Continue(
                "Nothing to undo in file edit history.",
                ReplFeedbackKind.Warning));
        }

        return UndoAsync(context, transaction, cancellationToken);
    }

    private async Task<ReplCommandResult> UndoAsync(
        ReplCommandContext context,
        WorkspaceFileEditTransaction transaction,
        CancellationToken cancellationToken)
    {
        await _workspaceFileService.ApplyFileEditStatesAsync(
            transaction.BeforeStates,
            cancellationToken);
        context.Session.CompleteUndoFileEdit();
        FileEditCommandStateRecorder.Record(
            context.Session,
            "undo",
            "Rolled back file edit",
            transaction);

        return ReplCommandResult.Continue(
            $"Rolled back the last file edit: {transaction.Description}. Use /redo to restore it.",
            ReplFeedbackKind.Info);
    }
}
