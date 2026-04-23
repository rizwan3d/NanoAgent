using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Presentation.Abstractions;

namespace NanoAgent.Presentation.Abstractions;

public interface IReplOutputWriter
{
    ValueTask<IResponseProgress> BeginResponseProgressAsync(
        int estimatedOutputTokens,
        int completedSessionEstimatedOutputTokens,
        CancellationToken cancellationToken);

    Task WriteSessionHeaderAsync(
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
