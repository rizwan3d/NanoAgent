using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;

namespace NanoAgent.Application.Services;

internal sealed class AgentTurnService : IAgentTurnService
{
    private readonly IConversationPipeline _conversationPipeline;

    public AgentTurnService(IConversationPipeline conversationPipeline)
    {
        _conversationPipeline = conversationPipeline;
    }

    public Task<ConversationTurnResult> ProcessTurnAsync(
        string input,
        ReplSessionContext session,
        IConversationProgressSink progressSink,
        CancellationToken cancellationToken)
    {
        return _conversationPipeline.ProcessAsync(
            input,
            session,
            progressSink,
            cancellationToken);
    }
}
