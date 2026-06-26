using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NanoAgent.VS.Services
{
    /// <summary>
    /// Speaks NanoAgent ACP (JSON-RPC over stdio) with the packaged NanoAgent CLI.
    /// </summary>
    internal sealed class VsAgentService : IDisposable
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly NanoAgentProcessManager _processManager;
        private readonly LogService _log;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pendingRequests = new();
        private long _requestCounter;
        private bool _disposed;
        private string? _sessionId;
        private Task? _readLoop;
        private CancellationTokenSource? _loopCts;

        public event Action<string, Dictionary<string, object?>?>? NotificationReceived;
        public event Action<string>? HostError;
        public event Action? HostExited;

        public VsAgentService(NanoAgentProcessManager processManager)
        {
            _processManager = processManager ?? throw new ArgumentNullException(nameof(processManager));
            _log = LogService.Instance;
        }

        public bool IsConnected => _processManager.IsRunning && _readLoop is not null;

        public string? SessionId => _sessionId;

        public void Start(string executablePath, params string[] extraArgs)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(VsAgentService));
            }

            var args = new List<string> { "--acp", "--surface", "visual_studio" };
            if (extraArgs != null)
            {
                args.AddRange(extraArgs.Where(static a => !string.IsNullOrWhiteSpace(a)));
            }

            _processManager.Start(executablePath, args.ToArray());
            _loopCts = new CancellationTokenSource();
            _readLoop = Task.Run(() => ReadLoopAsync(_loopCts.Token));
        }

        public async Task InitializeAsync(string workingDirectory, string? authToken = null)
        {
            JsonElement initResult = await SendRequestAsync("initialize", new Dictionary<string, object?>
            {
                ["protocolVersion"] = 1
            });

            if (RequiresTokenAuth(initResult))
            {
                string? token = !string.IsNullOrWhiteSpace(authToken)
                    ? authToken
                    : Environment.GetEnvironmentVariable("NANOAGENT_ACP_AUTH_TOKEN");
                if (!string.IsNullOrWhiteSpace(token))
                {
                    await SendRequestAsync<JsonElement>("authenticate", new Dictionary<string, object?> { ["token"] = token });
                }
                else
                {
                    _log.Warn("ACP server requested token authentication but no token is configured.");
                }
            }

            await NewSessionAsync(workingDirectory);
            NotificationReceived?.Invoke(VsProtocol.Ready, null);
        }

        private static bool RequiresTokenAuth(JsonElement initResult)
        {
            if (initResult.ValueKind != JsonValueKind.Object ||
                !initResult.TryGetProperty("authMethods", out JsonElement methods) ||
                methods.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (JsonElement m in methods.EnumerateArray())
            {
                if (m.ValueKind == JsonValueKind.String && m.GetString() == "token") return true;
                if (m.ValueKind == JsonValueKind.Object && m.TryGetProperty("id", out JsonElement id) &&
                    id.ValueKind == JsonValueKind.String && id.GetString() == "token") return true;
            }
            return false;
        }

        public async Task NewSessionAsync(string workingDirectory)
        {
            if (!string.IsNullOrWhiteSpace(_sessionId))
            {
                await CloseSessionAsync();
            }

            SessionNewResponse? response = await SendRequestAsync<SessionNewResponse>(
                "session/new",
                new Dictionary<string, object?>
                {
                    ["cwd"] = workingDirectory
                });

            string? newSessionId = response?.SessionId;
            if (string.IsNullOrWhiteSpace(newSessionId))
            {
                throw new InvalidOperationException("NanoAgent ACP server returned an invalid session.");
            }

            _sessionId = newSessionId;
        }

        public async Task LoadSessionAsync(string workingDirectory, string sessionId)
        {
            if (!string.IsNullOrWhiteSpace(_sessionId))
            {
                await CloseSessionAsync();
            }

            await SendRequestAsync<JsonElement>(
                "session/load",
                new Dictionary<string, object?>
                {
                    ["cwd"] = workingDirectory,
                    ["sessionId"] = sessionId
                });

            _sessionId = sessionId;
        }

        public async Task CloseSessionAsync()
        {
            string? sessionId = _sessionId;
            _sessionId = null;

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }

            try
            {
                await SendRequestAsync<JsonElement>(
                    "session/close",
                    new Dictionary<string, object?>
                    {
                        ["sessionId"] = sessionId
                    });
            }
            catch (Exception ex)
            {
                _log.Warn("Failed to close ACP session.", ex);
            }
        }

        public Task<SessionPromptResponse?> SendPromptAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(_sessionId))
            {
                throw new InvalidOperationException("NanoAgent ACP session is not initialized.");
            }

            return SendRequestAsync<SessionPromptResponse>(
                "session/prompt",
                new Dictionary<string, object?>
                {
                    ["sessionId"] = _sessionId,
                    ["prompt"] = new object[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["type"] = "text",
                            ["text"] = text
                        }
                    }
                });
        }

        public async Task<List<SessionSummary>> ListSessionsAsync()
        {
            JsonElement result = await SendRequestAsync("session/list", new Dictionary<string, object?>());
            var sessions = new List<SessionSummary>();
            if (result.ValueKind == JsonValueKind.Object &&
                result.TryGetProperty("sessions", out JsonElement arr) &&
                arr.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement s in arr.EnumerateArray())
                {
                    if (s.ValueKind != JsonValueKind.Object) continue;
                    sessions.Add(new SessionSummary
                    {
                        SessionId = TryGetString(s, "sessionId") ?? string.Empty,
                        Title = TryGetString(s, "title") ?? "(untitled)",
                        ModelId = TryGetString(s, "modelId"),
                        ProfileName = TryGetString(s, "profileName"),
                        UpdatedAtUtc = TryGetString(s, "updatedAtUtc"),
                        ParentSessionId = TryGetString(s, "parentSessionId"),
                        TurnCount = s.TryGetProperty("turnCount", out JsonElement tc) && tc.TryGetInt32(out int n) ? n : (int?)null,
                    });
                }
            }
            return sessions;
        }

        public Task CancelPromptAsync()
        {
            if (string.IsNullOrWhiteSpace(_sessionId))
            {
                return Task.CompletedTask;
            }

            return SendNotificationAsync(
                "session/cancel",
                new Dictionary<string, object?>
                {
                    ["sessionId"] = _sessionId
                });
        }

        public Task ResolveClientRequestAsync(string requestId, string outcome, string? value, string? optionId)
        {
            var result = new Dictionary<string, object?>
            {
                ["outcome"] = outcome switch
                {
                    "selected" => new Dictionary<string, object?>
                    {
                        ["outcome"] = "selected",
                        ["optionId"] = optionId
                    },
                    "submitted" => new Dictionary<string, object?>
                    {
                        ["outcome"] = "submitted",
                        ["value"] = value ?? string.Empty
                    },
                    _ => new Dictionary<string, object?>
                    {
                        ["outcome"] = "cancelled"
                    }
                }
            };

            return SendResponseAsync(requestId, result);
        }

        public async Task StopAsync()
        {
            _loopCts?.Cancel();
            await CloseSessionAsync();
            await _processManager.StopAsync();
            _readLoop = null;

            foreach (KeyValuePair<string, TaskCompletionSource<JsonElement>> kvp in _pendingRequests)
            {
                kvp.Value.TrySetCanceled();
            }

            _pendingRequests.Clear();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _loopCts?.Cancel();
            _loopCts?.Dispose();
            _processManager.Dispose();
        }

        private async Task<T?> SendRequestAsync<T>(string method, Dictionary<string, object?>? parameters = null)
        {
            JsonElement result = await SendRequestAsync(method, parameters);
            if (result.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>(result.GetRawText(), SerializerOptions);
        }

        private async Task<JsonElement> SendRequestAsync(string method, Dictionary<string, object?>? parameters = null)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(VsAgentService));
            }

            if (!_processManager.IsRunning)
            {
                throw new InvalidOperationException("NanoAgent ACP server is not running.");
            }

            long id = Interlocked.Increment(ref _requestCounter);
            string requestKey = CreateRequestKey(id);
            TaskCompletionSource<JsonElement> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingRequests[requestKey] = tcs;

            await WriteMessageAsync(new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = method,
                ["params"] = parameters ?? new Dictionary<string, object?>()
            });

            Task completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromMinutes(10)));
            if (completed != tcs.Task)
            {
                _pendingRequests.TryRemove(requestKey, out _);
                throw new TimeoutException($"ACP request '{method}' timed out.");
            }

            return await tcs.Task;
        }

        private Task SendNotificationAsync(string method, Dictionary<string, object?>? parameters = null)
        {
            return WriteMessageAsync(new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method,
                ["params"] = parameters ?? new Dictionary<string, object?>()
            });
        }

        private Task SendResponseAsync(string id, object result)
        {
            return WriteMessageAsync(new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = ParseId(id),
                ["result"] = result
            });
        }

        private async Task WriteMessageAsync(Dictionary<string, object?> message)
        {
            if (!_processManager.IsRunning || _processManager.Stdin is null)
            {
                return;
            }

            string json = JsonSerializer.Serialize(message, SerializerOptions);
            await _processManager.Stdin.WriteLineAsync(json);
            await _processManager.Stdin.FlushAsync();
        }

        private async Task ReadLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    string? line;
                    try
                    {
                        line = await _processManager.Stdout!.ReadLineAsync();
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }

                    if (line is null)
                    {
                        _log.Info("NanoAgent ACP stdout stream ended.");
                        HostExited?.Invoke();
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    try
                    {
                        using JsonDocument document = JsonDocument.Parse(line);
                        HandleIncomingMessage(document.RootElement.Clone());
                    }
                    catch (JsonException ex)
                    {
                        _log.Warn($"Failed to parse ACP message: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _log.Error("ACP read loop failed.", ex);
                HostError?.Invoke(ex.Message);
            }
        }

        private void HandleIncomingMessage(JsonElement root)
        {
            if (root.TryGetProperty("id", out JsonElement idElement) &&
                (root.TryGetProperty("result", out JsonElement resultElement) ||
                 root.TryGetProperty("error", out JsonElement errorElement)))
            {
                string key = CreateRequestKey(idElement);
                if (_pendingRequests.TryRemove(key, out TaskCompletionSource<JsonElement>? tcs))
                {
                    if (root.TryGetProperty("error", out errorElement) &&
                        errorElement.ValueKind == JsonValueKind.Object)
                    {
                        string message = TryGetString(errorElement, "message") ?? "NanoAgent ACP request failed.";
                        tcs.TrySetException(new InvalidOperationException(message));
                    }
                    else
                    {
                        tcs.TrySetResult(resultElement.Clone());
                    }
                }

                return;
            }

            string? method = TryGetString(root, "method");
            if (string.IsNullOrWhiteSpace(method))
            {
                return;
            }

            if (root.TryGetProperty("id", out JsonElement requestId))
            {
                HandleClientRequest(requestId, method!, root);
                return;
            }

            if (method == "session/update" && TryGetProperty(root, "params", out JsonElement parameters))
            {
                TranslateSessionUpdate(parameters);
            }
        }

        private void HandleClientRequest(JsonElement requestId, string method, JsonElement root)
        {
            Dictionary<string, object?> parameters = new()
            {
                ["id"] = IdToString(requestId)
            };

            if (TryGetProperty(root, "params", out JsonElement requestParameters) &&
                requestParameters.ValueKind == JsonValueKind.Object)
            {
                using JsonDocument cloned = JsonDocument.Parse(requestParameters.GetRawText());
                foreach (JsonProperty property in cloned.RootElement.EnumerateObject())
                {
                    parameters[property.Name] = property.Value.Clone();
                }
            }

            switch (method)
            {
                case "session/request_permission":
                    NotificationReceived?.Invoke(VsProtocol.RequestPermission, parameters);
                    break;

                case "session/request_text":
                    NotificationReceived?.Invoke(VsProtocol.RequestText, parameters);
                    break;

                default:
                    _ = SendErrorAsync(IdToString(requestId), -32601, $"Method '{method}' is not supported.");
                    break;
            }
        }

        private async Task SendErrorAsync(string id, int code, string message)
        {
            await WriteMessageAsync(new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = ParseId(id),
                ["error"] = new Dictionary<string, object?>
                {
                    ["code"] = code,
                    ["message"] = message
                }
            });
        }

        private void TranslateSessionUpdate(JsonElement parameters)
        {
            if (!TryGetProperty(parameters, "update", out JsonElement update) ||
                update.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            string? updateKind = TryGetString(update, "sessionUpdate");
            switch (updateKind)
            {
                case "session_info_update":
                    NotificationReceived?.Invoke(VsProtocol.SessionInfo, ToDictionary(update));
                    break;

                case "agent_message_chunk":
                    NotifyTextChunk(VsProtocol.MessageChunk, update);
                    break;

                case "user_message_chunk":
                    NotifyTextChunk(VsProtocol.UserMessageChunk, update);
                    break;

                case "agent_reasoning_chunk":
                case "reasoning_message_chunk":
                case "thinking_message_chunk":
                    NotifyTextChunk(VsProtocol.ReasoningChunk, update);
                    break;

                case "tool_call":
                    NotificationReceived?.Invoke(VsProtocol.ToolCallStart, ToDictionary(update));
                    break;

                case "tool_call_update":
                    NotificationReceived?.Invoke(VsProtocol.ToolCallEnd, ToDictionary(update));
                    break;

                case "plan":
                    NotificationReceived?.Invoke(VsProtocol.PlanUpdate, ToDictionary(update));
                    break;

                case "file_edits_summary":
                    NotificationReceived?.Invoke(VsProtocol.FileEditsSummary, ToDictionary(update));
                    break;
            }
        }

        private void NotifyTextChunk(string method, JsonElement update)
        {
            string? text = ReadContentText(update.TryGetProperty("content", out JsonElement content)
                ? content
                : default);

            if (!string.IsNullOrEmpty(text))
            {
                NotificationReceived?.Invoke(method, new Dictionary<string, object?>
                {
                    ["text"] = text
                });
            }
        }

        private static Dictionary<string, object?> ToDictionary(JsonElement element)
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(element.GetRawText(), SerializerOptions)
                ?? new Dictionary<string, object?>();
        }

        private static string? ReadContentText(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                return element.GetString();
            }

            if (element.ValueKind == JsonValueKind.Array)
            {
                List<string> lines = new();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    string? text = ReadContentText(item);
                    if (!string.IsNullOrEmpty(text))
                    {
                        lines.Add(text!);
                    }
                }

                return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
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
                return ReadContentText(contentElement);
            }

            return null;
        }

        private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement property)
        {
            if (element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(propertyName, out property))
            {
                return true;
            }

            property = default;
            return false;
        }

        private static string? TryGetString(JsonElement element, string propertyName)
        {
            return element.ValueKind == JsonValueKind.Object &&
                   element.TryGetProperty(propertyName, out JsonElement property) &&
                   property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
        }

        private static string CreateRequestKey(long id) => "n:" + id.ToString();

        private static string CreateRequestKey(JsonElement id)
        {
            return id.ValueKind switch
            {
                JsonValueKind.Number when id.TryGetInt64(out long number) => CreateRequestKey(number),
                JsonValueKind.String => "s:" + (id.GetString() ?? string.Empty),
                JsonValueKind.Null => "null",
                _ => id.GetRawText()
            };
        }

        private static string IdToString(JsonElement id)
        {
            return id.ValueKind switch
            {
                JsonValueKind.Number when id.TryGetInt64(out long number) => number.ToString(),
                JsonValueKind.String => id.GetString() ?? string.Empty,
                _ => id.GetRawText()
            };
        }

        private static object ParseId(string id)
        {
            return long.TryParse(id, out long numeric)
                ? numeric
                : id;
        }

        internal sealed class SessionPromptResponse
        {
            public string? StopReason { get; set; }
            public TurnMetrics? Metrics { get; set; }
        }

        internal sealed class TurnMetrics
        {
            // double? — the CLI emits these as fractional numbers (e.g. 66.7ms), which won't parse as long/int.
            public double? ElapsedMilliseconds { get; set; }
            public double? EstimatedOutputTokens { get; set; }
            public double? EstimatedTotalTokens { get; set; }
            public double? ToolRoundCount { get; set; }
            public double? ProviderRetryCount { get; set; }
        }

        internal sealed class SessionSummary
        {
            public string SessionId { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string? ModelId { get; set; }
            public string? ProfileName { get; set; }
            public string? UpdatedAtUtc { get; set; }
            public string? ParentSessionId { get; set; }
            public int? TurnCount { get; set; }
        }

        private sealed class SessionNewResponse
        {
            public string? SessionId { get; set; }
        }
    }
}
