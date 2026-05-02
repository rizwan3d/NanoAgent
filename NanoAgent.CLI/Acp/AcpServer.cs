using NanoAgent.Application.Backend;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Formatting;
using NanoAgent.Application.Models;
using NanoAgent.Application.UI;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace NanoAgent.CLI;

internal sealed class AcpServer : IAsyncDisposable
{
    private const int JsonRpcParseError = -32700;
    private const int JsonRpcInvalidRequest = -32600;
    private const int JsonRpcMethodNotFound = -32601;
    private const int JsonRpcInvalidParams = -32602;
    private const int JsonRpcInternalError = -32603;
    private const int ProtocolVersion = 1;

    private readonly string[] _backendArgs;
    private readonly Func<string[], INanoAgentBackend> _backendFactory;
    private readonly TextWriter _error;
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pendingRequests = new(StringComparer.Ordinal);
    private readonly Channel<JsonElement> _requestQueue = Channel.CreateUnbounded<JsonElement>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });
    private readonly SemaphoreSlim _outputLock = new(1, 1);
    private readonly string? _providerAuthKey;
    private readonly string _startupDirectory;
    private AcpSession? _session;
    private long _nextRequestId;

    public AcpServer(
        TextReader input,
        TextWriter output,
        TextWriter error,
        string[] backendArgs,
        string? providerAuthKey)
        : this(
            input,
            output,
            error,
            backendArgs,
            providerAuthKey,
            static args => new NanoAgentBackend(args))
    {
    }

    internal AcpServer(
        TextReader input,
        TextWriter output,
        TextWriter error,
        string[] backendArgs,
        string? providerAuthKey,
        Func<string[], INanoAgentBackend> backendFactory)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _error = error ?? throw new ArgumentNullException(nameof(error));
        _backendArgs = backendArgs ?? throw new ArgumentNullException(nameof(backendArgs));
        _providerAuthKey = NormalizeOrNull(providerAuthKey);
        _backendFactory = backendFactory ?? throw new ArgumentNullException(nameof(backendFactory));
        _startupDirectory = Directory.GetCurrentDirectory();
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Task processor = ProcessRequestQueueAsync(cancellationToken);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line = await _input.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                await ProcessIncomingLineAsync(line, cancellationToken);
            }
        }
        finally
        {
            _requestQueue.Writer.TryComplete();
        }

        await processor;
    }

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            await CloseSessionAsync(_session, CancellationToken.None);
        }

        _outputLock.Dispose();
    }

    private async Task ProcessIncomingLineAsync(string line, CancellationToken cancellationToken)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException exception)
        {
            await SendErrorAsync(
                id: null,
                JsonRpcParseError,
                $"Invalid JSON-RPC message: {exception.Message}",
                cancellationToken);
            return;
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                await SendErrorAsync(
                    id: null,
                    JsonRpcInvalidRequest,
                    "JSON-RPC message must be an object.",
                    cancellationToken);
                return;
            }

            if (IsResponse(root))
            {
                RouteResponse(root);
                return;
            }

            if (!TryGetString(root, "method", out string method))
            {
                JsonElement? id = TryGetRequestId(root, out JsonElement requestId)
                    ? requestId
                    : null;

                await SendErrorAsync(
                    id,
                    JsonRpcInvalidRequest,
                    "JSON-RPC request is missing a method.",
                    cancellationToken);
                return;
            }

            if (string.Equals(method, "session/cancel", StringComparison.Ordinal))
            {
                await HandleCancelNotificationAsync(root, cancellationToken);
                return;
            }

            await _requestQueue.Writer.WriteAsync(root.Clone(), cancellationToken);
        }
    }

    private async Task ProcessRequestQueueAsync(CancellationToken cancellationToken)
    {
        await foreach (JsonElement root in _requestQueue.Reader.ReadAllAsync(cancellationToken))
        {
            await ProcessRequestAsync(root, cancellationToken);
        }
    }

    private async Task ProcessRequestAsync(JsonElement root, CancellationToken cancellationToken)
    {
        bool hasId = TryGetRequestId(root, out JsonElement id);
        JsonElement? responseId = hasId ? id : null;

        try
        {
            if (!TryGetString(root, "method", out string method))
            {
                throw new AcpProtocolException(JsonRpcInvalidRequest, "JSON-RPC request is missing a method.");
            }

            switch (method)
            {
                case "initialize":
                    await HandleInitializeAsync(responseId, cancellationToken);
                    break;

                case "authenticate":
                    await HandleAuthenticateAsync(responseId, cancellationToken);
                    break;

                case "session/new":
                    await HandleSessionNewAsync(root, responseId, cancellationToken);
                    break;

                case "session/load":
                    await HandleSessionLoadAsync(root, responseId, cancellationToken);
                    break;

                case "session/prompt":
                    await HandleSessionPromptAsync(root, responseId, cancellationToken);
                    break;

                case "session/close":
                    await HandleSessionCloseAsync(root, responseId, cancellationToken);
                    break;

                default:
                    throw new AcpProtocolException(JsonRpcMethodNotFound, $"Method '{method}' is not supported.");
            }
        }
        catch (AcpProtocolException exception)
        {
            if (hasId)
            {
                await SendErrorAsync(responseId, exception.Code, exception.Message, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (hasId)
            {
                await SendErrorAsync(responseId, JsonRpcInternalError, exception.Message, cancellationToken);
            }
            else
            {
                _error.WriteLine($"NanoAgent ACP request error: {exception.Message}");
            }
        }
    }

    private async Task HandleInitializeAsync(JsonElement? id, CancellationToken cancellationToken)
    {
        await SendResultAsync(
            id,
            writer =>
            {
                writer.WriteStartObject();
                writer.WriteNumber("protocolVersion", ProtocolVersion);
                writer.WritePropertyName("agentCapabilities");
                writer.WriteStartObject();
                writer.WriteBoolean("loadSession", true);
                writer.WritePropertyName("promptCapabilities");
                writer.WriteStartObject();
                writer.WriteBoolean("image", true);
                writer.WriteBoolean("audio", false);
                writer.WriteBoolean("embeddedContext", true);
                writer.WriteEndObject();
                writer.WritePropertyName("mcpCapabilities");
                writer.WriteStartObject();
                writer.WriteBoolean("http", false);
                writer.WriteBoolean("sse", false);
                writer.WriteEndObject();
                writer.WritePropertyName("sessionCapabilities");
                writer.WriteStartObject();
                writer.WritePropertyName("close");
                writer.WriteStartObject();
                writer.WriteEndObject();
                writer.WriteEndObject();
                writer.WriteEndObject();
                writer.WritePropertyName("agentInfo");
                writer.WriteStartObject();
                writer.WriteString("name", "nanoagent");
                writer.WriteString("title", "NanoAgent");
                writer.WriteString("version", GetVersion());
                writer.WriteEndObject();
                writer.WritePropertyName("authMethods");
                writer.WriteStartArray();
                writer.WriteEndArray();
                writer.WriteEndObject();
            },
            cancellationToken);
    }

    private async Task HandleAuthenticateAsync(JsonElement? id, CancellationToken cancellationToken)
    {
        await SendResultAsync(
            id,
            writer =>
            {
                writer.WriteStartObject();
                writer.WriteEndObject();
            },
            cancellationToken);
    }

    private async Task HandleSessionNewAsync(
        JsonElement root,
        JsonElement? id,
        CancellationToken cancellationToken)
    {
        JsonElement parameters = GetRequiredParams(root);
        string cwd = ReadRequiredAbsolutePath(parameters, "cwd");
        LogUnsupportedMcpServers(parameters);

        if (_session is not null)
        {
            throw new AcpProtocolException(
                JsonRpcInvalidRequest,
                "NanoAgent ACP currently supports one active session per process.");
        }

        AcpSession session = await CreateSessionAsync(
            cwd,
            sectionId: null,
            replayHistory: false,
            cancellationToken);

        _session = session;

        await SendResultAsync(
            id,
            writer =>
            {
                writer.WriteStartObject();
                writer.WriteString("sessionId", session.SessionId);
                writer.WriteEndObject();
            },
            cancellationToken);

        await SendSessionInfoUpdateAsync(session, cancellationToken);
    }

    private async Task HandleSessionLoadAsync(
        JsonElement root,
        JsonElement? id,
        CancellationToken cancellationToken)
    {
        JsonElement parameters = GetRequiredParams(root);
        string cwd = ReadRequiredAbsolutePath(parameters, "cwd");
        string sessionId = ReadRequiredString(parameters, "sessionId");
        LogUnsupportedMcpServers(parameters);

        if (_session is not null)
        {
            throw new AcpProtocolException(
                JsonRpcInvalidRequest,
                "NanoAgent ACP currently supports one active session per process.");
        }

        AcpSession session = await CreateSessionAsync(
            cwd,
            sessionId,
            replayHistory: true,
            cancellationToken);

        _session = session;

        await SendResultAsync(
            id,
            static writer => writer.WriteNullValue(),
            cancellationToken);

        await SendSessionInfoUpdateAsync(session, cancellationToken);
    }

    private async Task HandleSessionPromptAsync(
        JsonElement root,
        JsonElement? id,
        CancellationToken cancellationToken)
    {
        JsonElement parameters = GetRequiredParams(root);
        string sessionId = ReadRequiredString(parameters, "sessionId");
        AcpSession session = GetSession(sessionId);
        AcpPrompt prompt = ReadPrompt(parameters);

        await session.TurnLock.WaitAsync(cancellationToken);
        using CancellationTokenSource promptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        session.ActivePromptCancellation = promptCancellation;

        try
        {
            Directory.SetCurrentDirectory(session.WorkingDirectory);

            ConversationTurnResult result = await session.Backend.RunTurnAsync(
                prompt.Text,
                prompt.Attachments,
                session.Bridge,
                promptCancellation.Token);

            await session.Bridge.FlushAsync();

            if (!string.IsNullOrWhiteSpace(result.ResponseText))
            {
                await SendAgentMessageChunkAsync(session.SessionId, result.ResponseText, cancellationToken);
            }

            await SendResultAsync(
                id,
                static writer =>
                {
                    writer.WriteStartObject();
                    writer.WriteString("stopReason", "end_turn");
                    writer.WriteEndObject();
                },
                cancellationToken);
        }
        catch (OperationCanceledException) when (promptCancellation.IsCancellationRequested)
        {
            await session.Bridge.FlushAsync();
            await SendResultAsync(
                id,
                static writer =>
                {
                    writer.WriteStartObject();
                    writer.WriteString("stopReason", "cancelled");
                    writer.WriteEndObject();
                },
                CancellationToken.None);
        }
        catch (PromptCancelledException)
        {
            await session.Bridge.FlushAsync();
            await SendResultAsync(
                id,
                static writer =>
                {
                    writer.WriteStartObject();
                    writer.WriteString("stopReason", "cancelled");
                    writer.WriteEndObject();
                },
                CancellationToken.None);
        }
        finally
        {
            session.ActivePromptCancellation = null;
            session.TurnLock.Release();
        }
    }

    private async Task HandleSessionCloseAsync(
        JsonElement root,
        JsonElement? id,
        CancellationToken cancellationToken)
    {
        JsonElement parameters = GetRequiredParams(root);
        string sessionId = ReadRequiredString(parameters, "sessionId");
        AcpSession session = GetSession(sessionId);

        await CloseSessionAsync(session, cancellationToken);
        _session = null;

        await SendResultAsync(
            id,
            writer =>
            {
                writer.WriteStartObject();
                writer.WriteEndObject();
            },
            cancellationToken);
    }

    private Task HandleCancelNotificationAsync(JsonElement root, CancellationToken cancellationToken)
    {
        if (!TryGetProperty(root, "params", out JsonElement parameters) ||
            !TryGetString(parameters, "sessionId", out string sessionId))
        {
            return Task.CompletedTask;
        }

        if (_session is not null &&
            string.Equals(_session.SessionId, sessionId, StringComparison.Ordinal))
        {
            _session.ActivePromptCancellation?.Cancel();
        }

        return Task.CompletedTask;
    }

    private async Task<AcpSession> CreateSessionAsync(
        string cwd,
        string? sectionId,
        bool replayHistory,
        CancellationToken cancellationToken)
    {
        Directory.SetCurrentDirectory(cwd);

        string[] backendArgs = CreateBackendArgs(sectionId);
        INanoAgentBackend backend = _backendFactory(backendArgs);
        AcpUiBridge bridge = new(this, _error, _providerAuthKey);

        try
        {
            BackendSessionInfo sessionInfo = await backend.InitializeAsync(bridge, cancellationToken);
            bridge.SessionId = sessionInfo.SessionId;

            AcpSession session = new(
                sessionInfo.SessionId,
                cwd,
                backend,
                bridge);

            if (replayHistory)
            {
                await ReplayConversationHistoryAsync(sessionInfo, cancellationToken);
            }

            return session;
        }
        catch
        {
            await backend.DisposeAsync();
            throw;
        }
    }

    private async Task ReplayConversationHistoryAsync(
        BackendSessionInfo sessionInfo,
        CancellationToken cancellationToken)
    {
        foreach (BackendConversationMessage message in sessionInfo.ConversationHistory)
        {
            if (string.IsNullOrWhiteSpace(message.Content))
            {
                continue;
            }

            string updateKind = string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase)
                ? "user_message_chunk"
                : "agent_message_chunk";

            await SendMessageChunkAsync(
                sessionInfo.SessionId,
                updateKind,
                message.Content,
                cancellationToken);
        }
    }

    private async Task CloseSessionAsync(AcpSession session, CancellationToken cancellationToken)
    {
        session.ActivePromptCancellation?.Cancel();
        await session.Backend.DisposeAsync();

        try
        {
            Directory.SetCurrentDirectory(_startupDirectory);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private async Task SendSessionInfoUpdateAsync(AcpSession session, CancellationToken cancellationToken)
    {
        await SendNotificationAsync(
            "session/update",
            writer =>
            {
                writer.WriteStartObject();
                writer.WriteString("sessionId", session.SessionId);
                writer.WritePropertyName("update");
                writer.WriteStartObject();
                writer.WriteString("sessionUpdate", "session_info_update");
                writer.WriteString("updatedAt", DateTimeOffset.UtcNow.ToString("O"));
                writer.WriteEndObject();
                writer.WriteEndObject();
            },
            cancellationToken);
    }

    internal async Task<JsonElement> SendClientRequestAsync(
        string method,
        Action<Utf8JsonWriter> writeParams,
        CancellationToken cancellationToken)
    {
        long id = Interlocked.Increment(ref _nextRequestId);
        string key = CreateIdKey(id);
        TaskCompletionSource<JsonElement> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_pendingRequests.TryAdd(key, completion))
        {
            throw new InvalidOperationException("Duplicate ACP request id.");
        }

        using CancellationTokenRegistration registration = cancellationToken.Register(() =>
        {
            if (_pendingRequests.TryRemove(key, out TaskCompletionSource<JsonElement>? pending))
            {
                pending.TrySetCanceled(cancellationToken);
            }
        });

        try
        {
            await WriteMessageAsync(
                writer =>
                {
                    writer.WriteStartObject();
                    writer.WriteString("jsonrpc", "2.0");
                    writer.WriteNumber("id", id);
                    writer.WriteString("method", method);
                    writer.WritePropertyName("params");
                    writeParams(writer);
                    writer.WriteEndObject();
                },
                cancellationToken);

            return await completion.Task;
        }
        catch
        {
            _pendingRequests.TryRemove(key, out _);
            throw;
        }
    }

    internal Task SendToolCallAsync(
        string sessionId,
        ConversationToolCall toolCall,
        string title,
        string kind,
        CancellationToken cancellationToken)
    {
        return SendNotificationAsync(
            "session/update",
            writer =>
            {
                writer.WriteStartObject();
                writer.WriteString("sessionId", sessionId);
                writer.WritePropertyName("update");
                writer.WriteStartObject();
                writer.WriteString("sessionUpdate", "tool_call");
                writer.WriteString("toolCallId", toolCall.Id);
                writer.WriteString("title", title);
                writer.WriteString("kind", kind);
                writer.WriteString("status", "pending");
                WriteJsonObjectOrString(writer, "rawInput", toolCall.ArgumentsJson);
                writer.WriteEndObject();
                writer.WriteEndObject();
            },
            cancellationToken);
    }

    internal Task SendToolCallUpdateAsync(
        string sessionId,
        string toolCallId,
        bool success,
        string content,
        CancellationToken cancellationToken)
    {
        return SendNotificationAsync(
            "session/update",
            writer =>
            {
                writer.WriteStartObject();
                writer.WriteString("sessionId", sessionId);
                writer.WritePropertyName("update");
                writer.WriteStartObject();
                writer.WriteString("sessionUpdate", "tool_call_update");
                writer.WriteString("toolCallId", toolCallId);
                writer.WriteString("status", success ? "completed" : "failed");
                writer.WritePropertyName("content");
                writer.WriteStartArray();
                WriteToolTextContent(writer, content);
                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.WriteEndObject();
            },
            cancellationToken);
    }

    internal Task SendPlanAsync(
        string sessionId,
        ExecutionPlanProgress progress,
        CancellationToken cancellationToken)
    {
        return SendNotificationAsync(
            "session/update",
            writer =>
            {
                writer.WriteStartObject();
                writer.WriteString("sessionId", sessionId);
                writer.WritePropertyName("update");
                writer.WriteStartObject();
                writer.WriteString("sessionUpdate", "plan");
                writer.WritePropertyName("entries");
                writer.WriteStartArray();

                for (int index = 0; index < progress.Tasks.Count; index++)
                {
                    string status = index < progress.CompletedTaskCount
                        ? "completed"
                        : index == progress.CompletedTaskCount
                            ? "in_progress"
                            : "pending";

                    writer.WriteStartObject();
                    writer.WriteString("content", progress.Tasks[index]);
                    writer.WriteString("priority", "medium");
                    writer.WriteString("status", status);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.WriteEndObject();
            },
            cancellationToken);
    }

    internal Task SendAgentMessageChunkAsync(
        string sessionId,
        string text,
        CancellationToken cancellationToken)
    {
        return SendMessageChunkAsync(
            sessionId,
            "agent_message_chunk",
            text,
            cancellationToken);
    }

    private Task SendMessageChunkAsync(
        string sessionId,
        string updateKind,
        string text,
        CancellationToken cancellationToken)
    {
        return SendNotificationAsync(
            "session/update",
            writer =>
            {
                writer.WriteStartObject();
                writer.WriteString("sessionId", sessionId);
                writer.WritePropertyName("update");
                writer.WriteStartObject();
                writer.WriteString("sessionUpdate", updateKind);
                writer.WritePropertyName("content");
                writer.WriteStartObject();
                writer.WriteString("type", "text");
                writer.WriteString("text", text);
                writer.WriteEndObject();
                writer.WriteEndObject();
                writer.WriteEndObject();
            },
            cancellationToken);
    }

    private async Task SendResultAsync(
        JsonElement? id,
        Action<Utf8JsonWriter> writeResult,
        CancellationToken cancellationToken)
    {
        if (id is null)
        {
            return;
        }

        await WriteMessageAsync(
            writer =>
            {
                writer.WriteStartObject();
                writer.WriteString("jsonrpc", "2.0");
                writer.WritePropertyName("id");
                WriteId(writer, id);
                writer.WritePropertyName("result");
                writeResult(writer);
                writer.WriteEndObject();
            },
            cancellationToken);
    }

    private async Task SendErrorAsync(
        JsonElement? id,
        int code,
        string message,
        CancellationToken cancellationToken)
    {
        await WriteMessageAsync(
            writer =>
            {
                writer.WriteStartObject();
                writer.WriteString("jsonrpc", "2.0");
                writer.WritePropertyName("id");
                WriteId(writer, id);
                writer.WritePropertyName("error");
                writer.WriteStartObject();
                writer.WriteNumber("code", code);
                writer.WriteString("message", message);
                writer.WriteEndObject();
                writer.WriteEndObject();
            },
            cancellationToken);
    }

    private Task SendNotificationAsync(
        string method,
        Action<Utf8JsonWriter> writeParams,
        CancellationToken cancellationToken)
    {
        return WriteMessageAsync(
            writer =>
            {
                writer.WriteStartObject();
                writer.WriteString("jsonrpc", "2.0");
                writer.WriteString("method", method);
                writer.WritePropertyName("params");
                writeParams(writer);
                writer.WriteEndObject();
            },
            cancellationToken);
    }

    private async Task WriteMessageAsync(
        Action<Utf8JsonWriter> writeMessage,
        CancellationToken cancellationToken)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writeMessage(writer);
        }

        string message = Encoding.UTF8.GetString(stream.ToArray());
        await _outputLock.WaitAsync(cancellationToken);
        try
        {
            await _output.WriteLineAsync(message);
            await _output.FlushAsync();
        }
        finally
        {
            _outputLock.Release();
        }
    }

    private void RouteResponse(JsonElement root)
    {
        if (!TryGetRequestId(root, out JsonElement id))
        {
            return;
        }

        string key = CreateIdKey(id);
        if (!_pendingRequests.TryRemove(key, out TaskCompletionSource<JsonElement>? completion))
        {
            return;
        }

        if (TryGetProperty(root, "error", out JsonElement error))
        {
            int code = TryGetInt32(error, "code", out int errorCode)
                ? errorCode
                : JsonRpcInternalError;
            string message = TryGetString(error, "message", out string errorMessage)
                ? errorMessage
                : "ACP client returned an error.";

            completion.TrySetException(new AcpRemoteException(code, message));
            return;
        }

        if (TryGetProperty(root, "result", out JsonElement result))
        {
            completion.TrySetResult(result.Clone());
            return;
        }

        completion.TrySetResult(default);
    }

    private AcpSession GetSession(string sessionId)
    {
        if (_session is null ||
            !string.Equals(_session.SessionId, sessionId, StringComparison.Ordinal))
        {
            throw new AcpProtocolException(JsonRpcInvalidParams, $"Unknown session '{sessionId}'.");
        }

        return _session;
    }

    private string[] CreateBackendArgs(string? sectionId)
    {
        List<string> args = [];
        for (int index = 0; index < _backendArgs.Length; index++)
        {
            string arg = _backendArgs[index];
            if (IsOptionWithValue(arg, "--section") ||
                IsOptionWithValue(arg, "--session"))
            {
                if (string.Equals(arg, "--section", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "--session", StringComparison.OrdinalIgnoreCase))
                {
                    index++;
                }

                continue;
            }

            if (string.Equals(arg, "--no-update-check", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            args.Add(arg);
        }

        if (!string.IsNullOrWhiteSpace(sectionId))
        {
            args.Add("--section");
            args.Add(sectionId.Trim());
        }

        args.Add("--no-update-check");
        return args.ToArray();
    }

    private static AcpPrompt ReadPrompt(JsonElement parameters)
    {
        if (!TryGetProperty(parameters, "prompt", out JsonElement promptElement) ||
            promptElement.ValueKind != JsonValueKind.Array)
        {
            throw new AcpProtocolException(JsonRpcInvalidParams, "session/prompt requires a prompt array.");
        }

        StringBuilder text = new();
        List<ConversationAttachment> attachments = [];
        int attachmentIndex = 0;

        foreach (JsonElement block in promptElement.EnumerateArray())
        {
            if (!TryGetString(block, "type", out string type))
            {
                continue;
            }

            switch (type)
            {
                case "text":
                    if (TryGetString(block, "text", out string blockText))
                    {
                        AppendParagraph(text, blockText);
                    }

                    break;

                case "image":
                    if (TryGetString(block, "data", out string imageData) &&
                        TryGetString(block, "mimeType", out string imageMimeType))
                    {
                        attachments.Add(new ConversationAttachment(
                            CreateAttachmentName(block, ++attachmentIndex, imageMimeType),
                            imageMimeType,
                            imageData));
                    }

                    break;

                case "resource":
                    AppendResource(text, attachments, block, ref attachmentIndex);
                    break;

                case "resource_link":
                    AppendResourceLink(text, block);
                    break;
            }
        }

        string normalizedText = text.ToString().Trim();
        if (string.IsNullOrWhiteSpace(normalizedText) && attachments.Count > 0)
        {
            normalizedText = "Please review the attached context.";
        }

        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            throw new AcpProtocolException(JsonRpcInvalidParams, "session/prompt requires text or supported context.");
        }

        return new AcpPrompt(normalizedText, attachments);
    }

    private static void AppendResource(
        StringBuilder text,
        List<ConversationAttachment> attachments,
        JsonElement block,
        ref int attachmentIndex)
    {
        if (!TryGetProperty(block, "resource", out JsonElement resource) ||
            resource.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        string uri = TryGetString(resource, "uri", out string resourceUri)
            ? resourceUri
            : "resource";
        string mimeType = TryGetString(resource, "mimeType", out string resourceMimeType)
            ? resourceMimeType
            : "text/plain";

        if (TryGetString(resource, "text", out string resourceText))
        {
            AppendParagraph(
                text,
                $"Resource: {uri}{Environment.NewLine}{resourceText}");
            return;
        }

        if (TryGetString(resource, "blob", out string blob) &&
            mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            attachments.Add(new ConversationAttachment(
                CreateAttachmentName(resource, ++attachmentIndex, mimeType),
                mimeType,
                blob));
            return;
        }

        AppendParagraph(text, $"Resource attached but not embedded: {uri}");
    }

    private static void AppendResourceLink(StringBuilder text, JsonElement block)
    {
        string uri = TryGetString(block, "uri", out string linkUri)
            ? linkUri
            : "resource";
        string name = TryGetString(block, "title", out string title)
            ? title
            : TryGetString(block, "name", out string linkName)
                ? linkName
                : uri;

        AppendParagraph(text, $"Resource link: {name} ({uri})");
    }

    private static string CreateAttachmentName(JsonElement block, int attachmentIndex, string mimeType)
    {
        if (TryGetString(block, "uri", out string uri) &&
            Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsedUri) &&
            !string.IsNullOrWhiteSpace(Path.GetFileName(parsedUri.LocalPath)))
        {
            return Path.GetFileName(parsedUri.LocalPath);
        }

        string extension = mimeType.EndsWith("/jpeg", StringComparison.OrdinalIgnoreCase)
            ? ".jpg"
            : mimeType.EndsWith("/png", StringComparison.OrdinalIgnoreCase)
                ? ".png"
                : ".bin";

        return $"attachment-{attachmentIndex}{extension}";
    }

    private static void AppendParagraph(StringBuilder builder, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine();
        }

        builder.Append(value.Trim());
    }

    private static JsonElement GetRequiredParams(JsonElement root)
    {
        if (!TryGetProperty(root, "params", out JsonElement parameters) ||
            parameters.ValueKind != JsonValueKind.Object)
        {
            throw new AcpProtocolException(JsonRpcInvalidParams, "Request params must be an object.");
        }

        return parameters;
    }

    private static string ReadRequiredAbsolutePath(JsonElement element, string propertyName)
    {
        string path = ReadRequiredString(element, propertyName);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new AcpProtocolException(JsonRpcInvalidParams, $"{propertyName} must be an absolute path.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new AcpProtocolException(JsonRpcInvalidParams, $"{propertyName} must be an absolute path.");
        }

        if (!Path.IsPathFullyQualified(fullPath))
        {
            throw new AcpProtocolException(JsonRpcInvalidParams, $"{propertyName} must be an absolute path.");
        }

        if (!Directory.Exists(fullPath))
        {
            throw new AcpProtocolException(JsonRpcInvalidParams, $"{propertyName} does not exist.");
        }

        return fullPath;
    }

    private static string ReadRequiredString(JsonElement element, string propertyName)
    {
        if (!TryGetString(element, propertyName, out string value) ||
            string.IsNullOrWhiteSpace(value))
        {
            throw new AcpProtocolException(JsonRpcInvalidParams, $"Missing required string parameter '{propertyName}'.");
        }

        return value.Trim();
    }

    private void LogUnsupportedMcpServers(JsonElement parameters)
    {
        if (TryGetProperty(parameters, "mcpServers", out JsonElement mcpServers) &&
            mcpServers.ValueKind == JsonValueKind.Array &&
            mcpServers.GetArrayLength() > 0)
        {
            _error.WriteLine(
                "NanoAgent ACP: client-supplied mcpServers are not imported yet; using NanoAgent MCP configuration.");
        }
    }

    private static bool IsResponse(JsonElement root)
    {
        return TryGetRequestId(root, out _) &&
            (TryGetProperty(root, "result", out _) || TryGetProperty(root, "error", out _)) &&
            !TryGetProperty(root, "method", out _);
    }

    private static bool TryGetRequestId(JsonElement root, out JsonElement id)
    {
        return TryGetProperty(root, "id", out id);
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

    private static bool TryGetInt32(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        return TryGetProperty(element, propertyName, out JsonElement property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out value);
    }

    private static bool IsOptionWithValue(string arg, string optionName)
    {
        return string.Equals(arg, optionName, StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith(optionName + "=", StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteId(Utf8JsonWriter writer, JsonElement? id)
    {
        if (id is null)
        {
            writer.WriteNullValue();
            return;
        }

        id.Value.WriteTo(writer);
    }

    private static void WriteToolTextContent(Utf8JsonWriter writer, string content)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "content");
        writer.WritePropertyName("content");
        writer.WriteStartObject();
        writer.WriteString("type", "text");
        writer.WriteString("text", content);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteJsonObjectOrString(
        Utf8JsonWriter writer,
        string propertyName,
        string json)
    {
        writer.WritePropertyName(propertyName);
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            document.RootElement.WriteTo(writer);
        }
        catch (JsonException)
        {
            writer.WriteStringValue(json);
        }
    }

    private static string CreateIdKey(long id)
    {
        return $"n:{id}";
    }

    private static string CreateIdKey(JsonElement id)
    {
        return id.ValueKind switch
        {
            JsonValueKind.Number when id.TryGetInt64(out long number) => CreateIdKey(number),
            JsonValueKind.String => "s:" + (id.GetString() ?? string.Empty),
            JsonValueKind.Null => "null",
            _ => id.GetRawText()
        };
    }

    private static string GetVersion()
    {
        return typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
            typeof(Program).Assembly.GetName().Version?.ToString() ??
            "0.0.0";
    }

    private static string? NormalizeOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private sealed record AcpPrompt(
        string Text,
        IReadOnlyList<ConversationAttachment> Attachments);

    private sealed class AcpSession
    {
        public AcpSession(
            string sessionId,
            string workingDirectory,
            INanoAgentBackend backend,
            AcpUiBridge bridge)
        {
            SessionId = sessionId;
            WorkingDirectory = workingDirectory;
            Backend = backend;
            Bridge = bridge;
        }

        public CancellationTokenSource? ActivePromptCancellation { get; set; }

        public INanoAgentBackend Backend { get; }

        public AcpUiBridge Bridge { get; }

        public string SessionId { get; }

        public SemaphoreSlim TurnLock { get; } = new(1, 1);

        public string WorkingDirectory { get; }
    }

    private sealed class AcpProtocolException : Exception
    {
        public AcpProtocolException(int code, string message)
            : base(message)
        {
            Code = code;
        }

        public int Code { get; }
    }

    private sealed class AcpRemoteException : Exception
    {
        public AcpRemoteException(int code, string message)
            : base(message)
        {
            Code = code;
        }

        public int Code { get; }
    }

    private sealed class AcpUiBridge : IUiBridge
    {
        private readonly AcpServer _server;
        private readonly TextWriter _error;
        private readonly object _providerAuthKeySync = new();
        private readonly object _tailSync = new();
        private readonly IToolOutputFormatter _toolOutputFormatter = new ToolOutputFormatter();
        private string? _providerAuthKey;
        private bool _providerAuthKeyConsumed;
        private Task _tail = Task.CompletedTask;

        public AcpUiBridge(
            AcpServer server,
            TextWriter error,
            string? providerAuthKey)
        {
            _server = server;
            _error = error;
            _providerAuthKey = NormalizeOrNull(providerAuthKey);
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

            if (string.IsNullOrWhiteSpace(SessionId))
            {
                return Task.FromException<T>(
                    new PromptCancelledException(
                        $"Prompt '{request.Title}' requires an active ACP session."));
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

            return Task.FromException<string>(
                new PromptCancelledException(
                    $"Prompt '{request.Label}' requires interactive input. Run nanoai once to finish setup before using ACP."));
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

                EnqueueNotification(token => _server.SendToolCallUpdateAsync(
                    sessionId,
                    result.ToolCallId,
                    result.Result.IsSuccess,
                    result.ToDisplayText(),
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

        private async Task<T> RequestSelectionViaAcpAsync<T>(
            SelectionPromptRequest<T> request,
            CancellationToken cancellationToken)
        {
            string sessionId = SessionId!;
            string toolCallId = "prompt-" + Guid.NewGuid().ToString("N");

            JsonElement result = await _server.SendClientRequestAsync(
                "session/request_permission",
                writer =>
                {
                    writer.WriteStartObject();
                    writer.WriteString("sessionId", sessionId);
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
                        WriteToolTextContent(writer, request.Description);
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

        private void EnqueueSessionText(string message)
        {
            string? sessionId = SessionId;
            if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(message))
            {
                if (!string.IsNullOrWhiteSpace(message))
                {
                    _error.WriteLine(message.Trim());
                }

                return;
            }

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
                "web_run" or "headless_browser" => "fetch",
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
    }
}
