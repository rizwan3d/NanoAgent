using NanoAgent.Application.Models;

namespace NanoAgent.Application.Abstractions;

public interface IConversationPipeline
{
    Task<ConversationTurnResult> ProcessAsync(
        string input,
        ReplSessionContext session,
        IConversationProgressSink progressSink,
        CancellationToken cancellationToken);

    Task<ConversationTurnResult> ProcessAsync(
        string input,
        ReplSessionContext session,
        IConversationProgressSink progressSink,
        IReadOnlyList<ConversationAttachment> attachments,
        CancellationToken cancellationToken);
}
