using Microsoft.Extensions.Logging;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using System.Collections.Concurrent;

namespace NanoAgent.Application.Services;

internal sealed class ReplSectionService : IReplSectionService
{
    private const string SectionTitlePrompt =
        """
        You are generating a short title for a coding session from the user's first prompt.
        Capture the main engineering task, bug, feature, or subsystem in a human-readable way.
        Prefer specific nouns and verbs from the request; avoid generic fillers like Help, Task, Request, or Session.
        Requirements:
        - 2 to 6 words
        - plain text only
        - no quotes
        - no trailing punctuation
        - no markdown, labels, or explanations
        Respond with the title only.
        """;

    private static readonly TimeSpan TitleGenerationTimeout = TimeSpan.FromSeconds(30);

    private readonly IConversationSectionStore _sectionStore;
    private readonly ISessionStore _sessionStore;
    private readonly IApiKeySecretStore _secretStore;
    private readonly IConversationProviderClient _providerClient;
    private readonly IConversationResponseMapper _responseMapper;
    private readonly IAgentProfileResolver _profileResolver;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReplSectionService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sectionLocks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task> _pendingTitleTasks = new(StringComparer.Ordinal);

    public ReplSectionService(
        IConversationSectionStore sectionStore,
        ISessionStore sessionStore,
        IApiKeySecretStore secretStore,
        IConversationProviderClient providerClient,
        IConversationResponseMapper responseMapper,
        IAgentProfileResolver profileResolver,
        TimeProvider timeProvider,
        ILogger<ReplSectionService> logger)
    {
        _sectionStore = sectionStore;
        _sessionStore = sessionStore;
        _secretStore = secretStore;
        _providerClient = providerClient;
        _responseMapper = responseMapper;
        _profileResolver = profileResolver;
        _timeProvider = timeProvider;
        _logger = logger;
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

        Task task = GenerateAndPersistTitleAsync(
            session,
            firstUserPrompt.Trim());

        if (!_pendingTitleTasks.TryAdd(session.SectionId, task))
        {
            return;
        }

        _ = task.ContinueWith(
            _ => _pendingTitleTasks.TryRemove(session.SectionId, out Task? _),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
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

        if (_pendingTitleTasks.TryGetValue(session.SectionId, out Task? pendingTitleTask))
        {
            try
            {
                await pendingTitleTask.WaitAsync(TitleGenerationTimeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                _logger.LogDebug(
                    "Timed out while waiting for background section title generation for section {SectionId}.",
                    session.SectionId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
        }

        await SaveIfDirtyAsync(session, cancellationToken);
        session.DeleteTemporaryArtifacts(TemporaryArtifactRetention.Turn);
        session.DeleteTemporaryArtifacts(TemporaryArtifactRetention.Session);
    }

    private async Task GenerateAndPersistTitleAsync(
        ReplSessionContext session,
        string firstUserPrompt)
    {
        try
        {
            string? title = await GenerateTitleAsync(
                session,
                firstUserPrompt,
                CancellationToken.None);

            if (string.IsNullOrWhiteSpace(title))
            {
                return;
            }

            SemaphoreSlim sync = GetSectionLock(session.SectionId);
            await sync.WaitAsync(CancellationToken.None);

            try
            {
                if (session.HasGeneratedSectionTitle)
                {
                    return;
                }

                DateTimeOffset updatedAtUtc = _timeProvider.GetUtcNow();
                session.RenameSection(title, updatedAtUtc);

                await _sectionStore.SaveAsync(
                    session.CreateSectionSnapshot(updatedAtUtc),
                    CancellationToken.None);

                session.MarkSectionPersisted(updatedAtUtc);
            }
            finally
            {
                sync.Release();
            }
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "Failed to generate a background title for section {SectionId}.",
                session.SectionId);
        }
    }

    private async Task<string?> GenerateTitleAsync(
        ReplSessionContext session,
        string firstUserPrompt,
        CancellationToken cancellationToken)
    {
        string? apiKey = await LoadProviderSecretAsync(session, cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TitleGenerationTimeout);

        ConversationProviderPayload payload = await _providerClient.SendAsync(
            new ConversationProviderRequest(
                session.ProviderProfile,
                apiKey,
                session.ActiveModelId,
                [ConversationRequestMessage.User(firstUserPrompt)],
                SectionTitlePrompt,
                []),
            timeoutSource.Token);

        ConversationResponse response = _responseMapper.Map(payload);
        if (response.HasToolCalls || string.IsNullOrWhiteSpace(response.AssistantMessage))
        {
            return null;
        }

        return NormalizeGeneratedTitle(response.AssistantMessage);
    }

    private async Task<string?> LoadProviderSecretAsync(
        ReplSessionContext session,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(session.ActiveProviderName))
        {
            string? providerSecret = await _secretStore.LoadAsync(
                session.ActiveProviderName,
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(providerSecret))
            {
                return providerSecret;
            }
        }

        return await _secretStore.LoadAsync(cancellationToken) ??
            session.ProviderProfile.ProviderKind.GetDefaultApiKey();
    }

    private SemaphoreSlim GetSectionLock(string sectionId)
    {
        return _sectionLocks.GetOrAdd(
            sectionId,
            static _ => new SemaphoreSlim(1, 1));
    }

    private static string? NormalizeGeneratedTitle(string title)
    {
        string normalizedTitle = title
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault()?
            .Trim()
            .Trim('"', '\'', '.', '!', '?', ':', ';')
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            return null;
        }

        normalizedTitle = string.Join(
            " ",
            normalizedTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return normalizedTitle.Length <= 80
            ? normalizedTitle
            : normalizedTitle[..80].TrimEnd();
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
