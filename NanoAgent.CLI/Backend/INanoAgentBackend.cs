using NanoAgent.Application.Models;

namespace NanoAgent.CLI;

public interface INanoAgentBackend : IAsyncDisposable
{
    Task<BackendSessionInfo> InitializeAsync(
        IUiBridge uiBridge,
        CancellationToken cancellationToken);

    Task<BackendCommandResult> RunCommandAsync(
        string commandText,
        CancellationToken cancellationToken);

    Task<ConversationTurnResult> RunTurnAsync(
        string input,
        IUiBridge uiBridge,
        CancellationToken cancellationToken);
}
