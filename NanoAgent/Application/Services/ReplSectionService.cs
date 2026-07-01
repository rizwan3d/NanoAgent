using Microsoft.Extensions.Logging;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Formatting;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using System.Collections.Concurrent;

namespace NanoAgent.Application.Services;

internal sealed class ReplSectionService : IReplSectionService
{
    private readonly IConversationSectionStore _sectionStore;
    private readonly ISessionStore _sessionStore;
    private readonly IAgentProfileResolver _profileResolver;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sectionLocks = new(StringComparer.Ordinal);

    public ReplSectionService(
        IConversationSectionStore sectionStore,
        ISessionStore sessionStore,
        IApiKeySecretStore secretStore,
        IConversationProviderClient providerClient,
        IConversationResponseMapper responseMapper,
        IAgentProfileResolver profileResolver,
        ToolExecutionSettings toolExecutionSettings,
        TimeProvider timeProvider,
        ILogger<ReplSectionService> logger)
    {
        ArgumentNullException.ThrowIfNull(toolExecutionSettings);

        _sectionStore = sectionStore;
        _sessionStore = sessionStore;
        _profileResolver = profileResolver;
        _timeProvider = timeProvider;

        // Establish the configured tool-output default from agent-profile.json
        // (Application.Tools.toolOutput). The /tooloutput command and per-agent
        // profile preference still take precedence over this default.
        ToolOutputDisplay.ConfiguredDefaultFullToolOutput =
            ToolOutputDisplay.ParsePreference(toolExecutionSettings.ToolOutput);
    }

    public async Task<ReplSessionContext> CreateNewAsync(
        string applicationName,
        AgentProviderProfile providerProfile,
        string activeModelId,
        IReadOnlyList<string> availableModelIds,
        IAgentProfile agentProfile,
        IReadOnlyDictionary<string, int>? modelContextWindowTokens,
        string? activeProviderName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        ArgumentNullException.ThrowIfNull(providerProfile);
        ArgumentException.ThrowIfNullOrWhiteSpace(activeModelId);
        ArgumentNullException.ThrowIfNull(availableModelIds);
        ArgumentNullException.ThrowIfNull(agentProfile);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        ReplSessionContext session = new(
            applicationName,
            providerProfile,
            activeModelId,
            availableModelIds,
            sectionCreatedAtUtc: now,
            sectionUpdatedAtUtc: now,
            agentProfile: agentProfile,
            modelContextWindowTokens: modelContextWindowTokens,
            activeProviderName: activeProviderName);

        await _sectionStore.SaveAsync(
            session.CreateSectionSnapshot(now),
            cancellationToken);

        // Create a session record linking this section to a new session
        SessionRecord sessionRecord = SessionRecord.CreateWithSection(session.SectionId);
        await _sessionStore.SaveAsync(sessionRecord, cancellationToken);

        session.MarkSectionPersisted(now);
        return session;
    }

    public async Task<ReplSessionContext> CreateNewWithinSessionAsync(
        string applicationName,
        AgentProviderProfile providerProfile,
        string activeModelId,
        IReadOnlyList<string> availableModelIds,
        IAgentProfile agentProfile,
        IReadOnlyDictionary<string, int>? modelContextWindowTokens,
        string? activeProviderName,
        ReplSessionContext completedSection,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        ArgumentNullException.ThrowIfNull(providerProfile);
        ArgumentException.ThrowIfNullOrWhiteSpace(activeModelId);
        ArgumentNullException.ThrowIfNull(availableModelIds);
        ArgumentNullException.ThrowIfNull(agentProfile);
        ArgumentNullException.ThrowIfNull(completedSection);

        DateTimeOffset now = _timeProvider.GetUtcNow();

        // Extract completed context from the current section
        SessionContext completedContext = completedSection.CreateCompletedSectionContext();

        // Determine the parent session ID
        string? parentSessionId = completedSection.ParentSessionId;
        SessionContext accumulatedContext;

        if (parentSessionId is not null)
        {
            // Load existing session record and update it
            SessionRecord? existingSession = await _sessionStore.LoadAsync(parentSessionId, cancellationToken);
            if (existingSession is not null)
            {
                SessionRecord updatedSession = existingSession.WithNewSection(
                    Guid.NewGuid().ToString("D"),
                    completedContext,
                    now);
                await _sessionStore.SaveAsync(updatedSession, cancellationToken);
                accumulatedContext = updatedSession.AccumulatedContext;
            }
            else
            {
                // Session record missing; create a new one
                accumulatedContext = completedContext;
            }
        }
        else
        {
            // First section within a session; create the session record
            accumulatedContext = completedContext;
        }

        // Create the new section with the accumulated context
        ReplSessionContext session = new(
            applicationName,
            providerProfile,
            activeModelId,
            availableModelIds,
            sectionCreatedAtUtc: now,
            sectionUpdatedAtUtc: now,
            agentProfile: agentProfile,
            modelContextWindowTokens: modelContextWindowTokens,
            activeProviderName: activeProviderName,
            parentSessionId: parentSessionId,
            sessionContext: accumulatedContext);

        await _sectionStore.SaveAsync(
            session.CreateSectionSnapshot(now),
            cancellationToken);

        // Ensure the session record exists and links this new section
        if (parentSessionId is not null)
        {
            SessionRecord? existingSession = await _sessionStore.LoadAsync(parentSessionId, cancellationToken);
            if (existingSession is not null)
            {
                SessionRecord updatedSession = existingSession.WithUpdatedTimestamp(now);
                await _sessionStore.SaveAsync(updatedSession, cancellationToken);
            }
        }

        session.MarkSectionPersisted(now);
        return session;
    }

    public void EnsureTitleGenerationStarted(
        ReplSessionContext session,
        string firstUserPrompt)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (string.IsNullOrWhiteSpace(firstUserPrompt) ||
            !session.TryStartSectionTitleGeneration())
        {
            return;
        }

        session.RenameSection(
            CreateSectionTitleFromPrompt(firstUserPrompt),
            _timeProvider.GetUtcNow());
    }

    public async Task<ReplSessionContext> ResumeAsync(
        string applicationName,
        string sectionId,
        IAgentProfile? profileOverride,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionId);

        ConversationSectionSnapshot snapshot = await _sectionStore.LoadAsync(
                sectionId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"Section '{sectionId.Trim()}' was not found.");

        EnsureWorkspaceMatches(snapshot);

        // If the section has a parent session, try to load the accumulated context
        SessionContext? sessionContext = null;
        if (snapshot.ParentSessionId is not null)
        {
            SessionRecord? parentSession = await _sessionStore.LoadAsync(
                snapshot.ParentSessionId,
                cancellationToken);
            if (parentSession is not null)
            {
                sessionContext = parentSession.AccumulatedContext;
            }
        }

        ReplSessionContext session = new(
            applicationName,
            snapshot.ProviderProfile,
            snapshot.ActiveModelId,
            snapshot.AvailableModelIds,
            snapshot.SectionId,
            snapshot.Title,
            snapshot.CreatedAtUtc,
            snapshot.UpdatedAtUtc,
            snapshot.TotalEstimatedOutputTokens,
            snapshot.Turns,
            snapshot.PendingExecutionPlan,
            isResumedSection: true,
            agentProfile: profileOverride ?? _profileResolver.Resolve(snapshot.AgentProfileName),
            reasoningEffort: snapshot.ReasoningEffort,
            thinkingMode: snapshot.ThinkingMode,
            sessionState: snapshot.SessionState,
            workspacePath: snapshot.WorkspacePath,
            modelContextWindowTokens: snapshot.ModelContextWindowTokens,
            activeProviderName: snapshot.ActiveProviderName,
            parentSessionId: snapshot.ParentSessionId,
            sessionContext: sessionContext);

        if (!session.HasGeneratedSectionTitle &&
            session.TryGetFirstUserPrompt(out string? firstUserPrompt) &&
            !string.IsNullOrWhiteSpace(firstUserPrompt))
        {
            EnsureTitleGenerationStarted(session, firstUserPrompt);
        }

        return session;
    }

    private static void EnsureWorkspaceMatches(ConversationSectionSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.WorkspacePath))
        {
            return;
        }

        string currentWorkspacePath = NormalizeWorkspacePath(Directory.GetCurrentDirectory());
        string sectionWorkspacePath = NormalizeWorkspacePath(snapshot.WorkspacePath);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!string.Equals(currentWorkspacePath, sectionWorkspacePath, comparison))
        {
            throw new SectionWorkspaceMismatchException(
                currentWorkspacePath,
                sectionWorkspacePath);
        }
    }

    public async Task SaveIfDirtyAsync(
        ReplSessionContext session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        SemaphoreSlim sync = GetSectionLock(session.SectionId);
        await sync.WaitAsync(cancellationToken);

        try
        {
            if (!session.IsPersistedStateDirty)
            {
                return;
            }

            DateTimeOffset updatedAtUtc = _timeProvider.GetUtcNow();
            await _sectionStore.SaveAsync(
                session.CreateSectionSnapshot(updatedAtUtc),
                cancellationToken);

            session.MarkSectionPersisted(updatedAtUtc);
        }
        finally
        {
            sync.Release();
        }
    }

    public async Task StopAsync(
        ReplSessionContext session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        await SaveIfDirtyAsync(session, cancellationToken);
        session.DeleteTemporaryArtifacts(TemporaryArtifactRetention.Turn);
        session.DeleteTemporaryArtifacts(TemporaryArtifactRetention.Session);
    }

    private SemaphoreSlim GetSectionLock(string sectionId)
    {
        return _sectionLocks.GetOrAdd(
            sectionId,
            static _ => new SemaphoreSlim(1, 1));
    }

    private static string CreateSectionTitleFromPrompt(string title)
    {
        string normalizedTitle = title
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault()?
            .Trim()
            ?? string.Empty;

        normalizedTitle = string.Join(
            " ",
            normalizedTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return normalizedTitle.Length <= 80
            ? normalizedTitle
            : normalizedTitle[..77].TrimEnd() + "...";
    }

    private static string NormalizeWorkspacePath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(fullPath);

        if (!string.IsNullOrEmpty(root))
        {
            StringComparison comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (string.Equals(fullPath, root, comparison))
            {
                return fullPath;
            }
        }

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
