using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using NanoAgent.VS.Services;

namespace NanoAgent.VS.ToolWindows
{
    public sealed partial class ChatToolWindowControl : UserControl, IDisposable
    {
        private readonly LogService _log;
        private readonly VsAgentService _agentService;
        private readonly NanoAgentProcessManager _processManager;
        private readonly ObservableCollection<ChatMessage> _messages = new();
        private bool _disposed;
        private CancellationTokenSource? _turnCts;
        private bool _promptRunning;
        private bool _hostInitialized;
        private AssistantMessage? _streamingAssistant;
        private ReasoningMessage? _streamingReasoning;
        private readonly Dictionary<string, ToolCallStatusMessage> _toolCallMessages = new();
        private string? _activeModalRequestId;
        private string _workingDirectory = string.Empty;

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

            InputTextBox.TextChanged += (_, _) => UpdateInputState();
            SendButton.IsEnabled = false;

            Loaded += OnLoadedAsync;
            Unloaded += OnUnloaded;
        }

        private async void OnLoadedAsync(object sender, RoutedEventArgs e)
        {
            try { await InitializeAsync(); }
            catch (Exception ex)
            {
                _log.Error("Failed to initialize", ex);
                AddSystemMessage($"Failed to initialize: {ex.Message}");
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e) => Dispose();

        private async Task InitializeAsync()
        {
            _workingDirectory = ResolveWorkingDirectory();
            string cliExePath = ResolveCliExecutable();

            try
            {
                _agentService.Start(cliExePath);
                _log.Info("NanoAgent ACP server started.");
                SetStatus("Starting...", "#FFCCA700");
                await InitializeAcpSessionAsync();
            }
            catch (Exception ex)
            {
                _log.Error("Failed to start NanoAgent ACP server", ex);
                SetStatus("Error", "#FFF48771");
                AddSystemMessage($"Failed to start CLI '{cliExePath}': {ex.Message}");
            }
        }

        private void OnHostExited()
        {
            _log.Warn("NanoAgent ACP server exited.");
            _hostInitialized = false;
            Dispatcher.Invoke(() =>
            {
                SetStatus("Stopped", "#FF8F8F8F");
                _promptRunning = false;
                UpdateInputState();
            });
        }

        private void OnHostError(string error)
        {
            _log.Error($"NanoAgent ACP error: {error}");
            Dispatcher.Invoke(() => AddSystemMessage($"Backend error: {error}"));
        }

        private async void OnNotificationReceived(string method, Dictionary<string, object?>? parameters)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                try { await HandleNotificationAsync(method, parameters); }
                catch (Exception ex) { _log.Error($"Notification failed: {method}", ex); }
            });
        }

        private async Task HandleNotificationAsync(string method, Dictionary<string, object?>? parameters)
        {
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
                        var msg = new ToolCallStatusMessage
                        {
                            ToolCallId = GetParamString(parameters, "toolCallId") ?? Guid.NewGuid().ToString("N"),
                            Title = GetParamString(parameters, "title") ?? "Tool",
                            Kind = GetParamString(parameters, "kind") ?? "",
                            Status = "running",
                            RawInput = GetParamObject(parameters, "rawInput")?.ToString() ?? ""
                        };
                        _toolCallMessages[msg.ToolCallId] = msg;
                        _messages.Add(msg);
                        ScrollToBottom();
                        break;
                    }

                case VsProtocol.ToolCallEnd:
                    {
                        string? id = GetParamString(parameters, "toolCallId");
                        if (id is not null && _toolCallMessages.TryGetValue(id, out var msg))
                        {
                            msg.Status = GetParamString(parameters, "status") ?? "completed";
                            var content = GetParamArray(parameters, "content");
                            foreach (var line in content) msg.ContentLines.Add(line);
                            msg.OnPropertyChanged(nameof(msg.DisplayText));
                        }
                        break;
                    }

                case VsProtocol.PlanUpdate:
                    {
                        string? planText = FormatPlanUpdate(parameters);
                        if (!string.IsNullOrEmpty(planText)) { _messages.Add(new SystemMessage { Text = $"Plan: {planText}" }); ScrollToBottom(); }
                        break;
                    }

                case VsProtocol.RequestPermission:
                    ShowModalRequest(parameters, "permission");
                    break;

                case VsProtocol.RequestText:
                    ShowModalRequest(parameters, "text");
                    break;

                case VsProtocol.SessionInfo:
                    {
                        string? modelId = GetParamString(parameters, "modelId");
                        if (modelId is not null) ModelText.Text = modelId;
                        string? profile = GetParamString(parameters, "agentProfileName");
                        if (profile is not null) ProfileText.Text = profile;
                        break;
                    }

                case VsProtocol.Ready:
                    SetStatus("Ready", "#FF73C991");
                    break;
            }
        }

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

        private async void OnSendClick(object sender, RoutedEventArgs e) => await SendCurrentInput();

        private async void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
            {
                e.Handled = true;
                await SendCurrentInput();
            }
        }

        private async void OnCancelClick(object sender, RoutedEventArgs e)
        {
            _turnCts?.Cancel();
            await _agentService.CancelPromptAsync();
            _promptRunning = false;
            _streamingAssistant = null;
            _streamingReasoning = null;
            UpdateInputState();
        }

        private async Task SendCurrentInput()
        {
            string text = InputTextBox.Text.Trim();
            if (string.IsNullOrEmpty(text) || _promptRunning) return;

            InputTextBox.Clear();
            _messages.Add(new UserMessage { Text = text });
            ScrollToBottom();

            if (text.StartsWith("/")) await HandleSlashCommand(text);
            else await HandleUserPrompt(text);
        }

        private async Task HandleSlashCommand(string command)
        {
            if (command == "/clear")
            {
                _messages.Clear();
                _toolCallMessages.Clear();
                _streamingAssistant = null;
                _streamingReasoning = null;
                EmptyStateText.Visibility = Visibility.Visible;
                return;
            }

            if (command == "/new")
            {
                await StartNewSessionAsync();
                return;
            }

            if (command.StartsWith("/resume ", StringComparison.Ordinal))
            {
                await ResumeSessionAsync(command.Substring("/resume ".Length).Trim());
                return;
            }

            await SendHostCommandAsync(command);
        }

        private async Task HandleUserPrompt(string prompt)
        {
            _turnCts?.Cancel();
            _turnCts = new CancellationTokenSource();

            _promptRunning = true;
            _streamingAssistant = null;
            _streamingReasoning = null;
            _toolCallMessages.Clear();
            UpdateInputState();

            try
            {
                await _agentService.SendPromptAsync(prompt);
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
            }
        }

        private async Task SendHostCommandAsync(string command)
        {
            _promptRunning = true;
            _streamingAssistant = null;
            _streamingReasoning = null;
            UpdateInputState();

            try
            {
                await _agentService.SendPromptAsync(command);
            }
            catch (Exception ex)
            {
                _log.Error($"Command failed: {command}", ex);
                AddSystemMessage($"Error: {ex.Message}");
            }
            finally
            {
                _promptRunning = false;
                UpdateInputState();
            }
        }

        private async Task StartNewSessionAsync()
        {
            try
            {
                await _agentService.NewSessionAsync(_workingDirectory);
                _messages.Clear();
                _toolCallMessages.Clear();
                _streamingAssistant = null;
                _streamingReasoning = null;
                EmptyStateText.Visibility = Visibility.Visible;
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
                AddSystemMessage($"Resumed session {sessionId}");
                SetStatus("Ready", "#FF73C991");
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to resume ACP session: {sessionId}", ex);
                AddSystemMessage($"Error: {ex.Message}");
            }
        }

        // ───── Modal dialog for permission/text requests ─────

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
                var textBox = new TextBox
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
                ModalBodyPanel.Children.Add(textBox);

                var submitBtn = new Button
                {
                    Content = "Submit",
                    MinWidth = 70, MinHeight = 28,
                    Padding = new Thickness(12, 0, 12, 0),
                    Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x63, 0x9C)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand,
                    Margin = new Thickness(4, 0, 0, 0)
                };
                submitBtn.Click += (_, _) => { _ = ResolveClientRequestAsync(requestId, "submitted", textBox.Text, null); };

                if (allowCancel)
                {
                    var cancelBtn = new Button
                    {
                        Content = "Cancel",
                        MinWidth = 60, MinHeight = 28,
                        Padding = new Thickness(8, 0, 8, 0),
                        Background = Brushes.Transparent,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                        BorderThickness = new Thickness(1),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
                        Cursor = Cursors.Hand
                    };
                    cancelBtn.Click += (_, _) => { _ = ResolveClientRequestAsync(requestId, "cancelled", null, null); };
                    ModalActionsPanel.Children.Add(cancelBtn);
                }
                ModalActionsPanel.Children.Add(submitBtn);
            }
            else
            {
                var options = GetPermissionOptions(request);
                if (options.Count > 0)
                {
                    foreach (var option in options)
                    {
                        var optionBtn = new Button
                        {
                            Content = option.Name,
                            MinHeight = 36,
                            Padding = new Thickness(10),
                            Margin = new Thickness(0, 0, 0, 6),
                            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
                            Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
                            BorderThickness = new Thickness(1),
                            HorizontalContentAlignment = HorizontalAlignment.Left,
                            Cursor = Cursors.Hand
                        };
                        string captured = option.OptionId;
                        optionBtn.Click += (_, _) => { _ = ResolveClientRequestAsync(requestId, "selected", null, captured); };
                        ModalBodyPanel.Children.Add(optionBtn);
                    }
                }

                if (allowCancel)
                {
                    var cancelBtn = new Button
                    {
                        Content = "Cancel",
                        MinWidth = 60, MinHeight = 28,
                        Padding = new Thickness(8, 0, 8, 0),
                        Background = Brushes.Transparent,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                        BorderThickness = new Thickness(1),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
                        Cursor = Cursors.Hand
                    };
                    cancelBtn.Click += (_, _) => { _ = ResolveClientRequestAsync(requestId, "cancelled", null, null); };
                    ModalActionsPanel.Children.Add(cancelBtn);
                }
            }
            ModalOverlay.Visibility = Visibility.Visible;
        }

        private async Task ResolveClientRequestAsync(string requestId, string outcome, string? value, string? optionId)
        {
            ModalOverlay.Visibility = Visibility.Collapsed;
            _activeModalRequestId = null;

            try
            {
                await _agentService.ResolveClientRequestAsync(requestId, outcome, value, optionId);
            }
            catch (Exception ex) { _log.Error($"Failed to resolve request: {requestId}", ex); }
        }

        private void SetStatus(string text, string? colorHex = null)
        {
            StatusText.Text = text;
            if (colorHex is not null && colorHex.Length == 9 && colorHex[0] == '#')
            {
                byte r = Convert.ToByte(colorHex.Substring(1, 2), 16);
                byte g = Convert.ToByte(colorHex.Substring(3, 2), 16);
                byte b = Convert.ToByte(colorHex.Substring(5, 2), 16);
                StatusText.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
            }
        }

        private void UpdateInputState()
        {
            bool hasText = InputTextBox.Text.Trim().Length > 0;
            SendButton.IsEnabled = !_promptRunning && hasText;
            InputTextBox.IsEnabled = !_promptRunning;
            CancelButton.Visibility = _promptRunning ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ScrollToBottom()
        {
            EmptyStateText.Visibility = Visibility.Collapsed;
            if (MessagesScrollViewer.ViewportHeight > 0) MessagesScrollViewer.ScrollToBottom();
        }

        private async Task InitializeAcpSessionAsync()
        {
            if (_hostInitialized)
            {
                return;
            }

            try
            {
                await _agentService.InitializeAsync(_workingDirectory);
                _hostInitialized = true;

                await Dispatcher.InvokeAsync(() =>
                {
                    SetStatus("Ready", "#FF73C991");
                });
            }
            catch (Exception ex)
            {
                _log.Error("Failed to initialize backend session", ex);
                await Dispatcher.InvokeAsync(() =>
                {
                    SetStatus("Error", "#FFF48771");
                    AddSystemMessage($"Failed to initialize backend session: {ex.Message}");
                });
            }
        }

        private static string ResolveCliExecutable()
        {
            NanoAgentOptionsPage? options = GetOptionsPageOrNull();
            string? configuredPath = options?.NanoAgentCliPath?.Trim();
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                return configuredPath;
            }

            return "nanoai.exe";
        }

        private static string ResolveWorkingDirectory()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            NanoAgentOptionsPage? options = GetOptionsPageOrNull();
            string? configuredWorkingDirectory = options?.WorkingDirectory?.Trim();
            if (!string.IsNullOrWhiteSpace(configuredWorkingDirectory))
            {
                return configuredWorkingDirectory;
            }

            if (Package.GetGlobalService(typeof(SVsSolution)) is IVsSolution solution &&
                solution.GetSolutionInfo(out string solutionDirectory, out string _, out string _) == 0 &&
                !string.IsNullOrWhiteSpace(solutionDirectory))
            {
                return solutionDirectory;
            }

            return Directory.GetCurrentDirectory();
        }

        private static NanoAgentOptionsPage? GetOptionsPageOrNull()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return VSPackage.Instance?.GetOptionsPage();
        }

        private void CompletePendingToolCalls()
        {
            foreach (ToolCallStatusMessage message in _toolCallMessages.Values.Where(static message => message.Status == "running"))
            {
                message.Status = "completed";
                message.OnPropertyChanged(nameof(message.DisplayText));
            }
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
                    string? content = TryGetJsonString(entry, "content")
                        ?? TryGetJsonString(entry, "Content");
                    string? status = TryGetJsonString(entry, "status")
                        ?? TryGetJsonString(entry, "Status");

                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        lines.Add(string.IsNullOrWhiteSpace(status)
                            ? content!
                            : $"{status}: {content}");
                    }
                }

                return lines.Count > 0
                    ? string.Join(Environment.NewLine, lines)
                    : null;
            }

            return GetParamString(parameters, "text");
        }

        private static List<PermissionOptionInfo> GetPermissionOptions(Dictionary<string, object?> request)
        {
            if (request.TryGetValue("options", out object? value) != true || value is null)
            {
                return new List<PermissionOptionInfo>();
            }

            if (value is JsonElement element && element.ValueKind == JsonValueKind.Array)
            {
                var result = new List<PermissionOptionInfo>();
                foreach (JsonElement option in element.EnumerateArray())
                {
                    if (option.ValueKind == JsonValueKind.String)
                    {
                        string optionText = option.GetString() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(optionText))
                        {
                            result.Add(new PermissionOptionInfo(optionText, optionText));
                        }
                    }
                    else if (option.ValueKind == JsonValueKind.Object)
                    {
                        string? optionId = TryGetJsonString(option, "optionId") ?? TryGetJsonString(option, "OptionId");
                        string? name = TryGetJsonString(option, "name") ?? TryGetJsonString(option, "Name") ?? optionId;
                        if (!string.IsNullOrWhiteSpace(optionId) && !string.IsNullOrWhiteSpace(name))
                        {
                            result.Add(new PermissionOptionInfo(optionId!, name!));
                        }
                    }
                }

                return result;
            }

            return GetParamStringArray(request, "options")
                .Select(static option => new PermissionOptionInfo(option, option))
                .ToList();
        }

        private static string? TryGetJsonString(JsonElement element, string propertyName)
        {
            return element.ValueKind == JsonValueKind.Object &&
                   element.TryGetProperty(propertyName, out JsonElement property) &&
                   property.ValueKind == JsonValueKind.String
                        ? property.GetString()
                        : null;
        }

        private static Dictionary<string, object?>? GetNestedObject(Dictionary<string, object?>? p, string key)
        {
            if (p?.TryGetValue(key, out var value) != true || value is null)
            {
                return null;
            }

            if (value is Dictionary<string, object?> dict)
            {
                return dict;
            }

            if (value is JsonElement element && element.ValueKind == JsonValueKind.Object)
            {
                return JsonSerializer.Deserialize<Dictionary<string, object?>>(element.GetRawText());
            }

            return null;
        }

        private static string? GetParamString(Dictionary<string, object?>? p, string key) => p?.TryGetValue(key, out var v) == true ? v?.ToString() : null;

        private static object? GetParamObject(Dictionary<string, object?>? p, string key) => p?.TryGetValue(key, out var v) == true ? v : null;

        private static string[] GetParamArray(Dictionary<string, object?>? p, string key)
        {
            if (p?.TryGetValue(key, out var v) != true || v is null) return Array.Empty<string>();
            if (v is JsonElement je && je.ValueKind == JsonValueKind.Array)
            {
                var items = new List<string>();
                foreach (var item in je.EnumerateArray())
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
            if (element.ValueKind == JsonValueKind.String)
            {
                return element.GetString();
            }

            if (element.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (element.TryGetProperty("text", out JsonElement textElement) &&
                textElement.ValueKind == JsonValueKind.String)
            {
                return textElement.GetString();
            }

            if (element.TryGetProperty("content", out JsonElement contentElement))
            {
                return ReadJsonContentText(contentElement);
            }

            return null;
        }

        private static string[] GetParamStringArray(Dictionary<string, object?>? p, string key)
        {
            if (p?.TryGetValue(key, out var v) != true || v is null) return Array.Empty<string>();
            if (v is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.Array)
                {
                    var items = new List<string>();
                    foreach (var item in je.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String) items.Add(item.GetString()!);
                        else if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("name", out var ne) && ne.ValueKind == JsonValueKind.String) items.Add(ne.GetString()!);
                        else if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("Name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String && nameElement.GetString() is string name) items.Add(name);
                        else if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("optionId", out var oe) && oe.ValueKind == JsonValueKind.String) items.Add(oe.GetString()!);
                        else if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("OptionId", out var optionElement) && optionElement.ValueKind == JsonValueKind.String && optionElement.GetString() is string optionId) items.Add(optionId);
                    }
                    return items.ToArray();
                }
                return Array.Empty<string>();
            }
            if (v is string[] arr) return arr;
            if (v is IList ilist)
            {
                var result = new List<string>();
                foreach (var item in ilist)
                {
                    if (item is string s) result.Add(s);
                    else if (item is JsonElement j && j.ValueKind == JsonValueKind.String) result.Add(j.GetString()!);
                }
                return result.ToArray();
            }
            return Array.Empty<string>();
        }

        private static bool GetParamBool(Dictionary<string, object?>? p, string key, bool def)
        {
            if (p?.TryGetValue(key, out var v) != true || v is null) return def;
            if (v is bool b) return b;
            if (v is JsonElement je) { if (je.ValueKind == JsonValueKind.True) return true; if (je.ValueKind == JsonValueKind.False) return false; }
            if (bool.TryParse(v.ToString(), out bool parsed)) return parsed;
            return def;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
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
