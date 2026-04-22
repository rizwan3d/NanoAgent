using System.Collections.Concurrent;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Profiles;
using Microsoft.Extensions.Logging;
using NanoAgent.Domain.Models;

namespace NanoAgent.Application.Services;

internal sealed class ReplSectionService : IReplSectionService
{
    private const string SectionTitlePrompt =
        """
        You are naming a coding session.
        Create a concise title from the user's first prompt.
        Requirements:
        - 2 to 6 words
        - plain text only
        - no quotes
        - no trailing punctuation
        Respond with the title only.
        """;

    private static readonly TimeSpan TitleGenerationTimeout = TimeSpan.FromSeconds(30);

    private readonly IConversationSectionStore _sectionStore;
    private readonly IApiKeySecretStore _secretStore;
    private readonly IConversationProviderClient _providerClient;
    private readonly IConversationResponseMapper _responseMapper;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReplSectionService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sectionLocks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task> _pendingTitleTasks = new(StringComparer.Ordinal);

    public ReplSectionService(
        IConversationSectionStore sectionStore,
        IApiKeySecretStore secretStore,
        IConversationProviderClient providerClient,
        IConversationResponseMapper responseMapper,
        TimeProvider timeProvider,
        ILogger<ReplSectionService> logger)
    {
        _sectionStore = sectionStore;
        _secretStore = secretStore;
        _providerClient = providerClient;
        _responseMapper = responseMapper;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ReplSessionContext> CreateNewAsync(
        string applicationName,
        AgentProviderProfile providerProfile,
        string activeModelId,
        IReadOnlyList<string> availableModelIds,
        CancellationToken cancellationToken)
    {
        return await CreateNewAsync(
            applicationName,
            providerProfile,
            activeModelId,
            availableModelIds,
            BuiltInAgentProfiles.Build,
            cancellationToken);
    }

    public async Task<ReplSessionContext> CreateNewAsync(
        string applicationName,
        AgentProviderProfile providerProfile,
        string activeModelId,
        IReadOnlyList<string> availableModelIds,
        IAgentProfile agentProfile,
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
            agentProfile: agentProfile);

        await _sectionStore.SaveAsync(
            session.CreateSectionSnapshot(now),
            cancellationToken);

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
        CancellationToken cancellationToken)
    {
        return await ResumeAsync(
            applicationName,
            sectionId,
            profileOverride: null,
            cancellationToken);
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
            agentProfile: profileOverride ?? BuiltInAgentProfiles.Resolve(snapshot.AgentProfileName));

        if (!session.HasGeneratedSectionTitle &&
            session.TryGetFirstUserPrompt(out string? firstUserPrompt) &&
            !string.IsNullOrWhiteSpace(firstUserPrompt))
        {
            EnsureTitleGenerationStarted(session, firstUserPrompt);
        }

        return session;
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
        string? apiKey = await _secretStore.LoadAsync(cancellationToken);
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
}
