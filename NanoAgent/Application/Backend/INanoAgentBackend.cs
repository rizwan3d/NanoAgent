using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.UI;

namespace NanoAgent.Application.Backend;

public interface INanoAgentBackend : IAsyncDisposable
{
    Task<BackendSessionInfo> InitializeAsync(
        IUiBridge uiBridge,
        CancellationToken cancellationToken);

    Task<BackendCommandResult> RunCommandAsync(
        string commandText,
        CancellationToken cancellationToken);

    Task<BackendCommandResult> SelectModelAsync(
        CancellationToken cancellationToken);

    Task<ConversationTurnResult> RunTurnAsync(
        string input,
        IUiBridge uiBridge,
        CancellationToken cancellationToken);

    Task<ConversationTurnResult> RunTurnAsync(
        string input,
        IReadOnlyList<ConversationAttachment> attachments,
        IUiBridge uiBridge,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<BackgroundTerminalInfo>> ListBackgroundTerminalsAsync(
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<BackgroundTerminalInfo>>([]);

    Task<ShellCommandExecutionResult> ReadBackgroundTerminalAsync(
        string terminalId,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("This backend does not support background terminals.");

    /// <summary>Per-file added/removed line totals for the current conversation, for the end-of-conversation summary.</summary>
    IReadOnlyList<FileEditSummary> GetFileEditSummary() => [];
}
