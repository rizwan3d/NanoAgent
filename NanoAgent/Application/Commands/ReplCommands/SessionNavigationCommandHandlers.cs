using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using System.Globalization;

namespace NanoAgent.Application.Commands;

internal sealed class NewSessionCommandHandler : IReplCommandHandler
{
    private readonly ISessionAppService _sessionAppService;

    public NewSessionCommandHandler(ISessionAppService sessionAppService)
    {
        _sessionAppService = sessionAppService;
    }

    public string CommandName => "new";

    public string Description => "Start a new session.";

    public string Usage => "/new";

    public async Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        ReplSessionContext current = context.Session;
        ReplSessionContext next = await _sessionAppService.CreateAsync(
            new CreateSessionRequest(
                current.ProviderProfile,
                current.ActiveModelId,
                current.AvailableModelIds,
                current.AgentProfile.Name,
                current.ReasoningEffort,
                current.ModelContextWindowTokens),
            cancellationToken);

        return ReplCommandResult.SwitchSession(
            next,
            $"Started new session.\nSession: {next.SessionId}\nResume command: {next.SectionResumeCommand}");
    }
}

internal sealed class ResumeCommandHandler : IReplCommandHandler
{
    private readonly ISelectionPrompt _selectionPrompt;
    private readonly ISessionAppService _sessionAppService;

    public ResumeCommandHandler(
        ISelectionPrompt selectionPrompt,
        ISessionAppService sessionAppService)
    {
        _selectionPrompt = selectionPrompt;
        _sessionAppService = sessionAppService;
    }

    public string CommandName => "resume";

    public string Description => "Resume a different session.";

    public string Usage => "/resume [session-id]";

    public async Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        string sessionId;
        if (context.Arguments.Count > 0)
        {
            if (!SessionCommandSupport.TryNormalizeSessionId(context.Arguments[0], out sessionId))
            {
                return ReplCommandResult.Continue(
                    "Session id must be a GUID. Usage: /resume [session-id]",
                    ReplFeedbackKind.Error);
            }
        }
        else
        {
            SessionSummary? selected = await PromptForSessionAsync(
                "Resume session",
                "Choose a saved session. Esc cancels resume.",
                context.Session.SessionId,
                cancellationToken);
            if (selected is null)
            {
                return ReplCommandResult.Continue("Resume cancelled.", ReplFeedbackKind.Warning);
            }

            sessionId = selected.SessionId;
        }

        if (string.Equals(sessionId, context.Session.SessionId, StringComparison.OrdinalIgnoreCase))
        {
            return ReplCommandResult.SwitchSession(
                context.Session,
                $"Already in session: {context.Session.SectionTitle}\nSession: {context.Session.SessionId}");
        }

        ReplSessionContext resumed;
        try
        {
            resumed = await _sessionAppService.ResumeAsync(
                new ResumeSessionRequest(sessionId),
                cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return ReplCommandResult.Continue(exception.Message, ReplFeedbackKind.Error);
        }

        return ReplCommandResult.SwitchSession(
            resumed,
            $"Resumed session: {resumed.SectionTitle}\nSession: {resumed.SessionId}");
    }

    internal async Task<SessionSummary?> PromptForSessionAsync(
        string title,
        string description,
        string currentSessionId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SessionSummary> sessions = await _sessionAppService.ListAsync(cancellationToken);
        if (sessions.Count == 0)
        {
            return null;
        }

        try
        {
            return await _selectionPrompt.PromptAsync(
                new SelectionPromptRequest<SessionSummary>(
                    title,
                    sessions
                        .Select(session =>
                        {
                            bool isCurrent = string.Equals(
                                session.SessionId,
                                currentSessionId,
                                StringComparison.OrdinalIgnoreCase);

                            return new SelectionPromptOption<SessionSummary>(
                                FormatSessionLabel(session, isCurrent),
                                session,
                                $"{session.ProviderName} / {session.ActiveModelId}. Updated {SessionCommandSupport.FormatTimestamp(session.UpdatedAtUtc)}.");
                        })
                        .ToArray(),
                    description,
                    DefaultIndex: GetCurrentSessionIndex(sessions, currentSessionId),
                    AllowCancellation: true),
                cancellationToken);
        }
        catch (PromptCancelledException)
        {
            return null;
        }
    }

    private static int GetCurrentSessionIndex(
        IReadOnlyList<SessionSummary> sessions,
        string currentSessionId)
    {
        for (int index = 0; index < sessions.Count; index++)
        {
            if (string.Equals(sessions[index].SessionId, currentSessionId, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return 0;
    }

    private static string FormatSessionLabel(SessionSummary session, bool isCurrent)
    {
        string prefix = isCurrent ? "Current: " : string.Empty;
        return prefix + session.Title + " (" + session.SessionId[..8] + ")";
    }
}

internal sealed class TreeCommandHandler : IReplCommandHandler
{
    private readonly ResumeCommandHandler _resumeCommandHandler;

    public TreeCommandHandler(ResumeCommandHandler resumeCommandHandler)
    {
        _resumeCommandHandler = resumeCommandHandler;
    }

    public string CommandName => "tree";

    public string Description => "Navigate the session tree and switch branches.";

    public string Usage => "/tree";

    public async Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        SessionSummary? selected = await _resumeCommandHandler.PromptForSessionAsync(
            "Session tree",
            "Saved sessions and forks are listed newest first. Esc returns to the current session.",
            context.Session.SessionId,
            cancellationToken);
        if (selected is null)
        {
            return ReplCommandResult.Continue("Session tree cancelled.", ReplFeedbackKind.Warning);
        }

        return await _resumeCommandHandler.ExecuteAsync(
            new ReplCommandContext(
                "resume",
                selected.SessionId,
                [selected.SessionId],
                "/resume " + selected.SessionId,
                context.Session),
            cancellationToken);
    }
}

internal sealed class CloneCommandHandler : IReplCommandHandler
{
    private readonly IConversationSectionStore _sectionStore;
    private readonly ISessionAppService _sessionAppService;

    public CloneCommandHandler(
        IConversationSectionStore sectionStore,
        ISessionAppService sessionAppService)
    {
        _sectionStore = sectionStore;
        _sessionAppService = sessionAppService;
    }

    public string CommandName => "clone";

    public string Description => "Duplicate the current session at the current position.";

    public string Usage => "/clone";

    public async Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        ConversationSectionSnapshot snapshot = SessionCommandSupport.CreateCopySnapshot(
            context.Session,
            SessionCommandSupport.CreateTitleWithSuffix(context.Session.SectionTitle, "clone"),
            context.Session.ConversationTurns,
            context.Session.TotalEstimatedOutputTokens,
            includeState: true);
        ReplSessionContext cloned = await SessionCommandSupport.SaveAndResumeAsync(
            snapshot,
            _sectionStore,
            _sessionAppService,
            cancellationToken);

        return ReplCommandResult.SwitchSession(
            cloned,
            $"Cloned session.\nSession: {cloned.SessionId}");
    }
}

internal sealed class ForkCommandHandler : IReplCommandHandler
{
    private readonly IConversationSectionStore _sectionStore;
    private readonly ISelectionPrompt _selectionPrompt;
    private readonly ISessionAppService _sessionAppService;

    public ForkCommandHandler(
        IConversationSectionStore sectionStore,
        ISelectionPrompt selectionPrompt,
        ISessionAppService sessionAppService)
    {
        _sectionStore = sectionStore;
        _selectionPrompt = selectionPrompt;
        _sessionAppService = sessionAppService;
    }

    public string CommandName => "fork";

    public string Description => "Create a new fork from a previous user message.";

    public string Usage => "/fork [turn-number]";

    public async Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        IReadOnlyList<ConversationSectionTurn> turns = context.Session.ConversationTurns;
        if (turns.Count == 0)
        {
            return ReplCommandResult.Continue(
                "No previous user messages are available to fork from.",
                ReplFeedbackKind.Warning);
        }

        int turnNumber;
        if (context.Arguments.Count > 0)
        {
            if (!int.TryParse(context.Arguments[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out turnNumber) ||
                turnNumber < 1 ||
                turnNumber > turns.Count)
            {
                return ReplCommandResult.Continue(
                    $"Turn number must be between 1 and {turns.Count.ToString(CultureInfo.InvariantCulture)}.",
                    ReplFeedbackKind.Error);
            }
        }
        else
        {
            int? selectedTurn = await PromptForTurnAsync(turns, cancellationToken);
            if (selectedTurn is null)
            {
                return ReplCommandResult.Continue("Fork cancelled.", ReplFeedbackKind.Warning);
            }

            turnNumber = selectedTurn.Value;
        }

        ConversationSectionTurn[] retainedTurns = turns
            .Take(turnNumber - 1)
            .ToArray();
        ConversationSectionSnapshot snapshot = SessionCommandSupport.CreateCopySnapshot(
            context.Session,
            SessionCommandSupport.CreateTitleWithSuffix(context.Session.SectionTitle, "fork"),
            retainedTurns,
            totalEstimatedOutputTokens: 0,
            includeState: false);
        ReplSessionContext forked = await SessionCommandSupport.SaveAndResumeAsync(
            snapshot,
            _sectionStore,
            _sessionAppService,
            cancellationToken);

        return ReplCommandResult.SwitchSession(
            forked,
            $"Forked before turn {turnNumber.ToString(CultureInfo.InvariantCulture)}.\nSession: {forked.SessionId}");
    }

    private async Task<int?> PromptForTurnAsync(
        IReadOnlyList<ConversationSectionTurn> turns,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _selectionPrompt.PromptAsync(
                new SelectionPromptRequest<int>(
                    "Fork from user message",
                    turns
                        .Select((turn, index) => new SelectionPromptOption<int>(
                            (index + 1).ToString(CultureInfo.InvariantCulture) + ". " +
                                SessionCommandSupport.CreatePreview(turn.UserInput),
                            index + 1,
                            "Create the fork before this user message."))
                        .ToArray(),
                    "Esc cancels fork.",
                    DefaultIndex: turns.Count - 1,
                    AllowCancellation: true),
                cancellationToken);
        }
        catch (PromptCancelledException)
        {
            return null;
        }
    }
}
