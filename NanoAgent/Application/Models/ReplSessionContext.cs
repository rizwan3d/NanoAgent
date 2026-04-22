using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Profiles;
using NanoAgent.Domain.Models;

namespace NanoAgent.Application.Models;

public sealed class ReplSessionContext
{
    private const string DefaultApplicationName = "NanoAgent";
    public const string DefaultSectionTitle = "Untitled section";
    private readonly HashSet<string> _availableModelIds;
    private List<WorkspaceFileEditTransaction>? _batchedFileEditTransactions;
    private readonly List<ConversationRequestMessage> _conversationHistory = [];
    private readonly Stack<WorkspaceFileEditTransaction> _redoFileEditTransactions = new();
    private bool _sectionTitleGenerationStarted;
    private readonly Stack<WorkspaceFileEditTransaction> _undoFileEditTransactions = new();
    private readonly List<PermissionRule> _permissionOverrides = [];

    public ReplSessionContext(
        AgentProviderProfile providerProfile,
        string activeModelId,
        IReadOnlyList<string> availableModelIds,
        IAgentProfile? agentProfile = null,
        string? reasoningEffort = null)
        : this(
            DefaultApplicationName,
            providerProfile,
            activeModelId,
            availableModelIds,
            agentProfile: agentProfile,
            reasoningEffort: reasoningEffort)
    {
    }

    public ReplSessionContext(
        string applicationName,
        AgentProviderProfile providerProfile,
        string activeModelId,
        IReadOnlyList<string> availableModelIds,
        string? sectionId = null,
        string? sectionTitle = null,
        DateTimeOffset? sectionCreatedAtUtc = null,
        DateTimeOffset? sectionUpdatedAtUtc = null,
        int totalEstimatedOutputTokens = 0,
        IReadOnlyList<ConversationSectionTurn>? conversationTurns = null,
        PendingExecutionPlan? pendingExecutionPlan = null,
        bool isResumedSection = false,
        IAgentProfile? agentProfile = null,
        string? reasoningEffort = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        ArgumentNullException.ThrowIfNull(providerProfile);
        ArgumentException.ThrowIfNullOrWhiteSpace(activeModelId);
        ArgumentNullException.ThrowIfNull(availableModelIds);

        if (totalEstimatedOutputTokens < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalEstimatedOutputTokens));
        }

        ApplicationName = applicationName.Trim();
        AgentProfile = agentProfile ?? BuiltInAgentProfiles.Build;
        ProviderProfile = providerProfile;
        AvailableModelIds = availableModelIds
            .Where(static modelId => !string.IsNullOrWhiteSpace(modelId))
            .Select(static modelId => modelId.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (AvailableModelIds.Count == 0)
        {
            throw new ArgumentException(
                "At least one available model must be provided.",
                nameof(availableModelIds));
        }

        _availableModelIds = new HashSet<string>(AvailableModelIds, StringComparer.Ordinal);

        string normalizedActiveModelId = activeModelId.Trim();
        if (!_availableModelIds.Contains(normalizedActiveModelId))
        {
            throw new ArgumentException(
                "The active model must exist in the available model set.",
                nameof(activeModelId));
        }

        ActiveModelId = normalizedActiveModelId;
        ReasoningEffort = ReasoningEffortOptions.NormalizeOrThrow(reasoningEffort);
        SectionId = NormalizeSectionId(sectionId);
        SectionTitle = NormalizeSectionTitle(sectionTitle);
        SectionCreatedAtUtc = sectionCreatedAtUtc ?? DateTimeOffset.UtcNow;
        SectionUpdatedAtUtc = sectionUpdatedAtUtc ?? SectionCreatedAtUtc;
        TotalEstimatedOutputTokens = totalEstimatedOutputTokens;
        IsResumedSection = isResumedSection;
        PendingExecutionPlan = pendingExecutionPlan;

        if (SectionUpdatedAtUtc < SectionCreatedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(sectionUpdatedAtUtc));
        }

        if (conversationTurns is null)
        {
            return;
        }

        foreach (ConversationSectionTurn turn in conversationTurns.Where(static turn => turn is not null))
        {
            _conversationHistory.Add(ConversationRequestMessage.User(turn.UserInput));
            _conversationHistory.Add(ConversationRequestMessage.AssistantMessage(turn.AssistantResponse));
        }
    }

    public string ApplicationName { get; }

    public string ActiveModelId { get; private set; }

    public IAgentProfile AgentProfile { get; private set; }

    public string AgentProfileName => AgentProfile.Name;

    public IReadOnlyList<string> AvailableModelIds { get; }

    public AgentProviderProfile ProviderProfile { get; }

    public string ProviderName => ProviderProfile.ProviderKind.ToDisplayName();

    public string? ReasoningEffort { get; private set; }

    public bool HasGeneratedSectionTitle =>
        !string.Equals(SectionTitle, DefaultSectionTitle, StringComparison.Ordinal);

    public bool IsPersistedStateDirty { get; private set; }

    public bool IsResumedSection { get; }

    public bool HasPendingExecutionPlan => PendingExecutionPlan is not null;

    public IReadOnlyList<ConversationRequestMessage> ConversationHistory => _conversationHistory;

    public PendingExecutionPlan? PendingExecutionPlan { get; private set; }

    public IReadOnlyList<PermissionRule> PermissionOverrides => _permissionOverrides;

    public DateTimeOffset SectionCreatedAtUtc { get; }

    public string SectionId { get; }

    public string SessionId => SectionId;

    public string SectionTitle { get; private set; }

    public DateTimeOffset SectionUpdatedAtUtc { get; private set; }

    public string SectionResumeCommand => $"nano --section {SectionId}";

    public int TotalEstimatedOutputTokens { get; private set; }

    public IDisposable BeginFileEditTransactionBatch()
    {
        if (_batchedFileEditTransactions is not null)
        {
            throw new InvalidOperationException("A file edit transaction batch is already active.");
        }

        _batchedFileEditTransactions = [];
        return new FileEditTransactionBatchScope(this);
    }

    public void AddPermissionOverride(PermissionRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        _permissionOverrides.Add(rule);
    }

    public void ClearPendingExecutionPlan()
    {
        if (PendingExecutionPlan is null)
        {
            return;
        }

        PendingExecutionPlan = null;
        IsPersistedStateDirty = true;
    }

    public bool ContainsModel(string modelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        return _availableModelIds.Contains(modelId.Trim());
    }

    public void SetActiveModel(string modelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        string normalizedModelId = modelId.Trim();
        if (!_availableModelIds.Contains(normalizedModelId))
        {
            throw new InvalidOperationException(
                $"Model '{normalizedModelId}' is not available in the current session.");
        }

        if (string.Equals(ActiveModelId, normalizedModelId, StringComparison.Ordinal))
        {
            return;
        }

        ActiveModelId = normalizedModelId;
        IsPersistedStateDirty = true;
    }

    public bool SetReasoningEffort(string? reasoningEffort)
    {
        string? normalizedReasoningEffort = ReasoningEffortOptions.NormalizeOrThrow(reasoningEffort);
        if (string.Equals(ReasoningEffort, normalizedReasoningEffort, StringComparison.Ordinal))
        {
            return false;
        }

        ReasoningEffort = normalizedReasoningEffort;
        IsPersistedStateDirty = true;
        return true;
    }

    public bool ClearReasoningEffort()
    {
        return SetReasoningEffort(null);
    }

    public void SetAgentProfile(IAgentProfile agentProfile)
    {
        ArgumentNullException.ThrowIfNull(agentProfile);

        if (string.Equals(AgentProfile.Name, agentProfile.Name, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        AgentProfile = agentProfile;
        IsPersistedStateDirty = true;
    }

    public int AddEstimatedOutputTokens(int estimatedOutputTokens)
    {
        if (estimatedOutputTokens < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(estimatedOutputTokens));
        }

        TotalEstimatedOutputTokens += estimatedOutputTokens;
        IsPersistedStateDirty = true;
        return TotalEstimatedOutputTokens;
    }

    public void AddConversationTurn(
        string userInput,
        string assistantResponse)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userInput);
        ArgumentException.ThrowIfNullOrWhiteSpace(assistantResponse);

        _conversationHistory.Add(ConversationRequestMessage.User(userInput.Trim()));
        _conversationHistory.Add(ConversationRequestMessage.AssistantMessage(assistantResponse.Trim()));
        IsPersistedStateDirty = true;
    }

    public ConversationSectionSnapshot CreateSectionSnapshot(DateTimeOffset updatedAtUtc)
    {
        if (updatedAtUtc < SectionCreatedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(updatedAtUtc));
        }

        if (_conversationHistory.Count % 2 != 0)
        {
            throw new InvalidOperationException(
                "Conversation history must contain complete user/assistant turns before it can be persisted.");
        }

        List<ConversationSectionTurn> turns = [];
        for (int index = 0; index < _conversationHistory.Count; index += 2)
        {
            ConversationRequestMessage userMessage = _conversationHistory[index];
            ConversationRequestMessage assistantMessage = _conversationHistory[index + 1];

            if (!string.Equals(userMessage.Role, "user", StringComparison.Ordinal) ||
                !string.Equals(assistantMessage.Role, "assistant", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(userMessage.Content) ||
                string.IsNullOrWhiteSpace(assistantMessage.Content))
            {
                throw new InvalidOperationException(
                    "Conversation history contains an unsupported message layout for section persistence.");
            }

            turns.Add(new ConversationSectionTurn(
                userMessage.Content,
                assistantMessage.Content));
        }

        return new ConversationSectionSnapshot(
            SectionId,
            SectionTitle,
            SectionCreatedAtUtc,
            updatedAtUtc,
            ProviderProfile,
            ActiveModelId,
            AvailableModelIds,
            turns,
            TotalEstimatedOutputTokens,
            PendingExecutionPlan,
            AgentProfile.Name,
            ReasoningEffort);
    }

    public IReadOnlyList<ConversationRequestMessage> GetConversationHistory(int maxHistoryTurns)
    {
        if (maxHistoryTurns <= 0 || _conversationHistory.Count == 0)
        {
            return [];
        }

        int maxMessageCount = checked(maxHistoryTurns * 2);
        if (_conversationHistory.Count <= maxMessageCount)
        {
            return _conversationHistory.ToArray();
        }

        return _conversationHistory
            .Skip(_conversationHistory.Count - maxMessageCount)
            .ToArray();
    }

    public void RecordFileEditTransaction(WorkspaceFileEditTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        if (_batchedFileEditTransactions is not null)
        {
            _batchedFileEditTransactions.Add(transaction);
            return;
        }

        _undoFileEditTransactions.Push(transaction);
        _redoFileEditTransactions.Clear();
    }

    public bool TryGetPendingUndoFileEdit(out WorkspaceFileEditTransaction? transaction)
    {
        if (_undoFileEditTransactions.Count == 0)
        {
            transaction = null;
            return false;
        }

        transaction = _undoFileEditTransactions.Peek();
        return true;
    }

    public void CompleteUndoFileEdit()
    {
        if (_undoFileEditTransactions.Count == 0)
        {
            throw new InvalidOperationException("There is no file edit transaction to undo.");
        }

        WorkspaceFileEditTransaction transaction = _undoFileEditTransactions.Pop();
        _redoFileEditTransactions.Push(transaction);
    }

    public bool TryGetPendingRedoFileEdit(out WorkspaceFileEditTransaction? transaction)
    {
        if (_redoFileEditTransactions.Count == 0)
        {
            transaction = null;
            return false;
        }

        transaction = _redoFileEditTransactions.Peek();
        return true;
    }

    public void CompleteRedoFileEdit()
    {
        if (_redoFileEditTransactions.Count == 0)
        {
            throw new InvalidOperationException("There is no file edit transaction to redo.");
        }

        WorkspaceFileEditTransaction transaction = _redoFileEditTransactions.Pop();
        _undoFileEditTransactions.Push(transaction);
    }

    public void MarkSectionPersisted(DateTimeOffset updatedAtUtc)
    {
        if (updatedAtUtc < SectionCreatedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(updatedAtUtc));
        }

        SectionUpdatedAtUtc = updatedAtUtc;
        IsPersistedStateDirty = false;
    }

    public void SetPendingExecutionPlan(PendingExecutionPlan pendingExecutionPlan)
    {
        ArgumentNullException.ThrowIfNull(pendingExecutionPlan);

        if (PendingExecutionPlan is not null &&
            string.Equals(PendingExecutionPlan.SourceUserInput, pendingExecutionPlan.SourceUserInput, StringComparison.Ordinal) &&
            string.Equals(PendingExecutionPlan.PlanningSummary, pendingExecutionPlan.PlanningSummary, StringComparison.Ordinal) &&
            PendingExecutionPlan.Tasks.SequenceEqual(pendingExecutionPlan.Tasks, StringComparer.Ordinal))
        {
            return;
        }

        PendingExecutionPlan = pendingExecutionPlan;
        IsPersistedStateDirty = true;
    }

    public void RenameSection(
        string title,
        DateTimeOffset updatedAtUtc)
    {
        if (updatedAtUtc < SectionCreatedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(updatedAtUtc));
        }

        string normalizedTitle = NormalizeSectionTitle(title);
        if (string.Equals(SectionTitle, normalizedTitle, StringComparison.Ordinal))
        {
            return;
        }

        SectionTitle = normalizedTitle;
        SectionUpdatedAtUtc = updatedAtUtc;
        IsPersistedStateDirty = true;
    }

    public bool TryGetFirstUserPrompt(out string? prompt)
    {
        prompt = _conversationHistory
            .FirstOrDefault(static message => string.Equals(message.Role, "user", StringComparison.Ordinal))
            ?.Content;

        return !string.IsNullOrWhiteSpace(prompt);
    }

    public bool TryStartSectionTitleGeneration()
    {
        if (HasGeneratedSectionTitle || _sectionTitleGenerationStarted)
        {
            return false;
        }

        _sectionTitleGenerationStarted = true;
        return true;
    }

    private void CompleteFileEditTransactionBatch()
    {
        List<WorkspaceFileEditTransaction>? batch = _batchedFileEditTransactions;
        _batchedFileEditTransactions = null;

        if (batch is null || batch.Count == 0)
        {
            return;
        }

        WorkspaceFileEditTransaction transaction = batch.Count == 1
            ? batch[0]
            : MergeTransactions(batch);

        _undoFileEditTransactions.Push(transaction);
        _redoFileEditTransactions.Clear();
    }

    private static WorkspaceFileEditTransaction MergeTransactions(
        IReadOnlyList<WorkspaceFileEditTransaction> transactions)
    {
        Dictionary<string, WorkspaceFileEditState> firstBeforeStates = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, WorkspaceFileEditState> lastAfterStates = new(StringComparer.OrdinalIgnoreCase);
        List<string> orderedPaths = [];

        foreach (WorkspaceFileEditTransaction transaction in transactions)
        {
            foreach (WorkspaceFileEditState state in transaction.BeforeStates)
            {
                if (firstBeforeStates.TryAdd(state.Path, state))
                {
                    orderedPaths.Add(state.Path);
                }
            }

            foreach (WorkspaceFileEditState state in transaction.AfterStates)
            {
                if (!lastAfterStates.ContainsKey(state.Path) &&
                    !orderedPaths.Contains(state.Path, StringComparer.OrdinalIgnoreCase))
                {
                    orderedPaths.Add(state.Path);
                }

                lastAfterStates[state.Path] = state;
            }
        }

        WorkspaceFileEditState[] beforeStates = orderedPaths
            .Where(firstBeforeStates.ContainsKey)
            .Select(path => firstBeforeStates[path])
            .ToArray();
        WorkspaceFileEditState[] afterStates = orderedPaths
            .Where(lastAfterStates.ContainsKey)
            .Select(path => lastAfterStates[path])
            .ToArray();
        int fileCount = orderedPaths.Count;

        return new WorkspaceFileEditTransaction(
            $"tool round ({transactions.Count} edits across {fileCount} {(fileCount == 1 ? "file" : "files")})",
            beforeStates,
            afterStates);
    }

    private static string NormalizeSectionId(string? sectionId)
    {
        if (string.IsNullOrWhiteSpace(sectionId))
        {
            return Guid.NewGuid().ToString("D");
        }

        if (!Guid.TryParse(sectionId.Trim(), out Guid parsedSectionId))
        {
            throw new ArgumentException(
                "Section id must be a valid GUID.",
                nameof(sectionId));
        }

        return parsedSectionId.ToString("D");
    }

    private static string NormalizeSectionTitle(string? sectionTitle)
    {
        return string.IsNullOrWhiteSpace(sectionTitle)
            ? DefaultSectionTitle
            : sectionTitle.Trim();
    }

    private sealed class FileEditTransactionBatchScope : IDisposable
    {
        private ReplSessionContext? _session;

        public FileEditTransactionBatchScope(ReplSessionContext session)
        {
            _session = session;
        }

        public void Dispose()
        {
            ReplSessionContext? session = _session;
            if (session is null)
            {
                return;
            }

            _session = null;
            session.CompleteFileEditTransactionBatch();
        }
    }
}
