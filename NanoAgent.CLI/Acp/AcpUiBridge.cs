using NanoAgent.Application.Formatting;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Application.UI;

using System.Text.Json;
namespace NanoAgent.CLI;

internal sealed class AcpUiBridge : IUiBridge
{
    private readonly AcpServer _server;
    private readonly TextWriter _error;
    private readonly object _providerAuthKeySync = new();
    private readonly object _tailSync = new();
    private readonly IToolOutputFormatter _toolOutputFormatter = new ToolOutputFormatter();
    private readonly TimeSpan _requestTimeout;
    private int _assistantMessageChunkCount;
    private string? _providerAuthKey;
    private bool _providerAuthKeyConsumed;
    private Task _tail = Task.CompletedTask;

    public AcpUiBridge(
        AcpServer server,
        TextWriter error,
        string? providerAuthKey,
        TimeSpan requestTimeout)
    {
        _server = server;
        _error = error;
        _providerAuthKey = NormalizeOrNull(providerAuthKey);
        _requestTimeout = requestTimeout;
    }

    public string? SessionId { get; set; }

    public Task<T> RequestSelectionAsync<T>(
        SelectionPromptRequest<T> request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Options.Count == 0)
        {
            throw new PromptCancelledException("No prompt options were available.");
        }

        return RequestSelectionViaAcpAsync(request, cancellationToken);
    }

    public Task<string> RequestTextAsync(
        TextPromptRequest request,
        bool isSecret,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (TryConsumeProviderAuthKey(request, isSecret, out string providerAuthKey))
        {
            return Task.FromResult(providerAuthKey);
        }

        return RequestTextViaAcpAsync(request, isSecret, cancellationToken);
    }

    public void ShowError(string message)
    {
        EnqueueSessionText($"Error: {message}");
    }

    public void ShowInfo(string message)
    {
        EnqueueSessionText(message);
    }

    public void ShowSuccess(string message)
    {
        EnqueueSessionText($"Success: {message}");
    }

    public void ShowAssistantMessageChunk(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        Interlocked.Increment(ref _assistantMessageChunkCount);

        string sessionId = SessionId ?? string.Empty;
        EnqueueNotification(token => _server.SendAgentMessageChunkAsync(sessionId, text, token));
    }

    public void ShowAssistantReasoning(string reasoningText)
    {
        if (string.IsNullOrWhiteSpace(reasoningText))
        {
            return;
        }

        string? sessionId = SessionId;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        EnqueueNotification(token => _server.SendThinkingAsync(
            sessionId,
            reasoningText,
            token));
    }

    public void ShowToolCalls(IReadOnlyList<ConversationToolCall> toolCalls)
    {
        ArgumentNullException.ThrowIfNull(toolCalls);

        foreach (ConversationToolCall toolCall in toolCalls)
        {
            string? sessionId = SessionId;
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                continue;
            }

            string title = _toolOutputFormatter.DescribeCall(toolCall);
            if (string.IsNullOrWhiteSpace(title))
            {
                title = toolCall.Name;
            }

            string kind = GetToolKind(toolCall.Name);
            EnqueueNotification(token => _server.SendToolCallAsync(
                sessionId,
                toolCall,
                title,
                kind,
                token));
        }
    }

    public void ShowToolResults(ToolExecutionBatchResult toolExecutionResult)
    {
        ArgumentNullException.ThrowIfNull(toolExecutionResult);

        foreach (ToolInvocationResult result in toolExecutionResult.Results)
        {
            string? sessionId = SessionId;
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                continue;
            }

            IReadOnlyList<string> formattedMessages = _toolOutputFormatter.FormatResults(
                new ToolExecutionBatchResult([result]));
            string content = formattedMessages.Count == 0
                ? result.ToDisplayText()
                : string.Join(
                    Environment.NewLine + Environment.NewLine,
                    formattedMessages);

            EnqueueNotification(token => _server.SendToolCallUpdateAsync(
                sessionId,
                result.ToolCallId,
                result.Result.IsSuccess,
                content,
                token));
        }
    }

    public void ShowExecutionPlan(ExecutionPlanProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        string? sessionId = SessionId;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        EnqueueNotification(token => _server.SendPlanAsync(sessionId, progress, token));
    }

    public Task FlushAsync()
    {
        lock (_tailSync)
        {
            return _tail;
        }
    }

    public bool HasObservedAssistantMessageChunks()
    {
        return Volatile.Read(ref _assistantMessageChunkCount) > 0;
    }

    public void ResetAssistantMessageChunkTracking()
    {
        Interlocked.Exchange(ref _assistantMessageChunkCount, 0);
    }

    private async Task<T> RequestSelectionViaAcpAsync<T>(
        SelectionPromptRequest<T> request,
        CancellationToken cancellationToken)
    {
        string sessionId = SessionId ?? string.Empty;
        string toolCallId = "prompt-" + Guid.NewGuid().ToString("N");
        int defaultIndex = Math.Clamp(request.DefaultIndex, 0, request.Options.Count - 1);

        JsonElement result = await _server.SendClientRequestAsync(
            "session/request_permission",
            writer =>
            {
                writer.WriteStartObject();
                writer.WriteString("sessionId", sessionId);
                writer.WriteBoolean("allowCancellation", request.AllowCancellation);
                writer.WriteString("defaultOptionId", defaultIndex.ToString());
                if (request.AutoSelectAfter is { } autoSelectAfter &&
                    autoSelectAfter > TimeSpan.Zero)
                {
                    writer.WriteNumber(
                        "autoSelectAfterMilliseconds",
                        (long)Math.Ceiling(autoSelectAfter.TotalMilliseconds));
                }

                writer.WritePropertyName("toolCall");
                writer.WriteStartObject();
                writer.WriteString("toolCallId", toolCallId);
                writer.WriteString("title", request.Title);
                writer.WriteString("kind", "other");
                writer.WriteString("status", "pending");
                if (!string.IsNullOrWhiteSpace(request.Description))
                {
                    writer.WritePropertyName("content");
                    writer.WriteStartArray();
                    AcpServer.WriteToolTextContent(writer, request.Description);
                    writer.WriteEndArray();
                }

                writer.WriteEndObject();
                writer.WritePropertyName("options");
                writer.WriteStartArray();

                for (int index = 0; index < request.Options.Count; index++)
                {
                    SelectionPromptOption<T> option = request.Options[index];
                    writer.WriteStartObject();
                    writer.WriteString("optionId", index.ToString());
                    writer.WriteString("name", option.Label);
                    writer.WriteString("kind", GetPermissionOptionKind(option.Label, option.Value));
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            },
            _requestTimeout,
            cancellationToken);

        if (!TryGetProperty(result, "outcome", out JsonElement outcome) ||
            !TryGetString(outcome, "outcome", out string outcomeKind))
        {
            throw new PromptCancelledException("ACP client returned an invalid permission response.");
        }

        if (string.Equals(outcomeKind, "cancelled", StringComparison.Ordinal))
        {
            throw new PromptCancelledException("The ACP client cancelled the prompt.");
        }

        if (!string.Equals(outcomeKind, "selected", StringComparison.Ordinal) ||
            !TryGetString(outcome, "optionId", out string optionId) ||
            !int.TryParse(optionId, out int optionIndex) ||
            optionIndex < 0 ||
            optionIndex >= request.Options.Count)
        {
            throw new PromptCancelledException("ACP client returned an unknown prompt option.");
        }

        return request.Options[optionIndex].Value;
    }

    private async Task<string> RequestTextViaAcpAsync(
        TextPromptRequest request,
        bool isSecret,
        CancellationToken cancellationToken)
    {
        string sessionId = SessionId ?? string.Empty;

        try
        {
            JsonElement result = await _server.SendClientRequestAsync(
                "session/request_text",
                writer =>
                {
                    writer.WriteStartObject();
                    writer.WriteString("sessionId", sessionId);
                    writer.WriteString("label", request.Label);
                    if (!string.IsNullOrWhiteSpace(request.Description))
                    {
                        writer.WriteString("description", request.Description);
                    }

                    if (!string.IsNullOrWhiteSpace(request.DefaultValue))
                    {
                        writer.WriteString("defaultValue", request.DefaultValue);
                    }

                    writer.WriteBoolean("isSecret", isSecret);
                    writer.WriteBoolean("allowCancellation", request.AllowCancellation);
                    writer.WriteEndObject();
                },
                _requestTimeout,
                cancellationToken);

            if (!TryGetProperty(result, "outcome", out JsonElement outcome) ||
                !TryGetString(outcome, "outcome", out string outcomeKind))
            {
                throw new PromptCancelledException("ACP client returned an invalid text prompt response.");
            }

            if (string.Equals(outcomeKind, "cancelled", StringComparison.Ordinal))
            {
                throw new PromptCancelledException("The ACP client cancelled the text prompt.");
            }

            if (string.Equals(outcomeKind, "submitted", StringComparison.Ordinal) &&
                TryGetString(outcome, "value", out string value))
            {
                return value;
            }

            throw new PromptCancelledException("ACP client returned an unknown text prompt response.");
        }
        catch (AcpRemoteException exception) when (exception.Code == -32601)
        {
            throw new PromptCancelledException(
                $"Prompt '{request.Label}' requires text input, but the ACP client does not support session/request_text.");
        }
    }

    private void EnqueueSessionText(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        string sessionId = SessionId ?? string.Empty;
        EnqueueNotification(token => _server.SendAgentMessageChunkAsync(sessionId, message.Trim(), token));
    }

    private void EnqueueNotification(Func<CancellationToken, Task> send)
    {
        lock (_tailSync)
        {
            _tail = _tail
                .ContinueWith(
                    async previous =>
                    {
                        if (previous.Exception is not null)
                        {
                            _error.WriteLine(previous.Exception.GetBaseException().Message);
                        }

                        await send(CancellationToken.None);
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default)
                .Unwrap();
        }
    }

    private bool TryConsumeProviderAuthKey(
        TextPromptRequest request,
        bool isSecret,
        out string providerAuthKey)
    {
        providerAuthKey = string.Empty;
        if (!isSecret || !IsProviderAuthKeyPrompt(request))
        {
            return false;
        }

        lock (_providerAuthKeySync)
        {
            if (_providerAuthKeyConsumed || string.IsNullOrWhiteSpace(_providerAuthKey))
            {
                return false;
            }

            providerAuthKey = _providerAuthKey;
            _providerAuthKeyConsumed = true;
            _providerAuthKey = null;
            return true;
        }
    }

    private static string GetToolKind(string toolName)
    {
        return toolName switch
        {
            "file_read" or "directory_list" => "read",
            "file_write" or "apply_patch" => "edit",
            "file_delete" => "delete",
            "search_files" or "text_search" => "search",
            "shell_command" => "execute",
            "update_plan" => "think",
            "web_search" or "headless_browser" => "fetch",
            _ => "other"
        };
    }

    private static string GetPermissionOptionKind<T>(string label, T value)
    {
        string normalizedLabel = label.Trim().ToLowerInvariant();
        if (normalizedLabel.Contains("allow once", StringComparison.Ordinal))
        {
            return "allow_once";
        }

        if (normalizedLabel.Contains("allow", StringComparison.Ordinal))
        {
            return "allow_always";
        }

        if (normalizedLabel.Contains("deny once", StringComparison.Ordinal) ||
            normalizedLabel.Contains("reject once", StringComparison.Ordinal))
        {
            return "reject_once";
        }

        if (normalizedLabel.Contains("deny", StringComparison.Ordinal) ||
            normalizedLabel.Contains("reject", StringComparison.Ordinal))
        {
            return "reject_always";
        }

        if (value is bool boolValue)
        {
            return boolValue ? "allow_once" : "reject_once";
        }

        return "allow_once";
    }

    private static bool IsProviderAuthKeyPrompt(TextPromptRequest request)
    {
        string label = request.Label.Trim();
        return string.Equals(label, "API key", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(label, "Provider auth key", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement property)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            property = default;
            return false;
        }

        return element.TryGetProperty(propertyName, out property);
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!TryGetProperty(element, propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return true;
    }
}
