using NanoAgent.Application.Models;

namespace NanoAgent.Application.Abstractions;

public interface IAgentTurnService
{
    Task<ConversationTurnResult> ProcessTurnAsync(
        string input,
        ReplSessionContext session,
        IConversationProgressSink progressSink,
        CancellationToken cancellationToken);
}
