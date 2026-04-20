using NanoAgent.Application.Models;

namespace NanoAgent.Application.Abstractions;

public interface IReplOutputWriter
{
    ValueTask<IResponseProgress> BeginResponseProgressAsync(
        int estimatedOutputTokens,
        int completedSessionEstimatedOutputTokens,
        CancellationToken cancellationToken);

    Task WriteShellHeaderAsync(
        string applicationName,
        string modelName,
        CancellationToken cancellationToken);

    Task WriteInfoAsync(string message, CancellationToken cancellationToken);

    Task WriteErrorAsync(string message, CancellationToken cancellationToken);

    Task WriteWarningAsync(string message, CancellationToken cancellationToken);

    Task WriteResponseAsync(
        string message,
        ConversationTurnMetrics? metrics,
        CancellationToken cancellationToken);
}
