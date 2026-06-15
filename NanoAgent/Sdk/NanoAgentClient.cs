using NanoAgent.Application.Backend;
using NanoAgent.Application.Models;
using NanoAgent.Sdk.Events;
using NanoAgent.Sdk.Internal;

namespace NanoAgent.Sdk;

/// <summary>
/// Embeddable, headless entry point for driving a NanoAgent coding agent from
/// your own application (an app builder, a server, a bot, automation, …).
///
/// Create one with the fluent <see cref="CreateBuilder"/>, configure a provider
/// and model, call <see cref="InitializeAsync"/> once, then drive turns with
/// <see cref="RunTurnAsync(string, CancellationToken)"/>. Subscribe to the
/// progress events to surface reasoning, tool activity, and plans in your UI.
/// The completed assistant answer is returned from each turn.
/// </summary>
/// <example>
/// <code>
/// await using NanoAgentClient client = NanoAgentClient.CreateBuilder()
///     .UseAnthropic(apiKey, "claude-opus-4-8")
///     .WithWorkspace("/path/to/repo")
///     .AutoApproveTools()
///     .Build();
///
/// client.ToolCallsStarted += (_, e) => Console.WriteLine($"Running {e.ToolCalls.Count} tool(s)...");
///
/// await client.InitializeAsync(cancellationToken);
/// ConversationTurnResult result = await client.RunTurnAsync("Build a TODO app", cancellationToken);
/// Console.WriteLine(result.ResponseText);
/// </code>
/// </example>
public sealed class NanoAgentClient : IAsyncDisposable
{
    private readonly NanoAgentBackend _backend;
    private readonly HeadlessUiBridge _uiBridge;
    private bool _initialized;

    internal NanoAgentClient(
        NanoAgentBackend backend,
        IAgentInteractionHandler? interactionHandler)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _uiBridge = new HeadlessUiBridge(this, interactionHandler);
    }

    /// <summary>Raised when the agent emits intermediate reasoning text.</summary>
    public event EventHandler<AssistantReasoningEventArgs>? ReasoningReceived;

    /// <summary>Raised when the agent is about to execute a batch of tool calls.</summary>
    public event EventHandler<ToolCallsEventArgs>? ToolCallsStarted;

    /// <summary>Raised when a batch of tool calls completes.</summary>
    public event EventHandler<ToolResultsEventArgs>? ToolResultsReceived;

    /// <summary>Raised when the execution plan changes during planning mode.</summary>
    public event EventHandler<ExecutionPlanEventArgs>? ExecutionPlanUpdated;

    /// <summary>Raised when a provider request is retried.</summary>
    public event EventHandler<ProviderRetryEventArgs>? ProviderRetry;

    /// <summary>Raised when the agent surfaces an informational, success, or error message.</summary>
    public event EventHandler<StatusMessageEventArgs>? StatusMessage;

    /// <summary>Creates a new fluent builder for configuring a <see cref="NanoAgentClient"/>.</summary>
    public static NanoAgentClientBuilder CreateBuilder()
    {
        return new NanoAgentClientBuilder();
    }

    /// <summary>
    /// Initializes the underlying agent host, resolves the configured provider and
    /// model, and creates (or resumes) the session. Must be called once before any
    /// turn. Safe to await again; subsequent calls return the existing session.
    /// </summary>
    public async Task<NanoAgentSession> InitializeAsync(CancellationToken cancellationToken = default)
    {
        BackendSessionInfo info = await _backend.InitializeAsync(_uiBridge, cancellationToken)
            .ConfigureAwait(false);
        _initialized = true;
        return NanoAgentSession.FromBackend(info);
    }

    /// <summary>Runs a single agent turn for the given prompt and returns the completed result.</summary>
    public Task<ConversationTurnResult> RunTurnAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        return RunTurnAsync(prompt, [], cancellationToken);
    }

    /// <summary>Runs a single agent turn with attachments (for example files or images).</summary>
    public Task<ConversationTurnResult> RunTurnAsync(
        string prompt,
        IReadOnlyList<ConversationAttachment> attachments,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentNullException.ThrowIfNull(attachments);

        return _backend.RunTurnAsync(prompt, attachments, _uiBridge, cancellationToken);
    }

    /// <summary>
    /// Runs a slash command (for example <c>/model</c> or <c>/profile</c>) against
    /// the current session, mirroring the interactive REPL command surface.
    /// </summary>
    public Task<BackendCommandResult> RunCommandAsync(
        string commandText,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        ArgumentException.ThrowIfNullOrWhiteSpace(commandText);

        return _backend.RunCommandAsync(commandText, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        return _backend.DisposeAsync();
    }

    internal void RaiseReasoning(string reasoningText)
    {
        ReasoningReceived?.Invoke(this, new AssistantReasoningEventArgs(reasoningText));
    }

    internal void RaiseToolCalls(IReadOnlyList<ConversationToolCall> toolCalls)
    {
        ToolCallsStarted?.Invoke(this, new ToolCallsEventArgs(toolCalls));
    }

    internal void RaiseToolResults(ToolExecutionBatchResult results)
    {
        ToolResultsReceived?.Invoke(this, new ToolResultsEventArgs(results));
    }

    internal void RaiseExecutionPlan(ExecutionPlanProgress progress)
    {
        ExecutionPlanUpdated?.Invoke(this, new ExecutionPlanEventArgs(progress));
    }

    internal void RaiseProviderRetry(ProviderRetryProgress progress)
    {
        ProviderRetry?.Invoke(this, new ProviderRetryEventArgs(progress));
    }

    internal void RaiseStatus(StatusMessageSeverity severity, string message)
    {
        StatusMessage?.Invoke(this, new StatusMessageEventArgs(severity, message));
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException(
                "The NanoAgent client has not been initialized. Call InitializeAsync(...) first.");
        }
    }
}
