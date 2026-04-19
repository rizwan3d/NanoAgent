using FinalAgent.Application.Models;

namespace FinalAgent.Application.Abstractions;

public interface IConversationPipeline
{
    Task<ConversationTurnResult> ProcessAsync(
        string input,
        ReplSessionContext session,
        CancellationToken cancellationToken);
}
