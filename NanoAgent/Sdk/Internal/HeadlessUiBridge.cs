using NanoAgent.Application.Models;
using NanoAgent.Application.UI;
using NanoAgent.Sdk.Events;

namespace NanoAgent.Sdk.Internal;

/// <summary>
/// Bridges the agent's <see cref="IUiBridge"/> contract to the event-driven
/// <see cref="NanoAgentClient"/> surface: progress callbacks are forwarded as
/// .NET events, while interactive prompts are routed to an optional
/// <see cref="IAgentInteractionHandler"/> or rejected with a clear error so a
/// headless host never blocks waiting on a console.
/// </summary>
internal sealed class HeadlessUiBridge : IUiBridge
{
    private readonly NanoAgentClient _client;
    private readonly IAgentInteractionHandler? _interactionHandler;

    public HeadlessUiBridge(
        NanoAgentClient client,
        IAgentInteractionHandler? interactionHandler)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _interactionHandler = interactionHandler;
    }

    public Task<T> RequestSelectionAsync<T>(
        SelectionPromptRequest<T> request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_interactionHandler is not null)
        {
            return _interactionHandler.ProvideSelectionAsync(request, cancellationToken);
        }

        throw new NanoAgentInteractionRequiredException(
            $"NanoAgent requested a selection ('{request.Title}') but the client is running headless. " +
            "Configure the provider and API key on NanoAgentClientBuilder so onboarding is skipped, " +
            "or supply an IAgentInteractionHandler via UseInteractionHandler(...).");
    }

    public Task<string> RequestTextAsync(
        TextPromptRequest request,
        bool isSecret,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_interactionHandler is not null)
        {
            return _interactionHandler.ProvideTextAsync(request, isSecret, cancellationToken);
        }

        throw new NanoAgentInteractionRequiredException(
            $"NanoAgent requested {(isSecret ? "a secret" : "text")} input ('{request.Label}') but the client is running headless. " +
            "Configure the provider and API key on NanoAgentClientBuilder so onboarding is skipped, " +
            "or supply an IAgentInteractionHandler via UseInteractionHandler(...).");
    }

    public void ShowError(string message)
    {
        _client.RaiseStatus(StatusMessageSeverity.Error, message);
    }

    public void ShowInfo(string message)
    {
        _client.RaiseStatus(StatusMessageSeverity.Info, message);
    }

    public void ShowSuccess(string message)
    {
        _client.RaiseStatus(StatusMessageSeverity.Success, message);
    }

    public void ShowAssistantReasoning(string reasoningText)
    {
        _client.RaiseReasoning(reasoningText);
    }

    public void ShowToolCalls(IReadOnlyList<ConversationToolCall> toolCalls)
    {
        _client.RaiseToolCalls(toolCalls);
    }

    public void ShowToolResults(ToolExecutionBatchResult toolExecutionResult)
    {
        _client.RaiseToolResults(toolExecutionResult);
    }

    public void ShowExecutionPlan(ExecutionPlanProgress progress)
    {
        _client.RaiseExecutionPlan(progress);
    }

    public void ShowProviderRetry(ProviderRetryProgress progress)
    {
        _client.RaiseProviderRetry(progress);
    }
}
