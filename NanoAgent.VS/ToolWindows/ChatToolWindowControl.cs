using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using EnvDTE;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Workspace.VSIntegration.Contracts;
using NanoAgent.VS.Services;
using Process = System.Diagnostics.Process;

namespace NanoAgent.VS.ToolWindows
{
    public sealed partial class ChatToolWindowControl : UserControl, IDisposable
    {
        private readonly LogService _log;
        private readonly VsAgentService _agentService;
        private readonly NanoAgentProcessManager _processManager;
        private readonly System.Collections.ObjectModel.ObservableCollection<ChatMessage> _messages = new();
        private readonly System.Collections.ObjectModel.ObservableCollection<string> _promptQueue = new(); // prompts typed while a turn runs
        private bool _disposed;
        private CancellationTokenSource? _turnCts;
        private bool _promptRunning;
        private bool _hostInitialized;
        private AssistantMessage? _streamingAssistant;
        private ReasoningMessage? _streamingReasoning;
        private SystemMessage? _fileEditsSummaryMessage; // single running "files changed" tally, updated in place.
        private readonly Dictionary<string, ToolCallStatusMessage> _toolCallMessages = new();
        private string? _activeModalRequestId;
        private string _workingDirectory = string.Empty;
        private string? _sessionWorkingDirectory; // cwd the live ACP session was created with.
        private bool _syncingWorkingDirectory;
        private SolutionEvents? _solutionEvents; // kept alive; GC'd events stop firing.
        private IVsFolderWorkspaceService? _folderWorkspaceService; // kept alive for unsubscribe.
        private string? _pendingExternalPrompt;
        private string? _authToken;
        private string[] _extraArgs = Array.Empty<string>();
        private bool _cliWasAutoResolved;

        // Latest session info, used by the model/settings pickers and status bar.
        private string? _modelId;
        private string? _profileName;
        private string? _thinkingMode;
        private string? _reasoningEffort;
        private string? _providerName;
        private readonly List<string> _availableModels = new();
        private readonly List<(string Name, string? Description)> _profiles = new();
        private long _contextWindow;
        private long _contextUsed;

        public ChatToolWindowControl()
        {
            InitializeComponent();

            _log = LogService.Instance;
            _processManager = new NanoAgentProcessManager();
            _agentService = new VsAgentService(_processManager);

            _agentService.HostExited += OnHostExited;
            _agentService.HostError += OnHostError;
            _agentService.NotificationReceived += OnNotificationReceived;

            MessagesList.ItemsSource = _messages;
            PromptQueueList.ItemsSource = _promptQueue;
            _promptQueue.CollectionChanged += (_, _) =>
                PromptQueueList.Visibility = _promptQueue.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            InputTextBox.TextChanged += (_, _) => { UpdateInputState(); UpdateSuggestions(); };
            SendButton.IsEnabled = false;

            // Route all chat hyperlink clicks (file refs + URLs) here.
            AddHandler(Hyperlink.RequestNavigateEvent, new RequestNavigateEventHandler(OnRequestNavigate));

            // only Loaded — Unloaded fires on every dock/tab/auto-hide change,
            // not just close. Disposal is owned by ChatToolWindow.OnClose/Dispose.
            Loaded += OnLoadedAsync;
        }

        private bool _initialized;

        private async void OnLoadedAsync(object sender, RoutedEventArgs e)
        {
            if (_initialized || _disposed) return; // Loaded can fire repeatedly; init once.
            _initialized = true;
            try { await InitializeAsync(); }
            catch (Exception ex)
            {
                _log.Error("Failed to initialize", ex);
                AddSystemMessage($"Failed to initialize: {ex.Message}");
            }
        }

        private async Task InitializeAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            NanoAgentOptionsPage? options = GetOptionsPageOrNull();
            _log.MinLevel = (options?.LogLevel ?? NanoAgentLogLevel.Info).ToString().ToLowerInvariant();
            _authToken = options?.AcpAuthenticationToken?.Trim();
            _extraArgs = SplitArgs(options?.ExtraArguments);

            _workingDirectory = ResolveWorkingDirectory();

            // Tool window can restore before the solution loads (package auto-loads on NoSolution),
            // leaving _workingDirectory at the process cwd. Re-resolve when a solution opens.
            // DTE SolutionEvents over IVsSolutionEvents COM advise — fires on the UI thread.
            if (_solutionEvents is null && Package.GetGlobalService(typeof(SDTE)) is DTE dte)
            {
                _solutionEvents = dte.Events.SolutionEvents;
                _solutionEvents.Opened += OnSolutionOpened;
            }

            // Open Folder/CMake mode fires no SolutionEvents; the workspace can load after the
            // tool window restores, leaving _workingDirectory at the process cwd. Re-resolve when it changes.
            if (_folderWorkspaceService is null &&
                Package.GetGlobalService(typeof(SComponentModel)) is IComponentModel cm &&
                cm.GetService<IVsFolderWorkspaceService>() is IVsFolderWorkspaceService fws)
            {
                _folderWorkspaceService = fws;
                _folderWorkspaceService.OnActiveWorkspaceChanged += OnActiveWorkspaceChanged;
            }

            string? cliExePath = await ResolveOrInstallCliAsync(options);
            if (cliExePath is null)
            {
                SetStatus("Error", "#FFF48771");
                return; // user declined install / not found; message already shown.
            }

            try
            {
                _agentService.Start(cliExePath, _extraArgs);
                _log.Info("NanoAgent ACP server started.");
                SetStatus("Starting...", "#FFCCA700");
                await InitializeAcpSessionAsync();

                if ((options?.CheckForUpdates ?? true) && _cliWasAutoResolved)
                {
                    _ = Task.Run(() => CheckForUpdateAsync(cliExePath));
                }
            }
            catch (Exception ex)
            {
                _log.Error("Failed to start NanoAgent ACP server", ex);
                SetStatus("Error", "#FFF48771");
                AddSystemMessage($"Failed to start CLI '{cliExePath}': {ex.Message}");
            }
        }

        private async Task<string?> ResolveOrInstallCliAsync(NanoAgentOptionsPage? options)
        {
            string? configured = options?.NanoAgentCliPath?.Trim();
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured; // explicit path — trust it.
            }

            _cliWasAutoResolved = true;
            string command = NanoAgentCli.ResolveCommand("nanoai", _log);
            if (NanoAgentCli.IsAvailable(command))
            {
                return command;
            }

            if (!(options?.AutoInstall ?? true) || VSPackage.Instance?.Confirm(
                    "Install NanoAgent CLI",
                    "NanoAgent CLI (nanoai) was not found.\n\nInstall it globally now? Runs: npm install -g nanoai-cli") != true)
            {
                AddSystemMessage("NanoAgent CLI 'nanoai' was not found. Install it (npm install -g nanoai-cli) or set its path in Tools → Options → NanoAgent.");
                return null;
            }

            SetStatus("Installing...", "#FFCCA700");
            AddSystemMessage("Installing NanoAgent CLI via npm…");
            bool ok = await NanoAgentCli.NpmInstallAsync("nanoai-cli", _log);

            command = NanoAgentCli.ResolveCommand("nanoai", _log);
            if (!ok || !NanoAgentCli.IsAvailable(command))
            {
                AddSystemMessage("Failed to install nanoai. Run 'npm install -g nanoai-cli' manually (Node.js/npm required), then restart Visual Studio.");
                return null;
            }

            _log.Info("nanoai installed successfully.");
            AddSystemMessage("NanoAgent CLI installed.");
            return command;
        }

        private async Task CheckForUpdateAsync(string command)
        {
            try
            {
                if (!NanoAgentCli.ShouldCheckForUpdate()) return;
                NanoAgentCli.RecordUpdateCheck();

                (string? current, string? latest) = NanoAgentCli.CheckVersions(command, _log);
                if (current is null || latest is null || !NanoAgentCli.IsNewer(latest, current)) return;

                _log.Info($"nanoai update available: {current} -> {latest}");
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                bool update = VSPackage.Instance?.Confirm("NanoAgent Update",
                    $"A new NanoAgent CLI is available ({current} → {latest}). Update now?") == true;
                if (!update) return;

                SetStatus("Updating...", "#FFCCA700");
                bool ok = await NanoAgentCli.NpmInstallAsync("nanoai-cli@latest", _log);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (ok)
                {
                    AddSystemMessage("NanoAgent CLI updated. Restart the chat (＋ New) or Visual Studio to use the new version.");
                }
                else
                {
                    AddSystemMessage("Failed to update nanoai. See logs for details.");
                }
                SetStatus("Ready", "#FF73C991");
            }
            catch (Exception ex)
            {
                _log.Debug("Update check skipped: " + ex.Message);
            }
        }

        private static string[] SplitArgs(string? args)
            => string.IsNullOrWhiteSpace(args)
                ? Array.Empty<string>()
                : args!.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        private void OnHostExited()
        {
            _log.Warn("NanoAgent ACP server exited.");
            _hostInitialized = false;
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async delegate
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                SetStatus("Stopped", "#FF8F8F8F");
                _promptRunning = false;
                UpdateInputState();
            });
        }

        private void OnHostError(string error)
        {
            _log.Error($"NanoAgent ACP error: {error}");
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async delegate
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                AddSystemMessage($"Backend error: {error}");
            });
        }

        private async void OnNotificationReceived(string method, Dictionary<string, object?>? parameters)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            try { await HandleNotificationAsync(method, parameters); }
            catch (Exception ex) { _log.Error($"Notification failed: {method}", ex); }
        }

        private async Task HandleNotificationAsync(string method, Dictionary<string, object?>? parameters)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            switch (method)
            {
                case VsProtocol.MessageChunk:
                    {
                        string? text = GetParamString(parameters, "text");
                        if (text is not null) AppendMessageChunk(text, "assistant");
                        break;
                    }

                case VsProtocol.ReasoningChunk:
                    {
                        string? text = GetParamString(parameters, "text");
                        if (text is not null) AppendMessageChunk(text, "reasoning");
                        break;
                    }

                case VsProtocol.UserMessageChunk:
                    break;

                case VsProtocol.ToolCallStart:
                    {
                        string id = GetParamString(parameters, "toolCallId") ?? Guid.NewGuid().ToString("N");
                        if (!_toolCallMessages.TryGetValue(id, out ToolCallStatusMessage? msg))
                        {
                            msg = new ToolCallStatusMessage { ToolCallId = id };
                            _toolCallMessages[id] = msg;
                            _messages.Add(msg);
                        }
                        string? newTitle = GetParamString(parameters, "title");
                        if (!string.IsNullOrEmpty(newTitle)) msg.Title = newTitle!;
                        else if (string.IsNullOrEmpty(msg.Title)) msg.Title = "Tool";
                        msg.Kind = GetParamString(parameters, "kind") ?? msg.Kind;
                        msg.Status = GetParamString(parameters, "status") ?? "running";
                        string? raw = GetRawInputJson(parameters);
                        if (!string.IsNullOrEmpty(raw)) msg.RawInput = raw!;
                        ScrollToBottom();
                        break;
                    }

                case VsProtocol.ToolCallEnd:
                    {
                        string? id = GetParamString(parameters, "toolCallId");
                        if (id is not null && _toolCallMessages.TryGetValue(id, out ToolCallStatusMessage? msg))
                        {
                            string? raw = GetRawInputJson(parameters);
                            if (!string.IsNullOrEmpty(raw)) msg.RawInput = raw!;
                            foreach (string line in GetParamArray(parameters, "content")) msg.ContentLines.Add(line);
                            msg.Status = GetParamString(parameters, "status") ?? "completed";
                        }
                        break;
                    }

                case VsProtocol.PlanUpdate:
                    {
                        string? planText = FormatPlanUpdate(parameters);
                        if (!string.IsNullOrEmpty(planText)) { _messages.Add(new SystemMessage { Text = $"Plan:\n{planText}" }); ScrollToBottom(); }
                        break;
                    }

                case VsProtocol.FileEditsSummary:
                    UpdateFileEditsSummary(parameters);
                    break;

                case VsProtocol.RequestPermission:
                    ShowModalRequest(parameters, "permission");
                    break;

                case VsProtocol.RequestText:
                    ShowModalRequest(parameters, "text");
                    break;

                case VsProtocol.SessionInfo:
                    ApplySessionInfo(parameters);
                    break;

                case VsProtocol.Ready:
                    SetStatus("Ready", "#FF73C991");
                    await FlushPendingExternalPromptAsync();
                    break;
            }
        }

        private void ApplySessionInfo(Dictionary<string, object?>? p)
        {
            if (p is null) return;

            _modelId = GetParamString(p, "modelId") ?? _modelId;
            _profileName = GetParamString(p, "agentProfileName") ?? _profileName;
            _thinkingMode = GetParamString(p, "thinkingMode") ?? _thinkingMode;
            _reasoningEffort = GetParamString(p, "reasoningEffort") ?? _reasoningEffort;
            _providerName = GetParamString(p, "providerName") ?? _providerName;

            List<string> models = GetParamStringArrayList(p, "availableModelIds");
            if (models.Count > 0) { _availableModels.Clear(); _availableModels.AddRange(models); }

            List<(string, string?)> profiles = GetProfiles(p, "availableAgentProfiles");
            if (profiles.Count > 0) { _profiles.Clear(); _profiles.AddRange(profiles); }

            _contextWindow = GetParamLong(p, "activeModelContextWindowTokens") ?? _contextWindow;
            _contextUsed = GetParamLong(p, "sectionEstimatedContextTokens") ?? _contextUsed;

            ModelText.Text = string.IsNullOrEmpty(_modelId)
                ? (string.IsNullOrEmpty(_profileName) ? "" : _profileName)
                : (string.IsNullOrEmpty(_profileName) ? _modelId : $"{_modelId} · {_profileName}");

            UpdateContextText();
        }

        private void UpdateContextText()
        {
            if (_contextWindow > 0)
            {
                int pct = (int)Math.Round(100.0 * _contextUsed / _contextWindow);
                ContextText.Text = $"{FormatTokens(_contextUsed)}/{FormatTokens(_contextWindow)} ({pct}%)";
                ContextText.Foreground = pct >= 80
                    ? new SolidColorBrush(Color.FromRgb(0xF4, 0x87, 0x71))
                    : new SolidColorBrush(Color.FromRgb(0x8F, 0x8F, 0x8F));
            }
            else
            {
                ContextText.Text = "";
            }
        }

        private static string FormatTokens(long n) => n >= 1000 ? $"{n / 1000.0:0.#}k" : n.ToString();

        private void AppendMessageChunk(string text, string role)
        {
            if (role == "reasoning")
            {
                if (_streamingReasoning is null)
                {
                    _streamingReasoning = new ReasoningMessage { Text = text };
                    _messages.Add(_streamingReasoning);
                }
                else _streamingReasoning.Text += text;
                _streamingReasoning.NotifyDerived();
            }
            else
            {
                if (_streamingAssistant is null)
                {
                    _streamingAssistant = new AssistantMessage { Text = text };
                    _messages.Add(_streamingAssistant);
                }
                else _streamingAssistant.Text += text;
            }
            ScrollToBottom();
        }

        private void AddSystemMessage(string text)
        {
            _messages.Add(new SystemMessage { Text = text });
            ScrollToBottom();
        }

        // ───── Composer ─────

        private async void OnSendClick(object sender, RoutedEventArgs e)
        {
            if (_promptRunning) await CancelTurnAsync();
            else await SendCurrentInput();
        }

        private async void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (SuggestionPopup.IsOpen && SuggestionList.Items.Count > 0)
            {
                switch (e.Key)
                {
                    case Key.Down:
                        SuggestionList.SelectedIndex = Math.Min(SuggestionList.SelectedIndex + 1, SuggestionList.Items.Count - 1);
                        SuggestionList.ScrollIntoView(SuggestionList.SelectedItem);
                        e.Handled = true;
                        return;
                    case Key.Up:
                        SuggestionList.SelectedIndex = Math.Max(SuggestionList.SelectedIndex - 1, 0);
                        SuggestionList.ScrollIntoView(SuggestionList.SelectedItem);
                        e.Handled = true;
                        return;
                    case Key.Tab:
                        AcceptSuggestion();
                        e.Handled = true;
                        return;
                    case Key.Enter:
                        AcceptSuggestion();
                        e.Handled = true;
                        return;
                    case Key.Escape:
                        SuggestionPopup.IsOpen = false;
                        e.Handled = true;
                        return;
                }
            }

            if (e.Key == Key.Enter && !Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
            {
                e.Handled = true;
                await SendCurrentInput();
            }
        }

        private async Task SendCurrentInput()
        {
            SuggestionPopup.IsOpen = false;
            string text = InputTextBox.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;

            // Busy? queue it; the queue drains automatically when the turn ends.
            if (_promptRunning)
            {
                _promptQueue.Add(text);
                InputTextBox.Clear();
                return;
            }

            InputTextBox.Clear();
            _messages.Add(new UserMessage { Text = text });
            ScrollToBottom();

            if (text.StartsWith("/")) await HandleSlashCommand(text);
            else await HandleUserPromptAsync(text);
        }

        private void OnRemoveQueuedClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string queued }) _promptQueue.Remove(queued);
        }

        private async Task DrainPromptQueueAsync()
        {
            if (_promptRunning || _promptQueue.Count == 0) return;

            string text = _promptQueue[0];
            _promptQueue.RemoveAt(0);
            _messages.Add(new UserMessage { Text = text });
            ScrollToBottom();

            if (text.StartsWith("/")) await HandleSlashCommand(text);
            else await HandleUserPromptAsync(text);
        }

        private async Task HandleSlashCommand(string command)
        {
            switch (command)
            {
                case "/clear":
                    ClearConversation();
                    return;
                case "/new":
                    await StartNewSessionAsync();
                    return;
                case "/models":
                    ShowModelPicker();
                    return;
                case "/session" when false: // handled by host
                    return;
            }

            if (command.StartsWith("/resume ", StringComparison.Ordinal))
            {
                await ResumeSessionAsync(command.Substring("/resume ".Length).Trim());
                return;
            }

            await SendHostCommandAsync(command);
        }

        private void ClearConversation()
        {
            _messages.Clear();
            _toolCallMessages.Clear();
            _streamingAssistant = null;
            _streamingReasoning = null;
            _fileEditsSummaryMessage = null;
            EmptyStateText.Visibility = Visibility.Visible;
        }

        private async Task HandleUserPromptAsync(string prompt)
        {
            _turnCts?.Cancel();
            _turnCts = new CancellationTokenSource();

            _promptRunning = true;
            _streamingAssistant = null;
            _streamingReasoning = null;
            _toolCallMessages.Clear();
            MetricsText.Text = "";
            UpdateInputState();

            try
            {
                VsAgentService.SessionPromptResponse? response = await _agentService.SendPromptAsync(prompt);
                ShowMetrics(response?.Metrics);
            }
            catch (Exception ex)
            {
                _log.Error("Prompt failed", ex);
                AddSystemMessage($"Error: {ex.Message}");
            }
            finally
            {
                _promptRunning = false;
                CompletePendingToolCalls();
                _streamingAssistant = null;
                _streamingReasoning = null;
                UpdateInputState();
                _ = DrainPromptQueueAsync();
            }
        }

        private void ShowMetrics(VsAgentService.TurnMetrics? m)
        {
            if (m is null) { MetricsText.Text = ""; return; }
            var parts = new List<string>();
            if (m.ElapsedMilliseconds is > 0) parts.Add($"{m.ElapsedMilliseconds.Value / 1000.0:0.0}s");
            if (m.EstimatedOutputTokens is > 0) parts.Add($"{FormatTokens((long)m.EstimatedOutputTokens.Value)} tok");
            long rounds = (long)(m.ToolRoundCount ?? 0);
            if (rounds > 0) parts.Add($"{rounds} round{(rounds == 1 ? "" : "s")}");
            long retries = (long)(m.ProviderRetryCount ?? 0);
            if (retries > 0) parts.Add($"{retries} retr{(retries == 1 ? "y" : "ies")}");
            MetricsText.Text = string.Join(" · ", parts);
        }

        private async Task SendHostCommandAsync(string command)
        {
            _promptRunning = true;
            _streamingAssistant = null;
            _streamingReasoning = null;
            UpdateInputState();

            try { await _agentService.SendPromptAsync(command); }
            catch (Exception ex)
            {
                _log.Error($"Command failed: {command}", ex);
                AddSystemMessage($"Error: {ex.Message}");
            }
            finally
            {
                _promptRunning = false;
                UpdateInputState();
                _ = DrainPromptQueueAsync();
            }
        }

        private async Task CancelTurnAsync()
        {
            _turnCts?.Cancel();
            await _agentService.CancelPromptAsync();
            _promptRunning = false;
            _streamingAssistant = null;
            _streamingReasoning = null;
            UpdateInputState();
        }

        // ───── Autocomplete ─────

        private void UpdateSuggestions()
        {
            string text = InputTextBox.Text;
            int caret = InputTextBox.CaretIndex;

            // Suggest model ids after "/use " and profiles after "/profile ".
            if (text.StartsWith("/use ", StringComparison.OrdinalIgnoreCase))
            {
                string q = text.Substring(5).Trim().ToLowerInvariant();
                ShowSuggestionItems(_availableModels
                    .Where(m => m.ToLowerInvariant().Contains(q))
                    .Select(m => new ChatCommandSuggestion(m, "", "Switch model", "/use " + m)));
                return;
            }
            if (text.StartsWith("/profile ", StringComparison.OrdinalIgnoreCase))
            {
                string q = text.Substring(9).Trim().ToLowerInvariant();
                ShowSuggestionItems(_profiles
                    .Where(pr => pr.Name.ToLowerInvariant().Contains(q))
                    .Select(pr => new ChatCommandSuggestion(pr.Name, "", pr.Description ?? "Agent profile", "/profile " + pr.Name)));
                return;
            }

            // Slash-command catalog while typing the first token.
            if (text.StartsWith("/") && !text.TrimEnd().Contains(' ') && caret <= text.Length)
            {
                ShowSuggestionItems(ChatCommands.Match(text.Trim()));
                return;
            }

            SuggestionPopup.IsOpen = false;
        }

        private void ShowSuggestionItems(IEnumerable<ChatCommandSuggestion> items)
        {
            var list = items.Take(50).ToList();
            if (list.Count == 0) { SuggestionPopup.IsOpen = false; return; }
            SuggestionList.ItemsSource = list;
            SuggestionList.SelectedIndex = 0;
            SuggestionPopup.IsOpen = true;
        }

        private void AcceptSuggestion()
        {
            if (SuggestionList.SelectedItem is ChatCommandSuggestion s)
            {
                InputTextBox.Text = s.InsertText;
                InputTextBox.CaretIndex = InputTextBox.Text.Length;
                InputTextBox.Focus();
            }
            SuggestionPopup.IsOpen = false;
        }

        private void OnSuggestionClick(object sender, MouseButtonEventArgs e)
        {
            if (SuggestionList.SelectedItem is not null) AcceptSuggestion();
        }

        // ───── Toolbar ─────

        private async void OnNewChatClick(object sender, RoutedEventArgs e) => await StartNewSessionAsync();

        private void OnAddContextClick(object sender, RoutedEventArgs e)
        {
            PrefillInput("/read ");
        }

        private void OnModelClick(object sender, RoutedEventArgs e) => ShowModelPicker();

        private async void OnSessionsClick(object sender, RoutedEventArgs e) => await ShowSessionBrowserAsync();

        private void OnSettingsClick(object sender, RoutedEventArgs e) => ShowSettingsPanel();

        // ───── External entry points (editor commands) ─────

        public async Task SubmitExternalPromptAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (!_hostInitialized || !_agentService.IsConnected)
            {
                _pendingExternalPrompt = text;
                return;
            }
            _messages.Add(new UserMessage { Text = text });
            ScrollToBottom();
            await HandleUserPromptAsync(text);
        }

        public void PrefillInput(string text)
        {
            ShowSettingsClose();
            InputTextBox.Text = text;
            InputTextBox.CaretIndex = text.Length;
            InputTextBox.Focus();
        }

        public async Task RunExternalCommandAsync(string command) => await SendHostCommandAsync(command);

        public Task NewChatAsync() => StartNewSessionAsync();

        private async Task FlushPendingExternalPromptAsync()
        {
            string? pending = _pendingExternalPrompt;
            _pendingExternalPrompt = null;
            if (!string.IsNullOrEmpty(pending)) await SubmitExternalPromptAsync(pending!);
        }

        // ───── Sessions ─────

        private async Task StartNewSessionAsync()
        {
            try
            {
                await _agentService.NewSessionAsync(_workingDirectory);
                ClearConversation();
                SetStatus("Ready", "#FF73C991");
            }
            catch (Exception ex)
            {
                _log.Error("Failed to start new ACP session", ex);
                AddSystemMessage($"Error: {ex.Message}");
            }
        }

        private async Task ResumeSessionAsync(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                AddSystemMessage("Usage: /resume <session-id>");
                return;
            }

            try
            {
                await _agentService.LoadSessionAsync(_workingDirectory, sessionId);
                ClearConversation();
                AddSystemMessage($"Resumed session {sessionId}");
                SetStatus("Ready", "#FF73C991");
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to resume ACP session: {sessionId}", ex);
                AddSystemMessage($"Error: {ex.Message}");
            }
        }

        private async Task ShowSessionBrowserAsync()
        {
            List<VsAgentService.SessionSummary> sessions;
            try { sessions = await _agentService.ListSessionsAsync(); }
            catch (Exception ex) { AddSystemMessage($"Failed to list sessions: {ex.Message}"); return; }

            ModalTitle.Text = "Sessions";
            ModalDescription.Text = $"{sessions.Count} saved session{(sessions.Count == 1 ? "" : "s")}.";
            ModalDescription.Visibility = Visibility.Visible;
            ModalBodyPanel.Children.Clear();
            ModalActionsPanel.Children.Clear();

            foreach (VsAgentService.SessionSummary s in sessions)
            {
                var btn = MenuButton($"{s.Title}", Subtitle(s));
                string id = s.SessionId;
                btn.Click += async (_, _) => { CloseModal(); await ResumeSessionAsync(id); };
                ModalBodyPanel.Children.Add(btn);
            }

            var fork = SecondaryButton("Fork current");
            fork.Click += async (_, _) => { CloseModal(); await SendHostCommandAsync("/fork"); };
            var export = SecondaryButton("Export (JSON)");
            export.Click += async (_, _) => { CloseModal(); await SendHostCommandAsync("/export json"); };
            ModalActionsPanel.Children.Add(fork);
            ModalActionsPanel.Children.Add(export);
            ModalActionsPanel.Children.Add(CloseButton());

            ModalOverlay.Visibility = Visibility.Visible;

            static string Subtitle(VsAgentService.SessionSummary s)
            {
                var bits = new List<string>();
                if (!string.IsNullOrEmpty(s.ModelId)) bits.Add(s.ModelId!);
                if (s.TurnCount is > 0) bits.Add($"{s.TurnCount} turns");
                if (!string.IsNullOrEmpty(s.ParentSessionId)) bits.Add("fork");
                if (!string.IsNullOrEmpty(s.UpdatedAtUtc)) bits.Add(s.UpdatedAtUtc!);
                return string.Join(" · ", bits);
            }
        }

        // ───── Model picker ─────

        private void ShowModelPicker()
        {
            ModalTitle.Text = "Model & reasoning";
            ModalDescription.Text = string.IsNullOrEmpty(_modelId) ? "" : $"Current: {_modelId}";
            ModalDescription.Visibility = string.IsNullOrEmpty(ModalDescription.Text) ? Visibility.Collapsed : Visibility.Visible;
            ModalBodyPanel.Children.Clear();
            ModalActionsPanel.Children.Clear();

            ModalBodyPanel.Children.Add(SectionLabel("Models"));
            if (_availableModels.Count == 0)
            {
                ModalBodyPanel.Children.Add(Hint("No models reported yet. Run /models to refresh."));
            }
            foreach (string model in _availableModels)
            {
                var btn = MenuButton(model, model == _modelId ? "current" : "");
                string m = model;
                btn.Click += async (_, _) => { CloseModal(); await SendHostCommandAsync("/use " + m); };
                ModalBodyPanel.Children.Add(btn);
            }

            ModalBodyPanel.Children.Add(SectionLabel("Reasoning effort"));
            foreach (string level in new[] { "none", "minimal", "low", "medium", "high", "xhigh", "max" })
            {
                var btn = MenuButton(level, level.Equals(_reasoningEffort, StringComparison.OrdinalIgnoreCase) ? "current" : "");
                string l = level;
                btn.Click += async (_, _) => { CloseModal(); await SendHostCommandAsync("/reasoning " + l); };
                ModalBodyPanel.Children.Add(btn);
            }

            ModalBodyPanel.Children.Add(SectionLabel("Thinking"));
            var on = MenuButton("on", string.Equals(_thinkingMode, "on", StringComparison.OrdinalIgnoreCase) ? "current" : "");
            on.Click += async (_, _) => { CloseModal(); await SendHostCommandAsync("/thinking on"); };
            var off = MenuButton("off", string.Equals(_thinkingMode, "off", StringComparison.OrdinalIgnoreCase) ? "current" : "");
            off.Click += async (_, _) => { CloseModal(); await SendHostCommandAsync("/thinking off"); };
            ModalBodyPanel.Children.Add(on);
            ModalBodyPanel.Children.Add(off);

            ModalActionsPanel.Children.Add(CloseButton());
            ModalOverlay.Visibility = Visibility.Visible;
        }

        // ───── Settings ─────

        private void ShowSettingsPanel()
        {
            ModalTitle.Text = "Settings";
            var summary = new List<string>();
            if (!string.IsNullOrEmpty(_providerName)) summary.Add("Provider: " + _providerName);
            if (!string.IsNullOrEmpty(_modelId)) summary.Add("Model: " + _modelId);
            if (!string.IsNullOrEmpty(_profileName)) summary.Add("Profile: " + _profileName);
            if (!string.IsNullOrEmpty(_thinkingMode)) summary.Add("Thinking: " + _thinkingMode);
            ModalDescription.Text = string.Join("\n", summary);
            ModalDescription.Visibility = summary.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ModalBodyPanel.Children.Clear();
            ModalActionsPanel.Children.Clear();

            AddSettingButton("Model & reasoning…", () => { CloseModal(); ShowModelPicker(); });
            AddSettingButton("Browse sessions…", async () => { CloseModal(); await ShowSessionBrowserAsync(); });
            AddSettingButton("Manage plugins…", () => { CloseModal(); ShowPluginPanel(); });
            AddSettingButton("Show config (/config)", async () => { CloseModal(); await SendHostCommandAsync("/config"); });
            AddSettingButton("Re-run onboarding (/onboard)", async () => { CloseModal(); await SendHostCommandAsync("/onboard"); });
            AddSettingButton("Show MCP servers (/mcp)", async () => { CloseModal(); await SendHostCommandAsync("/mcp"); });
            AddSettingButton("Open Visual Studio options…", () =>
            {
                CloseModal();
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    VSPackage.Instance?.ShowOptionPageInternal();
                });
            });

            ModalActionsPanel.Children.Add(CloseButton());
            ModalOverlay.Visibility = Visibility.Visible;
        }

        private void AddSettingButton(string label, Action onClick)
        {
            var btn = MenuButton(label, "");
            btn.Click += (_, _) => onClick();
            ModalBodyPanel.Children.Add(btn);
        }

        private void ShowSettingsClose() => CloseModal();

        // ───── Plugin marketplace panel ─────

        private sealed record MarketplaceEntry(string Alias, string Repository, string Ref);
        private sealed record InstalledEntry(string PluginId, string MarketplaceAlias, string Repository, string Ref, int FileCount);

        private void ShowPluginPanel()
        {
            (List<MarketplaceEntry> markets, List<InstalledEntry> installed) = ReadPluginState();

            ModalTitle.Text = "Plugins";
            ModalDescription.Visibility = Visibility.Collapsed;
            ModalBodyPanel.Children.Clear();
            ModalActionsPanel.Children.Clear();

            // Installed plugins
            ModalBodyPanel.Children.Add(SectionLabel("Installed plugins"));
            if (installed.Count == 0)
            {
                ModalBodyPanel.Children.Add(Hint("No plugins installed yet."));
            }
            foreach (InstalledEntry plugin in installed)
            {
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(plugin.Repository)) parts.Add($"{plugin.Repository}@{plugin.Ref}");
                if (!string.IsNullOrEmpty(plugin.MarketplaceAlias)) parts.Add("via " + plugin.MarketplaceAlias);
                parts.Add($"{plugin.FileCount} file{(plugin.FileCount == 1 ? "" : "s")}");

                var row = PluginRow(plugin.PluginId, string.Join(" · ", parts));
                var uninstall = DangerButton("Uninstall");
                string id = plugin.PluginId;
                uninstall.Click += async (_, _) => await RunPluginAsync($"/plugin uninstall {id}");
                row.Children.Add(uninstall);
                ModalBodyPanel.Children.Add(row);
            }

            // Install a plugin
            ModalBodyPanel.Children.Add(SectionLabel("Install a plugin"));
            if (markets.Count == 0)
            {
                ModalBodyPanel.Children.Add(Hint("Add a marketplace below before installing."));
            }
            else
            {
                var idInput = ModalInput("plugin-id");
                var select = new ComboBox
                {
                    Margin = new Thickness(0, 0, 0, 6),
                    Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
                    Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC))
                };
                foreach (MarketplaceEntry m in markets) select.Items.Add($"{m.Alias} ({m.Repository})");
                select.SelectedIndex = 0;
                var force = new CheckBox
                {
                    Content = "Overwrite existing files (--force)",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                    Margin = new Thickness(0, 0, 0, 6)
                };
                var installBtn = PrimaryButton("Install");
                installBtn.Click += async (_, _) =>
                {
                    string pid = idInput.Text.Trim();
                    if (string.IsNullOrEmpty(pid)) { idInput.Focus(); return; }
                    string alias = markets[Math.Max(0, select.SelectedIndex)].Alias;
                    await RunPluginAsync($"/plugin install {pid}@{alias}{(force.IsChecked == true ? " --force" : "")}");
                };
                ModalBodyPanel.Children.Add(idInput);
                ModalBodyPanel.Children.Add(select);
                ModalBodyPanel.Children.Add(force);
                ModalBodyPanel.Children.Add(installBtn);
            }

            // Marketplaces
            ModalBodyPanel.Children.Add(SectionLabel("Marketplaces"));
            if (markets.Count == 0)
            {
                ModalBodyPanel.Children.Add(Hint("No marketplaces configured."));
            }
            foreach (MarketplaceEntry m in markets)
            {
                var row = PluginRow(m.Alias, $"{m.Repository}@{m.Ref}");
                var browse = SecondaryButton("Browse");
                string alias = m.Alias;
                browse.Click += async (_, _) => { CloseModal(); await SendHostCommandAsync($"/plugin browse {alias}"); };
                var remove = DangerButton("Remove");
                remove.Click += async (_, _) => await RunPluginAsync($"/plugin marketplace remove {alias}");
                row.Children.Add(browse);
                row.Children.Add(remove);
                ModalBodyPanel.Children.Add(row);
            }

            var repoInput = ModalInput("owner/repo");
            var addBtn = SecondaryButton("Add marketplace");
            addBtn.Click += async (_, _) =>
            {
                string repo = repoInput.Text.Trim();
                if (string.IsNullOrEmpty(repo)) { repoInput.Focus(); return; }
                await RunPluginAsync($"/plugin marketplace add {repo}");
            };
            ModalBodyPanel.Children.Add(repoInput);
            ModalBodyPanel.Children.Add(addBtn);

            var refresh = SecondaryButton("Refresh");
            refresh.Click += (_, _) => ShowPluginPanel();
            ModalActionsPanel.Children.Add(refresh);
            ModalActionsPanel.Children.Add(CloseButton());

            ModalOverlay.Visibility = Visibility.Visible;
        }

        private async Task RunPluginAsync(string command)
        {
            CloseModal();
            await SendHostCommandAsync(command);
            ShowPluginPanel(); // re-read state from disk after the CLI applies the change.
        }

        private (List<MarketplaceEntry>, List<InstalledEntry>) ReadPluginState()
        {
            var markets = new List<MarketplaceEntry>();
            var installed = new List<InstalledEntry>();

            foreach (string root in PluginRoots())
            {
                string dir = Path.Combine(root, ".nanoagent", "plugins");
                if (markets.Count == 0) markets = ParseMarketplaces(Path.Combine(dir, "marketplaces.json"));
                if (installed.Count == 0) installed = ParseInstalled(Path.Combine(dir, "installed.json"));
            }
            return (markets, installed);
        }

        private IEnumerable<string> PluginRoots()
        {
            if (!string.IsNullOrEmpty(_workingDirectory)) yield return _workingDirectory;
            yield return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        private List<MarketplaceEntry> ParseMarketplaces(string path)
        {
            var result = new List<MarketplaceEntry>();
            try
            {
                if (!File.Exists(path)) return result;
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("marketplaces", out JsonElement map) && map.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty p in map.EnumerateObject())
                    {
                        result.Add(new MarketplaceEntry(
                            p.Name,
                            TryGetJsonString(p.Value, "repository") ?? "",
                            TryGetJsonString(p.Value, "ref") ?? "main"));
                    }
                }
            }
            catch (Exception ex) { _log.Debug($"Failed to read marketplaces.json: {ex.Message}"); }
            return result;
        }

        private List<InstalledEntry> ParseInstalled(string path)
        {
            var result = new List<InstalledEntry>();
            try
            {
                if (!File.Exists(path)) return result;
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("plugins", out JsonElement map) && map.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty p in map.EnumerateObject())
                    {
                        int files = p.Value.TryGetProperty("files", out JsonElement f) && f.ValueKind == JsonValueKind.Array ? f.GetArrayLength() : 0;
                        result.Add(new InstalledEntry(
                            p.Name,
                            TryGetJsonString(p.Value, "marketplaceAlias") ?? "",
                            TryGetJsonString(p.Value, "repository") ?? "",
                            TryGetJsonString(p.Value, "ref") ?? "main",
                            files));
                    }
                }
            }
            catch (Exception ex) { _log.Debug($"Failed to read installed.json: {ex.Message}"); }
            return result;
        }

        private static StackPanel PluginRow(string title, string subtitle)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            var info = new StackPanel { Width = 280 };
            info.Children.Add(new TextBlock { Text = title, Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)) });
            info.Children.Add(new TextBlock { Text = subtitle, Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0x8F, 0x8F)), FontSize = 11, TextWrapping = TextWrapping.Wrap });
            row.Children.Add(info);
            return row;
        }

        private TextBox ModalInput(string placeholder) => new()
        {
            // no placeholder watermark; Tag holds the hint, kept minimal.
            Tag = placeholder,
            MinWidth = 240, MinHeight = 28,
            Margin = new Thickness(0, 0, 0, 6),
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 2, 6, 2),
            CaretBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC))
        };

        private Button PrimaryButton(string text)
        {
            var btn = SecondaryButton(text);
            btn.Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x63, 0x9C));
            btn.Foreground = Brushes.White;
            btn.BorderThickness = new Thickness(0);
            return btn;
        }

        private Button DangerButton(string text)
        {
            var btn = SecondaryButton(text);
            btn.Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0x87, 0x71));
            btn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x66, 0xF4, 0x87));
            return btn;
        }

        // ───── Modal building blocks ─────

        private void CloseModal()
        {
            ModalOverlay.Visibility = Visibility.Collapsed;
            _activeModalRequestId = null;
        }

        private static TextBlock SectionLabel(string text) => new()
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0x8F, 0x8F)),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 4)
        };

        private static TextBlock Hint(string text) => new()
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0x8F, 0x8F)),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        };

        private static Button MenuButton(string title, string meta)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock { Text = title, Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)) });
            if (!string.IsNullOrEmpty(meta))
            {
                content.Children.Add(new TextBlock
                {
                    Text = meta,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0x8F, 0x8F)),
                    FontSize = 10,
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            return new Button
            {
                Content = content,
                MinHeight = 30,
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 0, 4),
                Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
                BorderThickness = new Thickness(1),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Cursor = Cursors.Hand
            };
        }

        private Button SecondaryButton(string text) => new()
        {
            Content = text,
            MinWidth = 80, MinHeight = 28,
            Padding = new Thickness(10, 0, 10, 0),
            Margin = new Thickness(4, 0, 0, 0),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand
        };

        private Button CloseButton()
        {
            var btn = SecondaryButton("Close");
            btn.Click += (_, _) => CloseModal();
            return btn;
        }

        // ───── Permission / text request modal ─────

        private void ShowModalRequest(Dictionary<string, object?>? parameters, string kind)
        {
            if (parameters is null) return;

            Dictionary<string, object?> request = GetNestedObject(parameters, "request") ?? parameters;

            string? requestId = GetParamString(request, "id");
            if (requestId is null) return;
            if (_activeModalRequestId == requestId) return;
            _activeModalRequestId = requestId;

            string? title = GetParamString(request, "title")
                ?? GetParamString(request, "label")
                ?? (kind == "text" ? "Input Required" : "Permission Required");
            string? description = GetParamString(request, "description") ?? "";
            bool allowCancel = GetParamBool(request, "allowCancellation", true);

            ModalTitle.Text = title;
            ModalDescription.Text = description;
            ModalDescription.Visibility = string.IsNullOrEmpty(description) ? Visibility.Collapsed : Visibility.Visible;
            ModalBodyPanel.Children.Clear();
            ModalActionsPanel.Children.Clear();

            if (kind == "text")
            {
                string? defaultValue = GetParamString(request, "defaultValue") ?? "";
                bool isSecret = GetParamBool(request, "isSecret", false);
                Control inputControl = isSecret
                    ? new PasswordBox
                    {
                        MinHeight = 30,
                        Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
                        Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
                        Padding = new Thickness(8),
                        Margin = new Thickness(0, 0, 0, 8)
                    }
                    : new TextBox
                    {
                        AcceptsReturn = true,
                        TextWrapping = TextWrapping.Wrap,
                        Text = defaultValue,
                        MinHeight = 40, MaxHeight = 120,
                        Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
                        Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(8),
                        Margin = new Thickness(0, 0, 0, 8),
                        CaretBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                        FontSize = 13
                    };
                ModalBodyPanel.Children.Add(inputControl);

                var submitBtn = SecondaryButton("Submit");
                submitBtn.Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x63, 0x9C));
                submitBtn.Foreground = Brushes.White;
                submitBtn.BorderThickness = new Thickness(0);
                submitBtn.Click += (_, _) =>
                {
                    string value = inputControl is PasswordBox pb ? pb.Password : ((TextBox)inputControl).Text;
                    _ = ResolveClientRequestAsync(requestId, "submitted", value, null);
                };

                if (allowCancel)
                {
                    var cancelBtn = SecondaryButton("Cancel");
                    cancelBtn.Click += (_, _) => { _ = ResolveClientRequestAsync(requestId, "cancelled", null, null); };
                    ModalActionsPanel.Children.Add(cancelBtn);
                }
                ModalActionsPanel.Children.Add(submitBtn);
            }
            else
            {
                foreach (PermissionOptionInfo option in GetPermissionOptions(request))
                {
                    var optionBtn = MenuButton(option.Name, "");
                    string captured = option.OptionId;
                    optionBtn.Click += (_, _) => { _ = ResolveClientRequestAsync(requestId, "selected", null, captured); };
                    ModalBodyPanel.Children.Add(optionBtn);
                }

                if (allowCancel)
                {
                    var cancelBtn = SecondaryButton("Cancel");
                    cancelBtn.Click += (_, _) => { _ = ResolveClientRequestAsync(requestId, "cancelled", null, null); };
                    ModalActionsPanel.Children.Add(cancelBtn);
                }
            }
            ModalOverlay.Visibility = Visibility.Visible;
        }

        private async Task ResolveClientRequestAsync(string requestId, string outcome, string? value, string? optionId)
        {
            CloseModal();
            try { await _agentService.ResolveClientRequestAsync(requestId, outcome, value, optionId); }
            catch (Exception ex) { _log.Error($"Failed to resolve request: {requestId}", ex); }
        }

        // ───── File navigation ─────

        private void OnRequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            e.Handled = true;
            Uri uri = e.Uri;
            if (uri.Scheme == "nanofile")
            {
                string query = uri.Query.TrimStart('?');
                string path = "";
                int line = 0;
                foreach (string pair in query.Split('&'))
                {
                    int eq = pair.IndexOf('=');
                    if (eq < 0) continue;
                    string key = pair.Substring(0, eq);
                    string val = Uri.UnescapeDataString(pair.Substring(eq + 1));
                    if (key == "p") path = val;
                    else if (key == "l") int.TryParse(val, out line);
                }
                OpenFileInEditor(path, line);
            }
            else
            {
                try { Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
                catch (Exception ex) { _log.Warn($"Failed to open URL {uri}: {ex.Message}"); }
            }
        }

        private void OpenFileInEditor(string path, int line)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                string full = path;
                if (!Path.IsPathRooted(full) && !string.IsNullOrEmpty(_workingDirectory))
                {
                    full = Path.Combine(_workingDirectory, path);
                }
                if (!File.Exists(full)) { AddSystemMessage($"File not found: {path}"); return; }

                if (Package.GetGlobalService(typeof(DTE)) is DTE dte)
                {
                    EnvDTE.Window window = dte.ItemOperations.OpenFile(full);
                    if (line > 0 && dte.ActiveDocument?.Selection is EnvDTE.TextSelection sel) sel.GotoLine(line, false);
                    window?.Activate();
                }
            }
            catch (Exception ex) { _log.Warn($"Failed to open file {path}: {ex.Message}"); }
        }

        // ───── Status / state ─────

        private void SetStatus(string text, string? colorHex = null)
        {
            StatusText.Text = text;
            if (colorHex is not null && colorHex.Length == 9 && colorHex[0] == '#')
            {
                byte r = Convert.ToByte(colorHex.Substring(1, 2), 16);
                byte g = Convert.ToByte(colorHex.Substring(3, 2), 16);
                byte b = Convert.ToByte(colorHex.Substring(5, 2), 16);
                ((Border)StatusText.Parent).Background = new SolidColorBrush(Color.FromRgb(r, g, b));
            }
        }

        private void UpdateInputState()
        {
            bool hasText = InputTextBox.Text.Trim().Length > 0;
            // Running: square = stop (always enabled). Idle: arrow = send (needs text).
            SendButton.IsEnabled = _promptRunning || hasText;
            InputTextBox.IsEnabled = true; // keep editable so prompts can be queued while busy
            SendButton.Content = _promptRunning ? "■" : "↑";
            SendButton.Background = new SolidColorBrush(_promptRunning
                ? Color.FromRgb(0xF4, 0x87, 0x71)
                : Color.FromRgb(0x0E, 0x63, 0x9C));
        }

        private void ScrollToBottom()
        {
            EmptyStateText.Visibility = Visibility.Collapsed;
            if (MessagesScrollViewer.ViewportHeight > 0) MessagesScrollViewer.ScrollToBottom();
        }

        private async void OnSolutionOpened() => await ReResolveWorkingDirectoryAsync();

        // IVsFolderWorkspaceService.OnActiveWorkspaceChanged is an AsyncEventHandler (returns Task).
        private async Task OnActiveWorkspaceChanged(object sender, EventArgs e) => await ReResolveWorkingDirectoryAsync();

        private async Task ReResolveWorkingDirectoryAsync()
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                string resolved = ResolveWorkingDirectory();
                if (string.Equals(resolved, _workingDirectory, StringComparison.OrdinalIgnoreCase)) return;

                _workingDirectory = resolved;
                // If the host is still starting, the post-init reconcile in
                // InitializeAcpSessionAsync picks this up; no-op until then.
                await EnsureSessionWorkingDirectoryAsync();
            }
            catch (Exception ex)
            {
                _log.Error("Failed to update working directory after workspace change", ex);
            }
        }

        // Recreate the ACP session if its cwd drifted from _workingDirectory (e.g. the
        // solution opened after the session was already created with the process cwd).
        private async Task EnsureSessionWorkingDirectoryAsync()
        {
            if (!_hostInitialized || _syncingWorkingDirectory) return;
            if (string.Equals(_sessionWorkingDirectory, _workingDirectory, StringComparison.OrdinalIgnoreCase)) return;

            _syncingWorkingDirectory = true;
            try
            {
                string dir = _workingDirectory;
                await _agentService.NewSessionAsync(dir);
                _sessionWorkingDirectory = dir;
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            }
            finally { _syncingWorkingDirectory = false; }
        }

        private async Task InitializeAcpSessionAsync()
        {
            if (_hostInitialized) return;

            try
            {
                string dir = _workingDirectory;
                await _agentService.InitializeAsync(dir, _authToken);
                _sessionWorkingDirectory = dir;
                _hostInitialized = true;
                // A solution may have opened while we were starting up; reconcile now.
                await EnsureSessionWorkingDirectoryAsync();
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                SetStatus("Ready", "#FF73C991");
                await FlushPendingExternalPromptAsync();
            }
            catch (Exception ex)
            {
                _log.Error("Failed to initialize backend session", ex);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                SetStatus("Error", "#FFF48771");
                AddSystemMessage($"Failed to initialize backend session: {ex.Message}");
            }
        }

        private static string ResolveCliExecutable()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            NanoAgentOptionsPage? options = GetOptionsPageOrNull();
            string? configuredPath = options?.NanoAgentCliPath?.Trim();
            return !string.IsNullOrWhiteSpace(configuredPath) ? configuredPath! : "nanoai.exe";
        }

        private static string ResolveWorkingDirectory()
        {
            string dir = ResolveWorkingDirectoryCore();
            LogService.Instance.Info($"Resolved working directory: {dir}");
            return dir;
        }

        private static string ResolveWorkingDirectoryCore()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            NanoAgentOptionsPage? options = GetOptionsPageOrNull();
            string? configured = options?.WorkingDirectory?.Trim();
            if (!string.IsNullOrWhiteSpace(configured)) return configured!;

            // Open Folder/CMake mode → the opened folder (IVsSolution doesn't report it reliably).
            string? folder = VSPackage.OpenFolderDirectory();
            LogService.Instance.Info($"OpenFolderDirectory probe: {folder ?? "<null>"}");
            if (!string.IsNullOrWhiteSpace(folder)) return folder!;

            // real .sln → the solution folder.
            if (Package.GetGlobalService(typeof(SVsSolution)) is IVsSolution solution &&
                solution.GetSolutionInfo(out string solutionDirectory, out string solutionFile, out string _) == 0 &&
                !string.IsNullOrWhiteSpace(solutionDirectory))
            {
                LogService.Instance.Info($"GetSolutionInfo dir: {solutionDirectory}, file: {solutionFile}");
                // Opening a bare project makes VS write a throwaway .sln under %TEMP%; prefer the project dir.
                bool tempSolution = !string.IsNullOrWhiteSpace(solutionFile) &&
                    solutionDirectory.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase);
                if (tempSolution && FirstProjectDirectory() is string projDir) return projDir;
                return solutionDirectory;
            }

            string? firstProject = FirstProjectDirectory();
            LogService.Instance.Info($"FirstProjectDirectory probe: {firstProject ?? "<null>"} (falling back to process cwd if null)");
            return firstProject ?? Directory.GetCurrentDirectory();
        }

        private static string? FirstProjectDirectory()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Package.GetGlobalService(typeof(SDTE)) is not DTE dte) return null;
            foreach (Project project in dte.Solution.Projects)
            {
                try
                {
                    string? file = project.FullName; // project file path; empty for virtual/solution-folder nodes
                    if (!string.IsNullOrWhiteSpace(file) && File.Exists(file))
                        return Path.GetDirectoryName(file);
                }
                catch { /* unloaded or non-file project node */ }
            }
            return null;
        }

        private static NanoAgentOptionsPage? GetOptionsPageOrNull()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return VSPackage.Instance?.GetOptionsPage();
        }

        private void CompletePendingToolCalls()
        {
            foreach (ToolCallStatusMessage message in _toolCallMessages.Values.Where(static m => m.Status == "running"))
            {
                message.Status = "completed";
            }
        }

        // ───── Parsing helpers ─────

        // Renders the running per-file change tally as a single chat message, updated in place
        // each turn. File names are markdown links (nanofile scheme) so clicks open the editor.
        private void UpdateFileEditsSummary(Dictionary<string, object?>? parameters)
        {
            if (parameters?.TryGetValue("files", out object? value) != true ||
                value is not JsonElement files ||
                files.ValueKind != JsonValueKind.Array ||
                files.GetArrayLength() == 0)
            {
                return;
            }

            var lines = new List<string>();
            int totalAdded = 0;
            int totalRemoved = 0;
            foreach (JsonElement file in files.EnumerateArray())
            {
                string? path = TryGetJsonString(file, "displayPath");
                if (string.IsNullOrWhiteSpace(path)) continue;
                int added = file.TryGetProperty("addedLineCount", out JsonElement a) && a.TryGetInt32(out int ai) ? ai : 0;
                int removed = file.TryGetProperty("removedLineCount", out JsonElement r) && r.TryGetInt32(out int ri) ? ri : 0;
                string action = TryGetJsonString(file, "action") ?? "Edited";
                string actionColor = action switch
                {
                    "Created" => "green",
                    "Deleted" => "red",
                    _ => "yellow",
                };
                totalAdded += added;
                totalRemoved += removed;
                lines.Add($"- {{c:{actionColor}}}{action}{{/c}} · [{path}]({path}) ({{c:green}}+{added}{{/c}} {{c:red}}-{removed}{{/c}})");
            }

            if (lines.Count == 0) return;

            string text = $"**Files changed ({lines.Count})**  ·  +{totalAdded} -{totalRemoved}"
                + Environment.NewLine + Environment.NewLine
                + string.Join(Environment.NewLine, lines);

            if (_fileEditsSummaryMessage is null)
            {
                _fileEditsSummaryMessage = new SystemMessage { Text = text };
                _messages.Add(_fileEditsSummaryMessage);
            }
            else
            {
                _fileEditsSummaryMessage.Text = text;
            }
            ScrollToBottom();
        }

        private static string? FormatPlanUpdate(Dictionary<string, object?>? parameters)
        {
            if (parameters?.TryGetValue("entries", out object? value) != true || value is null)
            {
                return GetParamString(parameters, "text");
            }

            if (value is JsonElement element && element.ValueKind == JsonValueKind.Array)
            {
                var lines = new List<string>();
                foreach (JsonElement entry in element.EnumerateArray())
                {
                    string? content = TryGetJsonString(entry, "content") ?? TryGetJsonString(entry, "Content");
                    string? status = TryGetJsonString(entry, "status") ?? TryGetJsonString(entry, "Status");
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        string mark = status?.ToLowerInvariant() switch
                        {
                            "completed" => "✓",
                            "in_progress" => "▶",
                            "failed" => "✗",
                            "skipped" => "⊘",
                            _ => "○"
                        };
                        lines.Add($"{mark} {content}");
                    }
                }
                return lines.Count > 0 ? string.Join(Environment.NewLine, lines) : null;
            }

            return GetParamString(parameters, "text");
        }

        private static string? GetRawInputJson(Dictionary<string, object?>? p)
        {
            object? v = GetParamObject(p, "rawInput") ?? GetParamObject(p, "input");
            if (v is null) return null;
            if (v is JsonElement je) return je.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : je.GetRawText();
            return v.ToString();
        }

        private static List<PermissionOptionInfo> GetPermissionOptions(Dictionary<string, object?> request)
        {
            if (request.TryGetValue("options", out object? value) != true || value is null)
                return new List<PermissionOptionInfo>();

            if (value is JsonElement element && element.ValueKind == JsonValueKind.Array)
            {
                var result = new List<PermissionOptionInfo>();
                foreach (JsonElement option in element.EnumerateArray())
                {
                    if (option.ValueKind == JsonValueKind.String)
                    {
                        string optionText = option.GetString() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(optionText)) result.Add(new PermissionOptionInfo(optionText, optionText));
                    }
                    else if (option.ValueKind == JsonValueKind.Object)
                    {
                        string? optionId = TryGetJsonString(option, "optionId") ?? TryGetJsonString(option, "OptionId");
                        string? name = TryGetJsonString(option, "name") ?? TryGetJsonString(option, "Name") ?? optionId;
                        if (!string.IsNullOrWhiteSpace(optionId) && !string.IsNullOrWhiteSpace(name))
                            result.Add(new PermissionOptionInfo(optionId!, name!));
                    }
                }
                return result;
            }

            return new List<PermissionOptionInfo>();
        }

        private static string? TryGetJsonString(JsonElement element, string propertyName)
            => element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out JsonElement property) &&
               property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;

        private static Dictionary<string, object?>? GetNestedObject(Dictionary<string, object?>? p, string key)
        {
            if (p?.TryGetValue(key, out object? value) != true || value is null) return null;
            if (value is Dictionary<string, object?> dict) return dict;
            if (value is JsonElement element && element.ValueKind == JsonValueKind.Object)
                return JsonSerializer.Deserialize<Dictionary<string, object?>>(element.GetRawText());
            return null;
        }

        private static string? GetParamString(Dictionary<string, object?>? p, string key)
        {
            if (p?.TryGetValue(key, out object? v) != true || v is null) return null;
            if (v is JsonElement je) return je.ValueKind == JsonValueKind.String ? je.GetString() : (je.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : je.ToString());
            return v.ToString();
        }

        private static object? GetParamObject(Dictionary<string, object?>? p, string key) => p?.TryGetValue(key, out object? v) == true ? v : null;

        private static long? GetParamLong(Dictionary<string, object?>? p, string key)
        {
            if (p?.TryGetValue(key, out object? v) != true || v is null) return null;
            if (v is JsonElement je && je.ValueKind == JsonValueKind.Number && je.TryGetInt64(out long n)) return n;
            if (v is long l) return l;
            if (v is int i) return i;
            if (long.TryParse(v.ToString(), out long parsed)) return parsed;
            return null;
        }

        private static List<string> GetParamStringArrayList(Dictionary<string, object?>? p, string key)
        {
            var result = new List<string>();
            if (p?.TryGetValue(key, out object? v) != true || v is null) return result;
            if (v is JsonElement je && je.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in je.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String) result.Add(item.GetString()!);
            }
            return result;
        }

        private static List<(string, string?)> GetProfiles(Dictionary<string, object?>? p, string key)
        {
            var result = new List<(string, string?)>();
            if (p?.TryGetValue(key, out object? v) != true || v is null) return result;
            if (v is JsonElement je && je.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in je.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String) { result.Add((item.GetString()!, null)); }
                    else if (item.ValueKind == JsonValueKind.Object)
                    {
                        string? name = TryGetJsonString(item, "name");
                        if (!string.IsNullOrEmpty(name)) result.Add((name!, TryGetJsonString(item, "description")));
                    }
                }
            }
            return result;
        }

        private static string[] GetParamArray(Dictionary<string, object?>? p, string key)
        {
            if (p?.TryGetValue(key, out object? v) != true || v is null) return Array.Empty<string>();
            if (v is JsonElement je && je.ValueKind == JsonValueKind.Array)
            {
                var items = new List<string>();
                foreach (JsonElement item in je.EnumerateArray())
                {
                    string? text = ReadJsonContentText(item);
                    if (!string.IsNullOrWhiteSpace(text)) items.Add(text!);
                }
                return items.ToArray();
            }
            if (v is string[] arr) return arr;
            return Array.Empty<string>();
        }

        private static string? ReadJsonContentText(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String) return element.GetString();
            if (element.ValueKind != JsonValueKind.Object) return null;
            if (element.TryGetProperty("text", out JsonElement textElement) && textElement.ValueKind == JsonValueKind.String)
                return textElement.GetString();
            if (element.TryGetProperty("content", out JsonElement contentElement)) return ReadJsonContentText(contentElement);
            return null;
        }

        private static bool GetParamBool(Dictionary<string, object?>? p, string key, bool def)
        {
            if (p?.TryGetValue(key, out object? v) != true || v is null) return def;
            if (v is bool b) return b;
            if (v is JsonElement je) { if (je.ValueKind == JsonValueKind.True) return true; if (je.ValueKind == JsonValueKind.False) return false; }
            if (bool.TryParse(v.ToString(), out bool parsed)) return parsed;
            return def;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_solutionEvents != null) _solutionEvents.Opened -= OnSolutionOpened;
            if (_folderWorkspaceService != null) _folderWorkspaceService.OnActiveWorkspaceChanged -= OnActiveWorkspaceChanged;
            _turnCts?.Cancel();
            _turnCts?.Dispose();
            _agentService.HostExited -= OnHostExited;
            _agentService.HostError -= OnHostError;
            _agentService.NotificationReceived -= OnNotificationReceived;
            _agentService.Dispose();
            _processManager.Dispose();
        }

        private sealed record PermissionOptionInfo(string OptionId, string Name);
    }
}
