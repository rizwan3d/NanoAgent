using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NanoAgent.Application.Backend;
using NanoAgent.Desktop.Models;
using NanoAgent.Desktop.Services;

namespace NanoAgent.Desktop.ViewModels;

public partial class ChatViewModel : ViewModelBase
{
    private const double EstimatedLiveTokensPerSecond = 4d;

    private readonly AgentRunner _agentRunner;
    private readonly ProviderSetupRunner _providerSetupRunner;
    private readonly DispatcherTimer _progressTimer;
    private readonly DispatcherTimer _selectionPromptTimer;
    private DateTimeOffset? _currentRunStartedAt;
    private bool _isApplyingSessionInfo;

    [ObservableProperty]
    private string _prompt = string.Empty;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isPromptRunning;

    [ObservableProperty]
    private bool _isProviderSetupActive;

    [ObservableProperty]
    private string _progressText = "(0s \u00B7 0 tokens)";

    [ObservableProperty]
    private string? _selectedModelId;

    [ObservableProperty]
    private string? _selectedThinkingMode = "off";

    [ObservableProperty]
    private bool _isThinkingEnabled;

    [ObservableProperty]
    private string? _selectedProfileName = "build";

    [ObservableProperty]
    private bool _hasSessionOptions;

    [ObservableProperty]
    private DesktopSelectionPrompt? _activeSelectionPrompt;

    [ObservableProperty]
    private DesktopTextPrompt? _activeTextPrompt;

    [ObservableProperty]
    private WorkspaceSectionInfo? _selectedSection;

    [ObservableProperty]
    private ProjectInfo? _activeProject;

    [ObservableProperty]
    private string _permissionToolPattern = string.Empty;

    [ObservableProperty]
    private string _permissionSubjectPattern = string.Empty;

    public ChatViewModel(AgentRunner agentRunner)
    {
        _agentRunner = agentRunner;
        _providerSetupRunner = new ProviderSetupRunner();
        _agentRunner.ConversationMessageReceived += OnConversationMessageReceived;
        _agentRunner.SelectionPromptChanged += OnSelectionPromptChanged;
        _agentRunner.TextPromptChanged += OnTextPromptChanged;
        _providerSetupRunner.ConversationMessageReceived += OnConversationMessageReceived;
        _providerSetupRunner.SelectionPromptChanged += OnSelectionPromptChanged;
        _providerSetupRunner.TextPromptChanged += OnTextPromptChanged;
        RunPromptCommand = new AsyncRelayCommand<ProjectInfo?>(RunPromptAsync, CanRunPrompt);
        LoadSessionCommand = new AsyncRelayCommand<ProjectInfo?>(LoadSessionAsync, CanRunWorkspaceCommand);
        ApplyModelCommand = new AsyncRelayCommand<ProjectInfo?>(ApplyModelAsync, CanApplyModel);
        ApplyThinkingCommand = new AsyncRelayCommand<ProjectInfo?>(ApplyThinkingAsync, CanApplyThinking);
        ApplyProfileCommand = new AsyncRelayCommand<ProjectInfo?>(ApplyProfileAsync, CanApplyProfile);
        ConfigureProviderCommand = new AsyncRelayCommand<ProjectInfo?>(ConfigureProviderAsync, CanConfigureProvider);
        ShowHelpCommand = new AsyncRelayCommand<ProjectInfo?>(ShowHelpAsync, CanRunWorkspaceCommand);
        ShowModelsCommand = new AsyncRelayCommand<ProjectInfo?>(ShowModelsAsync, CanRunWorkspaceCommand);
        ShowPermissionsCommand = new AsyncRelayCommand<ProjectInfo?>(ShowPermissionsAsync, CanRunWorkspaceCommand);
        ShowRulesCommand = new AsyncRelayCommand<ProjectInfo?>(ShowRulesAsync, CanRunWorkspaceCommand);
        UndoCommand = new AsyncRelayCommand<ProjectInfo?>(UndoAsync, CanRunWorkspaceCommand);
        RedoCommand = new AsyncRelayCommand<ProjectInfo?>(RedoAsync, CanRunWorkspaceCommand);
        AllowPermissionCommand = new AsyncRelayCommand<ProjectInfo?>(AllowPermissionAsync, CanApplyPermissionOverride);
        DenyPermissionCommand = new AsyncRelayCommand<ProjectInfo?>(DenyPermissionAsync, CanApplyPermissionOverride);
        _progressTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _progressTimer.Tick += (_, _) => UpdateProgressText();
        _selectionPromptTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _selectionPromptTimer.Tick += (_, _) => ActiveSelectionPrompt?.Tick();

        Messages.Add(new ChatMessage("NanoAgent", "Ready."));
        Activity.Add(new AgentEvent("idle", "Idle"));
    }

    public ObservableCollection<ChatMessage> Messages { get; } = new();

    public ObservableCollection<AgentEvent> Activity { get; } = new();

    public ObservableCollection<string> AvailableModels { get; } = new();

    public ObservableCollection<string> ThinkingModes { get; } = new(["off", "on"]);

    public ObservableCollection<string> ProfileOptions { get; } = new(["build", "plan", "review"]);

    public IAsyncRelayCommand<ProjectInfo?> RunPromptCommand { get; }

    public IAsyncRelayCommand<ProjectInfo?> LoadSessionCommand { get; }

    public IAsyncRelayCommand<ProjectInfo?> ApplyModelCommand { get; }

    public IAsyncRelayCommand<ProjectInfo?> ApplyThinkingCommand { get; }

    public IAsyncRelayCommand<ProjectInfo?> ApplyProfileCommand { get; }

    public IAsyncRelayCommand<ProjectInfo?> ConfigureProviderCommand { get; }

    public IAsyncRelayCommand<ProjectInfo?> ShowHelpCommand { get; }

    public IAsyncRelayCommand<ProjectInfo?> ShowModelsCommand { get; }

    public IAsyncRelayCommand<ProjectInfo?> ShowPermissionsCommand { get; }

    public IAsyncRelayCommand<ProjectInfo?> ShowRulesCommand { get; }

    public IAsyncRelayCommand<ProjectInfo?> UndoCommand { get; }

    public IAsyncRelayCommand<ProjectInfo?> RedoCommand { get; }

    public IAsyncRelayCommand<ProjectInfo?> AllowPermissionCommand { get; }

    public IAsyncRelayCommand<ProjectInfo?> DenyPermissionCommand { get; }

    public event EventHandler? RunCompleted;

    public bool HasActiveSelectionPrompt => ActiveSelectionPrompt is not null;

    public bool HasActiveTextPrompt => ActiveTextPrompt is not null;

    public bool HasFloatingSelectionPrompt => ActiveSelectionPrompt is not null && !IsProviderSetupActive;

    public bool HasFloatingTextPrompt => ActiveTextPrompt is not null && !IsProviderSetupActive;

    public bool HasOnboardingOverlay => IsProviderSetupActive;

    public bool HasOnboardingSelectionPrompt => IsProviderSetupActive && ActiveSelectionPrompt is not null;

    public bool HasOnboardingTextPrompt => IsProviderSetupActive && ActiveTextPrompt is not null;

    public bool HasOnboardingCountdown => ActiveSelectionPrompt?.HasCountdown == true;

    public bool CanCancelOnboardingSelectionPrompt => ActiveSelectionPrompt?.AllowCancellation == true;

    public bool CanCancelOnboardingTextPrompt => ActiveTextPrompt?.AllowCancellation == true;

    public string OnboardingStageText
    {
        get
        {
            if (ActiveSelectionPrompt is not null)
            {
                return "Step 1 of 3";
            }

            if (ActiveTextPrompt is not null)
            {
                return ActiveTextPrompt.IsSecret ? "Step 2 of 3" : "Step 1 of 3";
            }

            return IsRunning ? "Step 3 of 3" : "Provider setup";
        }
    }

    public string OnboardingTitle => ActiveSelectionPrompt?.Title ??
        ActiveTextPrompt?.Title ??
        "Checking provider setup";

    public string OnboardingDescription => ActiveSelectionPrompt?.Description ??
        ActiveTextPrompt?.Description ??
        "NanoAgent is validating the saved provider configuration and model list.";

    public string OnboardingInputKind => ActiveTextPrompt?.IsSecret == true
        ? "Secret"
        : "Input";

    public string OnboardingStatusText => IsRunning ? "Working" : "Provider setup";

    public bool HasPromptProgress => IsPromptRunning;

    public string StatusText => IsRunning
        ? IsPromptRunning ? $"Working {ProgressText}" : "Working"
        : "Ready";

    public bool SessionOptionsEnabled => HasSessionOptions && !IsRunning;

    public string ThinkingToggleText => IsThinkingEnabled ? "Thinking On" : "Thinking Off";

    partial void OnPromptChanged(string value)
    {
        RunPromptCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsRunningChanged(bool value)
    {
        NotifyCommandStatesChanged();
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(OnboardingStatusText));
        OnPropertyChanged(nameof(SessionOptionsEnabled));
        NotifyOnboardingStateChanged();
    }

    partial void OnIsPromptRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(HasPromptProgress));
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnIsProviderSetupActiveChanged(bool value)
    {
        NotifyOnboardingStateChanged();
    }

    partial void OnProgressTextChanged(string value)
    {
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnSelectedModelIdChanged(string? value)
    {
        ApplyModelCommand.NotifyCanExecuteChanged();
        _ = AutoApplyModelAsync();
    }

    partial void OnSelectedThinkingModeChanged(string? value)
    {
        bool shouldBeEnabled = string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
        if (IsThinkingEnabled != shouldBeEnabled)
        {
            IsThinkingEnabled = shouldBeEnabled;
        }

        ApplyThinkingCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsThinkingEnabledChanged(bool value)
    {
        string mode = value ? "on" : "off";
        if (!string.Equals(SelectedThinkingMode, mode, StringComparison.OrdinalIgnoreCase))
        {
            SelectedThinkingMode = mode;
        }

        OnPropertyChanged(nameof(ThinkingToggleText));
        ApplyThinkingCommand.NotifyCanExecuteChanged();
        _ = AutoApplyThinkingAsync();
    }

    partial void OnSelectedProfileNameChanged(string? value)
    {
        ApplyProfileCommand.NotifyCanExecuteChanged();
        _ = AutoApplyProfileAsync();
    }

    partial void OnHasSessionOptionsChanged(bool value)
    {
        OnPropertyChanged(nameof(SessionOptionsEnabled));
    }

    partial void OnActiveProjectChanged(ProjectInfo? value)
    {
        NotifyCommandStatesChanged();
    }

    partial void OnActiveSelectionPromptChanged(DesktopSelectionPrompt? value)
    {
        OnPropertyChanged(nameof(HasActiveSelectionPrompt));
        NotifyOnboardingStateChanged();

        if (value is not null && value.HasCountdown)
        {
            value.Tick();
            _selectionPromptTimer.Start();
            return;
        }

        _selectionPromptTimer.Stop();
    }

    partial void OnActiveTextPromptChanged(DesktopTextPrompt? value)
    {
        OnPropertyChanged(nameof(HasActiveTextPrompt));
        NotifyOnboardingStateChanged();
    }

    partial void OnPermissionToolPatternChanged(string value)
    {
        AllowPermissionCommand.NotifyCanExecuteChanged();
        DenyPermissionCommand.NotifyCanExecuteChanged();
    }

    private bool CanRunPrompt(ProjectInfo? project)
    {
        return !IsRunning && project is not null && !string.IsNullOrWhiteSpace(Prompt);
    }

    private bool CanRunWorkspaceCommand(ProjectInfo? project)
    {
        return !IsRunning && project is not null;
    }

    private bool CanConfigureProvider(ProjectInfo? project)
    {
        return !IsRunning;
    }

    private bool CanApplyModel(ProjectInfo? project)
    {
        return CanRunWorkspaceCommand(project) && !string.IsNullOrWhiteSpace(SelectedModelId);
    }

    private bool CanApplyThinking(ProjectInfo? project)
    {
        return CanRunWorkspaceCommand(project) && !string.IsNullOrWhiteSpace(SelectedThinkingMode);
    }

    private bool CanApplyProfile(ProjectInfo? project)
    {
        return CanRunWorkspaceCommand(project) && !string.IsNullOrWhiteSpace(SelectedProfileName);
    }

    private bool CanApplyPermissionOverride(ProjectInfo? project)
    {
        return CanRunWorkspaceCommand(project) && !string.IsNullOrWhiteSpace(PermissionToolPattern);
    }

    private Task AutoApplyModelAsync()
    {
        if (_isApplyingSessionInfo ||
            !CanApplyModel(ActiveProject))
        {
            return Task.CompletedTask;
        }

        return ApplyModelAsync(ActiveProject);
    }

    private Task AutoApplyThinkingAsync()
    {
        if (_isApplyingSessionInfo ||
            !CanApplyThinking(ActiveProject))
        {
            return Task.CompletedTask;
        }

        return ApplyThinkingAsync(ActiveProject);
    }

    private Task AutoApplyProfileAsync()
    {
        if (_isApplyingSessionInfo ||
            !CanApplyProfile(ActiveProject))
        {
            return Task.CompletedTask;
        }

        return ApplyProfileAsync(ActiveProject);
    }

    public async Task LoadSessionAsync(ProjectInfo? project)
    {
        if (project is null || IsRunning)
        {
            return;
        }

        bool shouldShowSetupOverlay = !HasSessionOptions;
        if (shouldShowSetupOverlay)
        {
            IsProviderSetupActive = true;
        }

        try
        {
            await RunControlOperationAsync(
                "Checking provider setup",
                project.Path,
                async () =>
                {
                    BackendSessionInfo sessionInfo = await _agentRunner.GetSessionAsync(project.Path, SelectedSection?.SectionId);
                    ApplySessionInfo(sessionInfo, replaceConversation: true, workspacePath: project.Path);
                    Activity.Add(new AgentEvent("settings", "Controls loaded.", project.Path));
                });
        }
        finally
        {
            if (shouldShowSetupOverlay)
            {
                IsProviderSetupActive = false;
            }
        }
    }

    public async Task EnsureProviderSetupAsync()
    {
        if (IsRunning)
        {
            return;
        }

        IsProviderSetupActive = true;
        try
        {
            await RunControlOperationAsync(
                "Checking provider setup",
                workspacePath: null,
                async () =>
                {
                    ProviderSetupRunResult result = await _providerSetupRunner.RunAsync();
                    Messages.Add(new ChatMessage(
                        "NanoAgent",
                        "Provider setup complete.\n" +
                        $"Provider: {result.ProviderName}\n" +
                        $"Active model: {result.ModelId}\n" +
                        "Open a workspace to start a section.",
                        statusNote: null,
                        workspacePath: null));

                    foreach (string activity in result.Activity)
                    {
                        Activity.Add(new AgentEvent("settings", activity));
                    }
                });
        }
        finally
        {
            IsProviderSetupActive = false;
        }
    }

    private async Task ApplyModelAsync(ProjectInfo? project)
    {
        if (project is null || string.IsNullOrWhiteSpace(SelectedModelId))
        {
            return;
        }

        await RunControlOperationAsync(
            "Changing model",
            project.Path,
            async () =>
            {
                AgentRunResult result = await _agentRunner.SetModelAsync(
                    project.Path,
                    SelectedModelId,
                    SelectedSection?.SectionId);
                ApplyRunResult(result, project.Path);
            });
    }

    private async Task ApplyThinkingAsync(ProjectInfo? project)
    {
        if (project is null || string.IsNullOrWhiteSpace(SelectedThinkingMode))
        {
            return;
        }

        await RunControlOperationAsync(
            "Changing thinking",
            project.Path,
            async () =>
            {
                AgentRunResult result = await _agentRunner.SetThinkingAsync(
                    project.Path,
                    SelectedThinkingMode,
                    SelectedSection?.SectionId);
                ApplyRunResult(result, project.Path);
            });
    }

    private async Task ApplyProfileAsync(ProjectInfo? project)
    {
        if (project is null || string.IsNullOrWhiteSpace(SelectedProfileName))
        {
            return;
        }

        await RunControlOperationAsync(
            "Changing profile",
            project.Path,
            async () =>
            {
                AgentRunResult result = await _agentRunner.SetProfileAsync(
                    project.Path,
                    SelectedProfileName,
                    SelectedSection?.SectionId);
                ApplyRunResult(result, project.Path);
            });
    }

    private async Task ConfigureProviderAsync(ProjectInfo? project)
    {
        if (project is null)
        {
            await EnsureProviderSetupAsync();
            return;
        }

        IsProviderSetupActive = true;
        try
        {
            await RunSessionCommandAsync(project, "/onboard", "Configuring provider");
        }
        finally
        {
            IsProviderSetupActive = false;
        }
    }

    private Task ShowHelpAsync(ProjectInfo? project)
    {
        return RunSessionCommandAsync(project, "/help", "Opening help");
    }

    private Task ShowModelsAsync(ProjectInfo? project)
    {
        return RunSessionCommandAsync(project, "/models", "Listing models");
    }

    private Task ShowPermissionsAsync(ProjectInfo? project)
    {
        return RunSessionCommandAsync(project, "/permissions", "Loading permissions");
    }

    private Task ShowRulesAsync(ProjectInfo? project)
    {
        return RunSessionCommandAsync(project, "/rules", "Loading permission rules");
    }

    private Task UndoAsync(ProjectInfo? project)
    {
        return RunSessionCommandAsync(project, "/undo", "Undoing last edit");
    }

    private Task RedoAsync(ProjectInfo? project)
    {
        return RunSessionCommandAsync(project, "/redo", "Redoing last edit");
    }

    private Task AllowPermissionAsync(ProjectInfo? project)
    {
        return RunPermissionOverrideAsync(project, "/allow", "Adding allow rule");
    }

    private Task DenyPermissionAsync(ProjectInfo? project)
    {
        return RunPermissionOverrideAsync(project, "/deny", "Adding deny rule");
    }

    private async Task RunPromptAsync(ProjectInfo? project)
    {
        if (project is null || string.IsNullOrWhiteSpace(Prompt))
        {
            return;
        }

        var prompt = Prompt.Trim();
        Prompt = string.Empty;
        bool isCommand = IsSlashCommand(prompt);
        bool isOnboardingCommand = IsOnboardingCommand(prompt);

        Messages.Add(new ChatMessage("You", prompt, statusNote: null, workspacePath: project.Path));
        Activity.Add(new AgentEvent("task", $"Running in {project.Name}", project.Path));

        if (isOnboardingCommand)
        {
            IsProviderSetupActive = true;
        }

        if (!isCommand)
        {
            _currentRunStartedAt = DateTimeOffset.UtcNow;
            UpdateProgressText();
            _progressTimer.Start();
            IsPromptRunning = true;
        }

        IsRunning = true;

        try
        {
            AgentRunResult result = await _agentRunner.RunAsync(
                project.Path,
                prompt,
                SelectedSection?.SectionId);
            string? finalProgressText = FormatFinalProgressText(result, allowLiveFallback: !isCommand);
            ApplySessionInfo(result.SessionInfo, workspacePath: project.Path);

            AddToolOutputMessages(result, project.Path);

            Messages.Add(new ChatMessage(
                "NanoAgent",
                string.IsNullOrWhiteSpace(result.ResponseText) ? "Task completed with no output." : result.ResponseText.Trim(),
                finalProgressText,
                project.Path));

            foreach (string activity in result.Activity)
            {
                Activity.Add(new AgentEvent("agent", activity, project.Path));
            }

            Activity.Add(new AgentEvent("done", "Task finished.", project.Path));
            RunCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Messages.Add(new ChatMessage("NanoAgent", ex.Message, statusNote: null, workspacePath: project.Path));
            Activity.Add(new AgentEvent("error", ex.Message, project.Path));
        }
        finally
        {
            _progressTimer.Stop();
            _currentRunStartedAt = null;
            IsPromptRunning = false;
            IsRunning = false;
            if (isOnboardingCommand)
            {
                IsProviderSetupActive = false;
            }
        }
    }

    private async Task RunControlOperationAsync(
        string description,
        string? workspacePath,
        Func<Task> operation)
    {
        IsPromptRunning = false;
        IsRunning = true;
        Activity.Add(new AgentEvent("settings", description, workspacePath));

        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            Messages.Add(new ChatMessage("NanoAgent", ex.Message, statusNote: null, workspacePath: workspacePath));
            Activity.Add(new AgentEvent("error", ex.Message, workspacePath));
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task RunPermissionOverrideAsync(
        ProjectInfo? project,
        string commandName,
        string description)
    {
        if (project is null || string.IsNullOrWhiteSpace(PermissionToolPattern))
        {
            return;
        }

        string command = string.IsNullOrWhiteSpace(PermissionSubjectPattern)
            ? $"{commandName} {PermissionToolPattern.Trim()}"
            : $"{commandName} {PermissionToolPattern.Trim()} {PermissionSubjectPattern.Trim()}";

        await RunSessionCommandAsync(project, command, description);
        PermissionSubjectPattern = string.Empty;
    }

    private async Task RunSessionCommandAsync(
        ProjectInfo? project,
        string command,
        string description)
    {
        if (project is null)
        {
            return;
        }

        await RunControlOperationAsync(
            description,
            project.Path,
            async () =>
            {
                Messages.Add(new ChatMessage("You", command, statusNote: null, workspacePath: project.Path));
                AgentRunResult result = await _agentRunner.RunAsync(
                    project.Path,
                    command,
                    SelectedSection?.SectionId);
                ApplySessionInfo(result.SessionInfo, workspacePath: project.Path);
                AddToolOutputMessages(result, project.Path);
                string? finalProgressText = FormatFinalProgressText(result, allowLiveFallback: false);
                Messages.Add(new ChatMessage(
                    "NanoAgent",
                    string.IsNullOrWhiteSpace(result.ResponseText)
                        ? "Command completed."
                        : result.ResponseText.Trim(),
                    statusNote: finalProgressText,
                    workspacePath: project.Path));

                foreach (string activity in result.Activity)
                {
                    Activity.Add(new AgentEvent("command", activity, project.Path));
                }

                RunCompleted?.Invoke(this, EventArgs.Empty);
            });
    }

    private static bool IsSlashCommand(string prompt)
    {
        return prompt.TrimStart().StartsWith("/", StringComparison.Ordinal);
    }

    private static bool IsOnboardingCommand(string prompt)
    {
        string trimmed = prompt.Trim();
        return string.Equals(trimmed, "/onboard", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("/onboard ", StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyRunResult(AgentRunResult result, string? workspacePath)
    {
        ApplySessionInfo(result.SessionInfo, workspacePath: workspacePath);

        foreach (string activity in result.Activity)
        {
            Activity.Add(new AgentEvent("settings", activity, workspacePath));
        }
    }

    private void ApplySessionInfo(
        BackendSessionInfo? sessionInfo,
        bool replaceConversation = false,
        string? workspacePath = null)
    {
        if (sessionInfo is null)
        {
            return;
        }

        _isApplyingSessionInfo = true;
        try
        {
            AvailableModels.Clear();
            foreach (string modelId in sessionInfo.AvailableModelIds)
            {
                AvailableModels.Add(modelId);
            }

            SelectedModelId = sessionInfo.ModelId;
            SelectedThinkingMode = sessionInfo.ThinkingMode;
            SelectedProfileName = sessionInfo.AgentProfileName;
            HasSessionOptions = true;
        }
        finally
        {
            _isApplyingSessionInfo = false;
        }

        if (replaceConversation)
        {
            ReplaceConversationMessages(sessionInfo, workspacePath);
        }

        NotifyCommandStatesChanged();
    }

    private void NotifyCommandStatesChanged()
    {
        RunPromptCommand.NotifyCanExecuteChanged();
        LoadSessionCommand.NotifyCanExecuteChanged();
        ApplyModelCommand.NotifyCanExecuteChanged();
        ApplyThinkingCommand.NotifyCanExecuteChanged();
        ApplyProfileCommand.NotifyCanExecuteChanged();
        ConfigureProviderCommand.NotifyCanExecuteChanged();
        ShowHelpCommand.NotifyCanExecuteChanged();
        ShowModelsCommand.NotifyCanExecuteChanged();
        ShowPermissionsCommand.NotifyCanExecuteChanged();
        ShowRulesCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        AllowPermissionCommand.NotifyCanExecuteChanged();
        DenyPermissionCommand.NotifyCanExecuteChanged();
    }

    private void NotifyOnboardingStateChanged()
    {
        OnPropertyChanged(nameof(HasFloatingSelectionPrompt));
        OnPropertyChanged(nameof(HasFloatingTextPrompt));
        OnPropertyChanged(nameof(HasOnboardingOverlay));
        OnPropertyChanged(nameof(HasOnboardingSelectionPrompt));
        OnPropertyChanged(nameof(HasOnboardingTextPrompt));
        OnPropertyChanged(nameof(HasOnboardingCountdown));
        OnPropertyChanged(nameof(CanCancelOnboardingSelectionPrompt));
        OnPropertyChanged(nameof(CanCancelOnboardingTextPrompt));
        OnPropertyChanged(nameof(OnboardingStageText));
        OnPropertyChanged(nameof(OnboardingTitle));
        OnPropertyChanged(nameof(OnboardingDescription));
        OnPropertyChanged(nameof(OnboardingInputKind));
    }

    private void AddToolOutputMessages(AgentRunResult result, string? workspacePath)
    {
        if (result.ToolOutput is not { Count: > 0 })
        {
            return;
        }

        foreach (string toolOutput in result.ToolOutput)
        {
            Messages.Add(new ChatMessage("Tool", toolOutput, statusNote: null, workspacePath: workspacePath));
        }
    }

    private void ReplaceConversationMessages(BackendSessionInfo sessionInfo, string? workspacePath)
    {
        Messages.Clear();

        if (sessionInfo.ConversationHistory.Count == 0)
        {
            string label = string.IsNullOrWhiteSpace(sessionInfo.SectionTitle)
                ? "Ready."
                : $"Ready. Section: {sessionInfo.SectionTitle}";
            Messages.Add(new ChatMessage("NanoAgent", label, statusNote: null, workspacePath: workspacePath));
            return;
        }

        foreach (BackendConversationMessage message in sessionInfo.ConversationHistory)
        {
            string role = string.Equals(message.Role, "user", StringComparison.Ordinal)
                ? "You"
                : "NanoAgent";
            Messages.Add(new ChatMessage(role, message.Content, statusNote: null, workspacePath: workspacePath));
        }
    }

    private void OnConversationMessageReceived(object? sender, ChatMessage message)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Messages.Add(message);
            return;
        }

        Dispatcher.UIThread.Post(() => Messages.Add(message));
    }

    private void OnSelectionPromptChanged(object? sender, DesktopSelectionPrompt? prompt)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ActiveSelectionPrompt = prompt;
            return;
        }

        Dispatcher.UIThread.Post(() => ActiveSelectionPrompt = prompt);
    }

    private void OnTextPromptChanged(object? sender, DesktopTextPrompt? prompt)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ActiveTextPrompt = prompt;
            return;
        }

        Dispatcher.UIThread.Post(() => ActiveTextPrompt = prompt);
    }

    private void UpdateProgressText()
    {
        if (_currentRunStartedAt is null)
        {
            ProgressText = "(0s \u00B7 0 tokens)";
            return;
        }

        ProgressText = FormatProgressText(GetLiveElapsed(), GetLiveEstimatedTokens());
    }

    private string? FormatFinalProgressText(AgentRunResult result, bool allowLiveFallback)
    {
        if (result.Elapsed is { } elapsed && result.EstimatedTokens is { } estimatedTokens)
        {
            return FormatProgressText(elapsed, estimatedTokens);
        }

        return allowLiveFallback ? ProgressText : null;
    }

    private static string FormatProgressText(TimeSpan elapsed, int estimatedTokens)
    {
        return $"({FormatElapsed(elapsed)} \u00B7 {FormatTokens(estimatedTokens)} tokens)";
    }

    private TimeSpan GetLiveElapsed()
    {
        return _currentRunStartedAt is null
            ? TimeSpan.Zero
            : DateTimeOffset.UtcNow - _currentRunStartedAt.Value;
    }

    private int GetLiveEstimatedTokens()
    {
        TimeSpan elapsed = GetLiveElapsed();
        return (int)Math.Floor(Math.Max(0d, elapsed.TotalSeconds) * EstimatedLiveTokensPerSecond);
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        int seconds = Math.Max(0, (int)Math.Floor(elapsed.TotalSeconds));
        TimeSpan normalized = TimeSpan.FromSeconds(seconds);

        if (normalized.TotalHours >= 1d)
        {
            return $"{(int)normalized.TotalHours}h {normalized.Minutes}m {normalized.Seconds}s";
        }

        if (normalized.TotalMinutes >= 1d)
        {
            return $"{(int)normalized.TotalMinutes}m {normalized.Seconds}s";
        }

        return $"{normalized.Seconds}s";
    }

    private static string FormatTokens(int estimatedTokens)
    {
        int safeValue = Math.Max(0, estimatedTokens);
        if (safeValue < 1_000)
        {
            return safeValue.ToString(CultureInfo.InvariantCulture);
        }

        double thousands = safeValue / 1_000d;
        string format = thousands >= 10d ? "0" : "0.#";
        double rounded = Math.Round(
            thousands,
            thousands >= 10d ? 0 : 1,
            MidpointRounding.AwayFromZero);

        return $"{rounded.ToString(format, CultureInfo.InvariantCulture)}k";
    }

}
