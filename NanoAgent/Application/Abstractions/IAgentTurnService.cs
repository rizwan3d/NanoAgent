using NanoAgent.Application.Models;

namespace NanoAgent.Application.Abstractions;

public interface IAgentTurnService
{
    Task<ConversationTurnResult> RunTurnAsync(
        AgentTurnRequest request,
        CancellationToken cancellationToken);
}
