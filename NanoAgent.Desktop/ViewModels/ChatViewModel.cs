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
    private readonly DispatcherTimer _progressTimer;
    private readonly DispatcherTimer _selectionPromptTimer;
    private DateTimeOffset? _currentRunStartedAt;

    [ObservableProperty]
    private string _prompt = string.Empty;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _progressText = "(0s \u00B7 0 tokens)";

    [ObservableProperty]
    private string? _selectedModelId;

    [ObservableProperty]
    private string? _selectedThinkingMode = "off";

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
    private string _permissionToolPattern = string.Empty;

    [ObservableProperty]
    private string _permissionSubjectPattern = string.Empty;

    public ChatViewModel(AgentRunner agentRunner)
    {
        _agentRunner = agentRunner;
        _agentRunner.ConversationMessageReceived += OnConversationMessageReceived;
        _agentRunner.SelectionPromptChanged += OnSelectionPromptChanged;
        _agentRunner.TextPromptChanged += OnTextPromptChanged;
        RunPromptCommand = new AsyncRelayCommand<ProjectInfo?>(RunPromptAsync, CanRunPrompt);
        LoadSessionCommand = new AsyncRelayCommand<ProjectInfo?>(LoadSessionAsync, CanRunWorkspaceCommand);
        ApplyModelCommand = new AsyncRelayCommand<ProjectInfo?>(ApplyModelAsync, CanApplyModel);
        ApplyThinkingCommand = new AsyncRelayCommand<ProjectInfo?>(ApplyThinkingAsync, CanApplyThinking);
        ApplyProfileCommand = new AsyncRelayCommand<ProjectInfo?>(ApplyProfileAsync, CanApplyProfile);
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

    public string StatusText => IsRunning ? $"Working {ProgressText}" : "Ready";

    partial void OnPromptChanged(string value)
    {
        RunPromptCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsRunningChanged(bool value)
    {
        NotifyCommandStatesChanged();
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnProgressTextChanged(string value)
    {
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnSelectedModelIdChanged(string? value)
    {
        ApplyModelCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedThinkingModeChanged(string? value)
    {
        ApplyThinkingCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedProfileNameChanged(string? value)
    {
        ApplyProfileCommand.NotifyCanExecuteChanged();
    }

    partial void OnActiveSelectionPromptChanged(DesktopSelectionPrompt? value)
    {
        OnPropertyChanged(nameof(HasActiveSelectionPrompt));

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

    public async Task LoadSessionAsync(ProjectInfo? project)
    {
        if (project is null || IsRunning)
        {
            return;
        }

        await RunControlOperationAsync(
            "Loading controls",
            async () =>
            {
                BackendSessionInfo sessionInfo = await _agentRunner.GetSessionAsync(project.Path, SelectedSection?.SectionId);
                ApplySessionInfo(sessionInfo, replaceConversation: true);
                Activity.Add(new AgentEvent("settings", "Controls loaded."));
            });
    }

    private async Task ApplyModelAsync(ProjectInfo? project)
    {
        if (project is null || string.IsNullOrWhiteSpace(SelectedModelId))
        {
            return;
        }

        await RunControlOperationAsync(
            "Changing model",
            async () =>
            {
                AgentRunResult result = await _agentRunner.SetModelAsync(
                    project.Path,
                    SelectedModelId,
                    SelectedSection?.SectionId);
                ApplyRunResult(result);
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
            async () =>
            {
                AgentRunResult result = await _agentRunner.SetThinkingAsync(
                    project.Path,
                    SelectedThinkingMode,
                    SelectedSection?.SectionId);
                ApplyRunResult(result);
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
            async () =>
            {
                AgentRunResult result = await _agentRunner.SetProfileAsync(
                    project.Path,
                    SelectedProfileName,
                    SelectedSection?.SectionId);
                ApplyRunResult(result);
            });
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

        Messages.Add(new ChatMessage("You", prompt));
        Activity.Add(new AgentEvent("task", $"Running in {project.Name}"));

        _currentRunStartedAt = DateTimeOffset.UtcNow;
        UpdateProgressText();
        _progressTimer.Start();
        IsRunning = true;

        try
        {
            AgentRunResult result = await _agentRunner.RunAsync(
                project.Path,
                prompt,
                SelectedSection?.SectionId);
            string finalProgressText = FormatFinalProgressText(result);
            ApplySessionInfo(result.SessionInfo);

            AddToolOutputMessages(result);

            Messages.Add(new ChatMessage(
                "NanoAgent",
                string.IsNullOrWhiteSpace(result.ResponseText) ? "Task completed with no output." : result.ResponseText.Trim(),
                finalProgressText));

            foreach (string activity in result.Activity)
            {
                Activity.Add(new AgentEvent("agent", activity));
            }

            Activity.Add(new AgentEvent("done", "Task finished."));
            RunCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Messages.Add(new ChatMessage("NanoAgent", ex.Message));
            Activity.Add(new AgentEvent("error", ex.Message));
        }
        finally
        {
            _progressTimer.Stop();
            _currentRunStartedAt = null;
            IsRunning = false;
        }
    }

    private async Task RunControlOperationAsync(string description, Func<Task> operation)
    {
        _currentRunStartedAt = DateTimeOffset.UtcNow;
        UpdateProgressText();
        _progressTimer.Start();
        IsRunning = true;
        Activity.Add(new AgentEvent("settings", description));

        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            Messages.Add(new ChatMessage("NanoAgent", ex.Message));
            Activity.Add(new AgentEvent("error", ex.Message));
        }
        finally
        {
            _progressTimer.Stop();
            _currentRunStartedAt = null;
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
            async () =>
            {
                Messages.Add(new ChatMessage("You", command));
                AgentRunResult result = await _agentRunner.RunAsync(
                    project.Path,
                    command,
                    SelectedSection?.SectionId);
                ApplySessionInfo(result.SessionInfo);
                AddToolOutputMessages(result);
                Messages.Add(new ChatMessage(
                    "NanoAgent",
                    string.IsNullOrWhiteSpace(result.ResponseText)
                        ? "Command completed."
                        : result.ResponseText.Trim()));

                foreach (string activity in result.Activity)
                {
                    Activity.Add(new AgentEvent("command", activity));
                }

                RunCompleted?.Invoke(this, EventArgs.Empty);
            });
    }

    private void ApplyRunResult(AgentRunResult result)
    {
        ApplySessionInfo(result.SessionInfo);

        foreach (string activity in result.Activity)
        {
            Activity.Add(new AgentEvent("settings", activity));
        }
    }

    private void ApplySessionInfo(
        BackendSessionInfo? sessionInfo,
        bool replaceConversation = false)
    {
        if (sessionInfo is null)
        {
            return;
        }

        AvailableModels.Clear();
        foreach (string modelId in sessionInfo.AvailableModelIds)
        {
            AvailableModels.Add(modelId);
        }

        SelectedModelId = sessionInfo.ModelId;
        SelectedThinkingMode = sessionInfo.ThinkingMode;
        SelectedProfileName = sessionInfo.AgentProfileName;
        HasSessionOptions = true;
        if (replaceConversation)
        {
            ReplaceConversationMessages(sessionInfo);
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
        ShowHelpCommand.NotifyCanExecuteChanged();
        ShowModelsCommand.NotifyCanExecuteChanged();
        ShowPermissionsCommand.NotifyCanExecuteChanged();
        ShowRulesCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        AllowPermissionCommand.NotifyCanExecuteChanged();
        DenyPermissionCommand.NotifyCanExecuteChanged();
    }

    private void AddToolOutputMessages(AgentRunResult result)
    {
        if (result.ToolOutput is not { Count: > 0 })
        {
            return;
        }

        foreach (string toolOutput in result.ToolOutput)
        {
            Messages.Add(new ChatMessage("Tool", toolOutput));
        }
    }

    private void ReplaceConversationMessages(BackendSessionInfo sessionInfo)
    {
        Messages.Clear();

        if (sessionInfo.ConversationHistory.Count == 0)
        {
            string label = string.IsNullOrWhiteSpace(sessionInfo.SectionTitle)
                ? "Ready."
                : $"Ready. Section: {sessionInfo.SectionTitle}";
            Messages.Add(new ChatMessage("NanoAgent", label));
            return;
        }

        foreach (BackendConversationMessage message in sessionInfo.ConversationHistory)
        {
            string role = string.Equals(message.Role, "user", StringComparison.Ordinal)
                ? "You"
                : "NanoAgent";
            Messages.Add(new ChatMessage(role, message.Content));
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

        TimeSpan elapsed = DateTimeOffset.UtcNow - _currentRunStartedAt.Value;
        int estimatedTokens = (int)Math.Floor(Math.Max(0d, elapsed.TotalSeconds) * EstimatedLiveTokensPerSecond);
        ProgressText = FormatProgressText(elapsed, estimatedTokens);
    }

    private string FormatFinalProgressText(AgentRunResult result)
    {
        if (result.Elapsed is { } elapsed && result.EstimatedTokens is { } estimatedTokens)
        {
            return FormatProgressText(elapsed, estimatedTokens);
        }

        return ProgressText;
    }

    private static string FormatProgressText(TimeSpan elapsed, int estimatedTokens)
    {
        return $"({FormatElapsed(elapsed)} \u00B7 {FormatTokens(estimatedTokens)} tokens)";
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
