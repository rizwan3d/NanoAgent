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

    public Task<ConversationTurnResult> RunTurnAsync(
        AgentTurnRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _conversationPipeline.ProcessAsync(
            request.UserInput,
            request.Session,
            request.ProgressSink,
            cancellationToken);
    }
}
