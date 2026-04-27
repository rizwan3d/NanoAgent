using NanoAgent.Application.Models;
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
}
