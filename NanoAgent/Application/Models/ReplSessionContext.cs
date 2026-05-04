using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Profiles;
using NanoAgent.Application.Utilities;
using NanoAgent.Domain.Models;
using System.Globalization;
using System.Text;

namespace NanoAgent.Application.Models;

public sealed class ReplSessionContext
{
    private const string DefaultApplicationName = "NanoAgent";
    private const int MaxFileContextEntries = 40;
    private const int MaxEditContextEntries = 40;
    private const int MaxTerminalHistoryEntries = 40;
    private const int MaxStateTextCharacters = 1_500;
    private const int MaxPromptFileContextEntries = 16;
    private const int MaxPromptEditContextEntries = 12;
    private const int MaxPromptTerminalHistoryEntries = 12;
    private const int MaxPromptFieldCharacters = 600;
    private const int MaxCompactedHistoryTurns = 8;
    private const int MaxCompactedHistoryCharacters = 4_000;
    private const int MaxCompactedHistoryFieldCharacters = 500;
    private readonly object _syncRoot = new();
    public const string DefaultSectionTitle = "Untitled section";
    private readonly HashSet<string> _availableModelIds;
    private Dictionary<string, int> _modelContextWindowTokens;
    private List<WorkspaceFileEditTransaction>? _batchedFileEditTransactions;
    private readonly List<ConversationRequestMessage> _conversationHistory = [];
    private readonly List<ConversationSectionTurn> _conversationTurns = [];
    private readonly List<SessionEditContext> _editContexts = [];
    private readonly List<SessionFileContext> _fileContexts = [];
    private readonly Stack<WorkspaceFileEditTransaction> _redoFileEditTransactions = new();
    private readonly List<SessionTerminalCommand> _terminalHistory = [];
    private readonly List<TemporaryArtifactReference> _temporaryArtifacts = [];
    private bool _sectionTitleGenerationStarted;
    private readonly Stack<WorkspaceFileEditTransaction> _undoFileEditTransactions = new();
    private readonly List<PermissionRule> _permissionOverrides = [];

    public ReplSessionContext(
        AgentProviderProfile providerProfile,
        string activeModelId,
        IReadOnlyList<string> availableModelIds,
        IAgentProfile? agentProfile = null,
        string? reasoningEffort = null,
        string? workspacePath = null,
        IReadOnlyDictionary<string, int>? modelContextWindowTokens = null)
        : this(
            DefaultApplicationName,
            providerProfile,
            activeModelId,
            availableModelIds,
            agentProfile: agentProfile,
            reasoningEffort: reasoningEffort,
            workspacePath: workspacePath,
            modelContextWindowTokens: modelContextWindowTokens)
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
        string? reasoningEffort = null,
        SessionStateSnapshot? sessionState = null,
        string? workspacePath = null,
        IReadOnlyDictionary<string, int>? modelContextWindowTokens = null)
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
        AvailableModelIds = NormalizeAvailableModelIds(availableModelIds);
        _modelContextWindowTokens = NormalizeModelContextWindowTokens(
            modelContextWindowTokens,
            AvailableModelIds);

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
        WorkspacePath = NormalizeWorkspacePath(workspacePath);
        SectionId = NormalizeSectionId(sectionId);
        SectionTitle = NormalizeSectionTitle(sectionTitle);
        SectionCreatedAtUtc = sectionCreatedAtUtc ?? DateTimeOffset.UtcNow;
        SectionUpdatedAtUtc = sectionUpdatedAtUtc ?? SectionCreatedAtUtc;
        TotalEstimatedOutputTokens = totalEstimatedOutputTokens;
        IsResumedSection = isResumedSection;
        PendingExecutionPlan = pendingExecutionPlan;
        RestoreSessionState(sessionState);

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
            _conversationTurns.Add(turn);
            _conversationHistory.Add(ConversationRequestMessage.User(turn.UserInput));
            _conversationHistory.Add(ConversationRequestMessage.AssistantMessage(turn.AssistantResponse));
        }
    }

    public string ApplicationName { get; }

    public string ActiveModelId { get; private set; }

    public IAgentProfile AgentProfile { get; private set; }

    public string AgentProfileName => AgentProfile.Name;

    public IReadOnlyList<string> AvailableModelIds { get; private set; }

    public AgentProviderProfile ProviderProfile { get; private set; }

    public string ProviderName => ProviderProfile.ProviderKind.ToDisplayName();

    public IReadOnlyDictionary<string, int> ModelContextWindowTokens => _modelContextWindowTokens;

    public int? ActiveModelContextWindowTokens => _modelContextWindowTokens.TryGetValue(
        ActiveModelId,
        out int contextWindowTokens)
            ? contextWindowTokens
            : null;

    public string? ReasoningEffort { get; private set; }

    public bool HasGeneratedSectionTitle =>
        !string.Equals(SectionTitle, DefaultSectionTitle, StringComparison.Ordinal);

    public bool IsPersistedStateDirty { get; private set; }

    public bool IsResumedSection { get; }

    public bool HasPendingExecutionPlan => PendingExecutionPlan is not null;

    public IReadOnlyList<ConversationRequestMessage> ConversationHistory => _conversationHistory;

    public IReadOnlyList<ConversationSectionTurn> ConversationTurns => _conversationTurns.ToArray();

    public PendingExecutionPlan? PendingExecutionPlan { get; private set; }

    public IReadOnlyList<PermissionRule> PermissionOverrides
    {
        get
        {
            lock (_syncRoot)
            {
                return _permissionOverrides.ToArray();
            }
        }
    }

    public DateTimeOffset SectionCreatedAtUtc { get; }

    public string SectionId { get; }

    public string SessionId => SectionId;

    public string WorkspacePath { get; }

    public string WorkingDirectory { get; private set; } = ".";

    public SessionStateSnapshot SessionState
    {
        get
        {
            lock (_syncRoot)
            {
                return new SessionStateSnapshot(
                    _fileContexts.ToArray(),
                    _editContexts.ToArray(),
                    _terminalHistory.ToArray(),
                    WorkingDirectory);
            }
        }
    }

    public string SectionTitle { get; private set; }

    public DateTimeOffset SectionUpdatedAtUtc { get; private set; }

    public string SectionResumeCommand => $"nanoai --section {SectionId}";

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
        lock (_syncRoot)
        {
            _permissionOverrides.Add(rule);
        }
    }

    public void ClearPermissionOverrides()
    {
        lock (_syncRoot)
        {
            if (_permissionOverrides.Count == 0)
            {
                return;
            }

            _permissionOverrides.Clear();
            IsPersistedStateDirty = true;
        }
    }

    public void RegisterTemporaryArtifact(
        string path,
        TemporaryArtifactRetention retention)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path.Trim());
        lock (_syncRoot)
        {
            _temporaryArtifacts.Add(new TemporaryArtifactReference(fullPath, retention));
        }
    }

    public void DeleteTemporaryArtifacts(TemporaryArtifactRetention retention)
    {
        TemporaryArtifactReference[] artifacts;
        lock (_syncRoot)
        {
            artifacts = _temporaryArtifacts
                .Where(artifact => artifact.Retention == retention)
                .ToArray();

            if (artifacts.Length == 0)
            {
                return;
            }

            _temporaryArtifacts.RemoveAll(artifact => artifact.Retention == retention);
        }

        foreach (TemporaryArtifactReference artifact in artifacts)
        {
            TryDeleteTemporaryArtifact(artifact.Path);
        }
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

    public void ReplaceProviderConfiguration(
        AgentProviderProfile providerProfile,
        string activeModelId,
        IReadOnlyList<string> availableModelIds,
        IReadOnlyDictionary<string, int>? modelContextWindowTokens = null)
    {
        ArgumentNullException.ThrowIfNull(providerProfile);
        ArgumentException.ThrowIfNullOrWhiteSpace(activeModelId);
        ArgumentNullException.ThrowIfNull(availableModelIds);

        string[] normalizedAvailableModelIds = NormalizeAvailableModelIds(availableModelIds);
        if (normalizedAvailableModelIds.Length == 0)
        {
            throw new ArgumentException(
                "At least one available model must be provided.",
                nameof(availableModelIds));
        }

        string normalizedActiveModelId = activeModelId.Trim();
        if (!normalizedAvailableModelIds.Contains(normalizedActiveModelId, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                "The active model must exist in the available model set.",
                nameof(activeModelId));
        }

        Dictionary<string, int> normalizedModelContextWindowTokens =
            NormalizeModelContextWindowTokens(modelContextWindowTokens, normalizedAvailableModelIds);

        bool changed =
            !Equals(ProviderProfile, providerProfile) ||
            !string.Equals(ActiveModelId, normalizedActiveModelId, StringComparison.Ordinal) ||
            !AvailableModelIds.SequenceEqual(normalizedAvailableModelIds, StringComparer.Ordinal) ||
            !ModelContextWindowTokensEqual(_modelContextWindowTokens, normalizedModelContextWindowTokens);

        ProviderProfile = providerProfile;
        AvailableModelIds = normalizedAvailableModelIds;
        _modelContextWindowTokens = normalizedModelContextWindowTokens;
        _availableModelIds.Clear();
        foreach (string modelId in normalizedAvailableModelIds)
        {
            _availableModelIds.Add(modelId);
        }

        ActiveModelId = normalizedActiveModelId;

        if (changed)
        {
            IsPersistedStateDirty = true;
        }
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
        return SetReasoningEffort(ReasoningEffortOptions.Off);
    }

    public string ResolvePathFromWorkingDirectory(string? requestedPath)
    {
        string workingDirectory;
        lock (_syncRoot)
        {
            workingDirectory = WorkingDirectory;
        }

        return ResolvePathFromWorkingDirectory(requestedPath, workingDirectory);
    }

    public string ResolvePathFromWorkingDirectory(
        string? requestedPath,
        string baseWorkingDirectory)
    {
        string normalizedBaseDirectory = string.IsNullOrWhiteSpace(baseWorkingDirectory)
            ? "."
            : baseWorkingDirectory.Trim();
        string baseFullPath = NanoAgent.Application.Utilities.WorkspacePath.Resolve(
            WorkspacePath,
            normalizedBaseDirectory);
        string normalizedRequestedPath = string.IsNullOrWhiteSpace(requestedPath)
            ? "."
            : requestedPath.Trim();

        string fullPath = Path.GetFullPath(
            Path.IsPathRooted(normalizedRequestedPath)
                ? normalizedRequestedPath
                : Path.Combine(baseFullPath, normalizedRequestedPath));

        if (!NanoAgent.Application.Utilities.WorkspacePath.IsSamePathOrDescendant(WorkspacePath, fullPath))
        {
            throw new InvalidOperationException("Tool paths must stay within the current workspace.");
        }

        return NanoAgent.Application.Utilities.WorkspacePath.ToRelativePath(WorkspacePath, fullPath);
    }

    public bool TrySetWorkingDirectory(
        string requestedPath,
        out string? error)
    {
        return TrySetWorkingDirectory(requestedPath, WorkingDirectory, out error);
    }

    public bool TrySetWorkingDirectory(
        string requestedPath,
        string baseWorkingDirectory,
        out string? error)
    {
        error = null;

        string resolvedPath;
        try
        {
            resolvedPath = ResolvePathFromWorkingDirectory(requestedPath, baseWorkingDirectory);
        }
        catch (InvalidOperationException exception)
        {
            error = exception.Message;
            return false;
        }

        string fullPath = NanoAgent.Application.Utilities.WorkspacePath.Resolve(WorkspacePath, resolvedPath);
        if (!Directory.Exists(fullPath))
        {
            error = $"Directory '{resolvedPath}' does not exist.";
            return false;
        }

        if (string.Equals(WorkingDirectory, resolvedPath, StringComparison.Ordinal))
        {
            return true;
        }

        WorkingDirectory = resolvedPath;
        IsPersistedStateDirty = true;
        return true;
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
        string assistantResponse,
        IReadOnlyList<ConversationToolCall>? toolCalls = null,
        IReadOnlyList<string>? toolOutputMessages = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userInput);
        ArgumentException.ThrowIfNullOrWhiteSpace(assistantResponse);

        ConversationSectionTurn turn = new(
            userInput,
            assistantResponse,
            toolCalls,
            toolOutputMessages);

        _conversationTurns.Add(turn);
        _conversationHistory.Add(ConversationRequestMessage.User(turn.UserInput));
        _conversationHistory.Add(ConversationRequestMessage.AssistantMessage(turn.AssistantResponse));
        IsPersistedStateDirty = true;
    }

    public void RecordFileContext(SessionFileContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(context.Path) ||
            string.IsNullOrWhiteSpace(context.Activity) ||
            string.IsNullOrWhiteSpace(context.Summary))
        {
            return;
        }

        SessionFileContext normalizedContext = context with
        {
            Path = NormalizeStateText(context.Path, MaxPromptFieldCharacters),
            Activity = NormalizeStateText(context.Activity, MaxPromptFieldCharacters),
            Summary = NormalizeStateText(context.Summary, MaxStateTextCharacters)
        };

        lock (_syncRoot)
        {
            int existingIndex = _fileContexts.FindIndex(existing =>
                string.Equals(existing.Path, normalizedContext.Path, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.Activity, normalizedContext.Activity, StringComparison.Ordinal));

            if (existingIndex >= 0)
            {
                _fileContexts.RemoveAt(existingIndex);
            }

            _fileContexts.Add(normalizedContext);
            TrimOldest(_fileContexts, MaxFileContextEntries);
            IsPersistedStateDirty = true;
        }
    }

    public void RecordEditContext(SessionEditContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(context.Description))
        {
            return;
        }

        string[] normalizedPaths = (context.Paths ?? [])
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => NormalizeStateText(path, MaxPromptFieldCharacters))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        SessionEditContext normalizedContext = context with
        {
            Description = NormalizeStateText(context.Description, MaxPromptFieldCharacters),
            Paths = normalizedPaths,
            AddedLineCount = Math.Max(0, context.AddedLineCount),
            RemovedLineCount = Math.Max(0, context.RemovedLineCount)
        };

        lock (_syncRoot)
        {
            _editContexts.Add(normalizedContext);
            TrimOldest(_editContexts, MaxEditContextEntries);
            IsPersistedStateDirty = true;
        }
    }

    public void RecordTerminalCommand(SessionTerminalCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Command) ||
            string.IsNullOrWhiteSpace(command.WorkingDirectory))
        {
            return;
        }

        SessionTerminalCommand normalizedCommand = command with
        {
            Command = NormalizeStateText(command.Command, MaxPromptFieldCharacters),
            WorkingDirectory = NormalizeStateText(command.WorkingDirectory, MaxPromptFieldCharacters),
            StandardOutput = NormalizeOptionalStateText(command.StandardOutput, MaxStateTextCharacters),
            StandardError = NormalizeOptionalStateText(command.StandardError, MaxStateTextCharacters),
            TerminalId = NormalizeOptionalStateText(command.TerminalId, MaxPromptFieldCharacters),
            TerminalStatus = NormalizeOptionalStateText(command.TerminalStatus, MaxPromptFieldCharacters)
        };

        lock (_syncRoot)
        {
            _terminalHistory.Add(normalizedCommand);
            TrimOldest(_terminalHistory, MaxTerminalHistoryEntries);
            IsPersistedStateDirty = true;
        }
    }

    public string? CreateStatefulContextPrompt()
    {
        if (_fileContexts.Count == 0 &&
            _editContexts.Count == 0 &&
            _terminalHistory.Count == 0 &&
            IsWorkspaceRootWorkingDirectory())
        {
            return null;
        }

        StringBuilder builder = new();
        builder.AppendLine("Session state:");
        builder.AppendLine("Compact memory from previous tool use in this section. Use it to maintain continuity across turns; re-read files or rerun commands when exact current contents or fresh output matter.");
        AppendWorkingDirectoryPrompt(builder);

        AppendFileContextPrompt(builder);
        AppendEditContextPrompt(builder);
        AppendTerminalHistoryPrompt(builder);

        return builder.ToString().Trim();
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

        if (_conversationTurns.Count * 2 != _conversationHistory.Count)
        {
            throw new InvalidOperationException(
                "Conversation turn metadata must match conversation history before it can be persisted.");
        }

        for (int index = 0; index < _conversationHistory.Count; index += 2)
        {
            ConversationRequestMessage userMessage = _conversationHistory[index];
            ConversationRequestMessage assistantMessage = _conversationHistory[index + 1];
            ConversationSectionTurn turn = _conversationTurns[index / 2];

            if (!string.Equals(userMessage.Role, "user", StringComparison.Ordinal) ||
                !string.Equals(assistantMessage.Role, "assistant", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(userMessage.Content) ||
                string.IsNullOrWhiteSpace(assistantMessage.Content) ||
                !string.Equals(userMessage.Content, turn.UserInput, StringComparison.Ordinal) ||
                !string.Equals(assistantMessage.Content, turn.AssistantResponse, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Conversation history contains an unsupported message layout for section persistence.");
            }
        }

        return new ConversationSectionSnapshot(
            SectionId,
            SectionTitle,
            SectionCreatedAtUtc,
            updatedAtUtc,
            ProviderProfile,
            ActiveModelId,
            AvailableModelIds,
            _conversationTurns.ToArray(),
            TotalEstimatedOutputTokens,
            PendingExecutionPlan,
            AgentProfile.Name,
            ReasoningEffort,
            SessionState,
            WorkspacePath,
            _modelContextWindowTokens);
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

        ConversationRequestMessage[] recentHistory = _conversationHistory
            .Skip(_conversationHistory.Count - maxMessageCount)
            .ToArray();
        string? compactedHistory = CreateCompactedConversationHistory(maxHistoryTurns);

        return string.IsNullOrWhiteSpace(compactedHistory)
            ? recentHistory
            : [ConversationRequestMessage.User(compactedHistory), .. recentHistory];
    }

    private string? CreateCompactedConversationHistory(int retainedHistoryTurns)
    {
        int compactedTurnCount = _conversationTurns.Count - retainedHistoryTurns;
        if (compactedTurnCount <= 0)
        {
            return null;
        }

        int skippedCompactedTurns = Math.Max(0, compactedTurnCount - MaxCompactedHistoryTurns);
        ConversationSectionTurn[] compactedTurns = _conversationTurns
            .Skip(skippedCompactedTurns)
            .Take(compactedTurnCount - skippedCompactedTurns)
            .ToArray();
        if (compactedTurns.Length == 0)
        {
            return null;
        }

        StringBuilder builder = new();
        builder.Append("Earlier conversation context (compacted; ");
        builder.Append(compactedTurnCount.ToString(CultureInfo.InvariantCulture));
        builder.Append(compactedTurnCount == 1 ? " older turn" : " older turns");
        if (skippedCompactedTurns > 0)
        {
            builder.Append(", showing the most recent ");
            builder.Append(compactedTurns.Length.ToString(CultureInfo.InvariantCulture));
        }
        builder.AppendLine("; full transcript omitted):");
        builder.AppendLine("Use this for continuity only; inspect files or rerun commands when exact current state matters.");

        for (int index = 0; index < compactedTurns.Length; index++)
        {
            ConversationSectionTurn turn = compactedTurns[index];
            int originalTurnNumber = skippedCompactedTurns + index + 1;
            builder.Append("- Turn ");
            builder.Append(originalTurnNumber.ToString(CultureInfo.InvariantCulture));
            builder.Append(" user: ");
            builder.AppendLine(CompactHistoryField(turn.UserInput));
            builder.Append("  assistant: ");
            builder.AppendLine(CompactHistoryField(turn.AssistantResponse));

            if (turn.ToolCalls.Count > 0)
            {
                string toolNames = string.Join(
                    ", ",
                    turn.ToolCalls
                        .Select(static toolCall => toolCall.Name)
                        .Where(static name => !string.IsNullOrWhiteSpace(name))
                        .Distinct(StringComparer.Ordinal)
                        .Take(8));

                if (!string.IsNullOrWhiteSpace(toolNames))
                {
                    builder.Append("  tools: ");
                    builder.AppendLine(toolNames);
                }
            }

            if (builder.Length >= MaxCompactedHistoryCharacters)
            {
                break;
            }
        }

        string compactedHistory = builder.ToString().Trim();
        return compactedHistory.Length <= MaxCompactedHistoryCharacters
            ? compactedHistory
            : compactedHistory[..Math.Max(0, MaxCompactedHistoryCharacters - 3)].TrimEnd() + "...";
    }

    private static string CompactHistoryField(string value)
    {
        string normalized = NormalizeWhitespace(value);
        return normalized.Length <= MaxCompactedHistoryFieldCharacters
            ? normalized
            : normalized[..Math.Max(0, MaxCompactedHistoryFieldCharacters - 3)].TrimEnd() + "...";
    }

    private static string NormalizeWhitespace(string value)
    {
        StringBuilder builder = new(value.Length);
        bool previousWasWhitespace = false;
        foreach (char character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhitespace)
                {
                    builder.Append(' ');
                    previousWasWhitespace = true;
                }

                continue;
            }

            builder.Append(character);
            previousWasWhitespace = false;
        }

        return builder.ToString().Trim();
    }

    private static string[] NormalizeAvailableModelIds(IReadOnlyList<string> availableModelIds)
    {
        return availableModelIds
            .Where(static modelId => !string.IsNullOrWhiteSpace(modelId))
            .Select(static modelId => modelId.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static Dictionary<string, int> NormalizeModelContextWindowTokens(
        IReadOnlyDictionary<string, int>? modelContextWindowTokens,
        IReadOnlyList<string> availableModelIds)
    {
        Dictionary<string, int> normalized = new(StringComparer.Ordinal);
        if (modelContextWindowTokens is null || modelContextWindowTokens.Count == 0)
        {
            return normalized;
        }

        HashSet<string> available = new(availableModelIds, StringComparer.Ordinal);
        foreach ((string modelId, int contextWindowTokens) in modelContextWindowTokens)
        {
            if (string.IsNullOrWhiteSpace(modelId) || contextWindowTokens <= 0)
            {
                continue;
            }

            string normalizedModelId = modelId.Trim();
            if (available.Contains(normalizedModelId))
            {
                normalized[normalizedModelId] = contextWindowTokens;
            }
        }

        return normalized;
    }

    private static bool ModelContextWindowTokensEqual(
        IReadOnlyDictionary<string, int> first,
        IReadOnlyDictionary<string, int> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        foreach ((string modelId, int contextWindowTokens) in first)
        {
            if (!second.TryGetValue(modelId, out int otherContextWindowTokens) ||
                otherContextWindowTokens != contextWindowTokens)
            {
                return false;
            }
        }

        return true;
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

    public bool TryCreateFileEditTransactionSnapshot(
        string description,
        out WorkspaceFileEditTransaction? transaction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        if (_batchedFileEditTransactions is not null)
        {
            throw new InvalidOperationException(
                "File edit transactions cannot be snapshotted while a transaction batch is active.");
        }

        WorkspaceFileEditTransaction[] transactions = _undoFileEditTransactions
            .Reverse()
            .ToArray();

        if (transactions.Length == 0)
        {
            transaction = null;
            return false;
        }

        transaction = transactions.Length == 1
            ? new WorkspaceFileEditTransaction(
                description,
                transactions[0].BeforeStates,
                transactions[0].AfterStates)
            : MergeTransactions(transactions, description);
        return true;
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

    private void RestoreSessionState(SessionStateSnapshot? sessionState)
    {
        if (sessionState is null)
        {
            return;
        }

        RestoreWorkingDirectory(sessionState.WorkingDirectory);

        if (sessionState.IsEmpty)
        {
            return;
        }

        foreach (SessionFileContext context in (sessionState.Files ?? []).Where(static context => context is not null))
        {
            if (string.IsNullOrWhiteSpace(context.Path) ||
                string.IsNullOrWhiteSpace(context.Activity) ||
                string.IsNullOrWhiteSpace(context.Summary))
            {
                continue;
            }

            _fileContexts.Add(context with
            {
                Path = NormalizeStateText(context.Path, MaxPromptFieldCharacters),
                Activity = NormalizeStateText(context.Activity, MaxPromptFieldCharacters),
                Summary = NormalizeStateText(context.Summary, MaxStateTextCharacters)
            });
        }

        foreach (SessionEditContext context in (sessionState.Edits ?? []).Where(static context => context is not null))
        {
            if (string.IsNullOrWhiteSpace(context.Description))
            {
                continue;
            }

            _editContexts.Add(context with
            {
                Description = NormalizeStateText(context.Description, MaxPromptFieldCharacters),
                Paths = (context.Paths ?? [])
                    .Where(static path => !string.IsNullOrWhiteSpace(path))
                    .Select(static path => NormalizeStateText(path, MaxPromptFieldCharacters))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                AddedLineCount = Math.Max(0, context.AddedLineCount),
                RemovedLineCount = Math.Max(0, context.RemovedLineCount)
            });
        }

        foreach (SessionTerminalCommand command in (sessionState.TerminalHistory ?? []).Where(static command => command is not null))
        {
            if (string.IsNullOrWhiteSpace(command.Command) ||
                string.IsNullOrWhiteSpace(command.WorkingDirectory))
            {
                continue;
            }

            _terminalHistory.Add(command with
            {
                Command = NormalizeStateText(command.Command, MaxPromptFieldCharacters),
                WorkingDirectory = NormalizeStateText(command.WorkingDirectory, MaxPromptFieldCharacters),
                StandardOutput = NormalizeOptionalStateText(command.StandardOutput, MaxStateTextCharacters),
                StandardError = NormalizeOptionalStateText(command.StandardError, MaxStateTextCharacters),
                TerminalId = NormalizeOptionalStateText(command.TerminalId, MaxPromptFieldCharacters),
                TerminalStatus = NormalizeOptionalStateText(command.TerminalStatus, MaxPromptFieldCharacters)
            });
        }

        TrimOldest(_fileContexts, MaxFileContextEntries);
        TrimOldest(_editContexts, MaxEditContextEntries);
        TrimOldest(_terminalHistory, MaxTerminalHistoryEntries);
    }

    private void RestoreWorkingDirectory(string? workingDirectory)
    {
        string resolvedPath;
        try
        {
            resolvedPath = ResolvePathFromWorkingDirectory(workingDirectory, ".");
        }
        catch (InvalidOperationException)
        {
            WorkingDirectory = ".";
            return;
        }

        string fullPath = NanoAgent.Application.Utilities.WorkspacePath.Resolve(WorkspacePath, resolvedPath);
        WorkingDirectory = Directory.Exists(fullPath)
            ? resolvedPath
            : ".";
    }

    private void AppendWorkingDirectoryPrompt(StringBuilder builder)
    {
        if (IsWorkspaceRootWorkingDirectory())
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine($"Current working directory: {WorkingDirectory}");
        builder.AppendLine("Relative file paths and shell commands default to this directory unless an explicit path says otherwise.");
    }

    private void AppendFileContextPrompt(StringBuilder builder)
    {
        if (_fileContexts.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("Known files and workspace observations:");
        foreach (SessionFileContext context in TakeLatest(_fileContexts, MaxPromptFileContextEntries))
        {
            builder
                .Append("- ")
                .Append(context.Path)
                .Append(" [")
                .Append(context.Activity)
                .Append(", ")
                .Append(FormatTimestamp(context.ObservedAtUtc))
                .Append("]: ")
                .AppendLine(FormatPromptField(context.Summary));
        }
    }

    private void AppendEditContextPrompt(StringBuilder builder)
    {
        if (_editContexts.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("Previous edits:");
        foreach (SessionEditContext context in TakeLatest(_editContexts, MaxPromptEditContextEntries))
        {
            string paths = context.Paths.Count == 0
                ? "(paths unavailable)"
                : string.Join(", ", context.Paths);

            builder
                .Append("- ")
                .Append(FormatTimestamp(context.EditedAtUtc))
                .Append(": ")
                .Append(context.Description)
                .Append(" on ")
                .Append(paths)
                .Append(" (+")
                .Append(context.AddedLineCount.ToString(CultureInfo.InvariantCulture))
                .Append(" -")
                .Append(context.RemovedLineCount.ToString(CultureInfo.InvariantCulture))
                .AppendLine(").");
        }
    }

    private void AppendTerminalHistoryPrompt(StringBuilder builder)
    {
        if (_terminalHistory.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("Terminal history:");
        foreach (SessionTerminalCommand command in TakeLatest(_terminalHistory, MaxPromptTerminalHistoryEntries))
        {
            builder
                .Append("- ")
                .Append(FormatTimestamp(command.ExecutedAtUtc))
                .Append(": `")
                .Append(command.Command)
                .Append("` in ")
                .Append(command.WorkingDirectory);

            if (command.Background)
            {
                builder
                    .Append(" background terminal ")
                    .Append(string.IsNullOrWhiteSpace(command.TerminalId)
                        ? "(unknown)"
                        : command.TerminalId)
                    .Append(" status ")
                    .Append(string.IsNullOrWhiteSpace(command.TerminalStatus)
                        ? "unknown"
                        : command.TerminalStatus);

                if (command.ExitCode >= 0)
                {
                    builder
                        .Append(" exit ")
                        .Append(command.ExitCode.ToString(CultureInfo.InvariantCulture));
                }
            }
            else
            {
                builder
                    .Append(" exited ")
                    .Append(command.ExitCode.ToString(CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrWhiteSpace(command.StandardOutput))
            {
                builder
                    .Append("; stdout: ")
                    .Append(FormatPromptField(command.StandardOutput));
            }

            if (!string.IsNullOrWhiteSpace(command.StandardError))
            {
                builder
                    .Append("; stderr: ")
                    .Append(FormatPromptField(command.StandardError));
            }

            builder.AppendLine();
        }
    }

    private static IReadOnlyList<T> TakeLatest<T>(
        IReadOnlyList<T> values,
        int maxCount)
    {
        if (values.Count <= maxCount)
        {
            return values.ToArray();
        }

        return values
            .Skip(values.Count - maxCount)
            .ToArray();
    }

    private static string FormatTimestamp(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("u", CultureInfo.InvariantCulture);
    }

    private static string FormatPromptField(string value)
    {
        return NormalizeStateText(value, MaxPromptFieldCharacters)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private static string? NormalizeOptionalStateText(
        string? value,
        int maxCharacters)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : NormalizeStateText(value, maxCharacters);
    }

    private static string NormalizeStateText(
        string value,
        int maxCharacters)
    {
        string normalized = SecretRedactor.Redact(value)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

        if (normalized.Length <= maxCharacters)
        {
            return normalized;
        }

        return normalized[..Math.Max(0, maxCharacters - 3)].TrimEnd() + "...";
    }

    private static void TrimOldest<T>(
        List<T> values,
        int maxCount)
    {
        if (values.Count <= maxCount)
        {
            return;
        }

        values.RemoveRange(0, values.Count - maxCount);
    }

    private bool IsWorkspaceRootWorkingDirectory()
    {
        return string.Equals(WorkingDirectory, ".", StringComparison.Ordinal);
    }

    private static WorkspaceFileEditTransaction MergeTransactions(
        IReadOnlyList<WorkspaceFileEditTransaction> transactions,
        string? description = null)
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
        string transactionDescription = string.IsNullOrWhiteSpace(description)
            ? $"tool round ({transactions.Count} edits across {fileCount} {(fileCount == 1 ? "file" : "files")})"
            : description.Trim();

        return new WorkspaceFileEditTransaction(
            transactionDescription,
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

    private static string NormalizeWorkspacePath(string? workspacePath)
    {
        string normalized = string.IsNullOrWhiteSpace(workspacePath)
            ? Directory.GetCurrentDirectory()
            : workspacePath.Trim();

        return Path.GetFullPath(normalized);
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

    private static void TryDeleteTemporaryArtifact(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                TryDeleteEmptyParentDirectory(path);
                return;
            }

            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteEmptyParentDirectory(string path)
    {
        try
        {
            string? parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(parent) &&
                Directory.Exists(parent) &&
                !Directory.EnumerateFileSystemEntries(parent).Any())
            {
                Directory.Delete(parent);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record TemporaryArtifactReference(
        string Path,
        TemporaryArtifactRetention Retention);
}
