using FinalAgent.Application.Abstractions;
using FinalAgent.Application.Models;

namespace FinalAgent.Application.Repl.Services;

internal sealed class AgentConversationPipeline : IConversationPipeline
{
    public Task<ConversationTurnResult> ProcessAsync(
        string input,
        ReplSessionContext session,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        string response =
            $"Conversation pipeline received your request for model '{session.SelectedModelId}' " +
            $"on provider '{session.ProviderName}'. LLM request execution is the next layer to plug in.";

        return Task.FromResult(new ConversationTurnResult(response));
    }
}
