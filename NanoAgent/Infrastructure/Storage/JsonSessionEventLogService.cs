using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Utilities;
using System.Text;
using System.Text.Json;

namespace NanoAgent.Infrastructure.Storage;

internal sealed class JsonSessionEventLogService : ISessionEventLogService
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _timeProvider;
    private readonly IUserDataPathProvider _userDataPathProvider;

    public JsonSessionEventLogService(
        IUserDataPathProvider userDataPathProvider,
        TimeProvider? timeProvider = null)
    {
        _userDataPathProvider = userDataPathProvider;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string GetStoragePath(string sectionId)
    {
        string normalizedSectionId = NormalizeSectionId(sectionId);
        return Path.Combine(
            _userDataPathProvider.GetSessionsDirectoryPath(),
            $"{normalizedSectionId}.events.jsonl");
    }

    public Task RecordUserInputAsync(
        ReplSessionContext session,
        string input,
        CancellationToken cancellationToken)
    {
        return AppendSafeAsync(
            CreateRecord(
                session,
                "user_input",
                text: input),
            cancellationToken);
    }

    public Task RecordAssistantReasoningAsync(
        ReplSessionContext session,
        string reasoningText,
        CancellationToken cancellationToken)
    {
        return AppendSafeAsync(
            CreateRecord(
                session,
                "assistant_reasoning",
                text: reasoningText),
            cancellationToken);
    }

    public Task RecordAssistantOutputAsync(
        ReplSessionContext session,
        string outputText,
        CancellationToken cancellationToken)
    {
        return AppendSafeAsync(
            CreateRecord(
                session,
                "assistant_output",
                text: outputText),
            cancellationToken);
    }

    public Task RecordToolCallRequestedAsync(
        ReplSessionContext session,
        ConversationToolCall toolCall,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(toolCall);

        return AppendSafeAsync(
            CreateRecord(
                session,
                "assistant_tool_call_request",
                toolCallId: toolCall.Id,
                toolName: toolCall.Name,
                toolArgumentsJson: toolCall.ArgumentsJson),
            cancellationToken);
    }

    public Task RecordToolResultAsync(
        ReplSessionContext session,
        ToolInvocationResult invocationResult,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocationResult);

        return AppendSafeAsync(
            CreateRecord(
                session,
                "tool_call_response",
                toolCallId: invocationResult.ToolCallId,
                toolName: invocationResult.ToolName,
                toolStatus: invocationResult.Result.Status.ToString(),
                toolMessage: invocationResult.Result.Message,
                toolResultJson: invocationResult.Result.JsonResult),
            cancellationToken);
    }

    public Task RecordExecutionPlanAsync(
        ReplSessionContext session,
        ExecutionPlanProgress executionPlanProgress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(executionPlanProgress);

        string text = executionPlanProgress.Tasks.Count == 0
            ? "Execution plan is empty."
            : $"Completed {executionPlanProgress.CompletedTaskCount} of {executionPlanProgress.Tasks.Count}: " +
              string.Join(" | ", executionPlanProgress.Tasks);

        return AppendSafeAsync(
            CreateRecord(
                session,
                "execution_plan",
                text: text),
            cancellationToken);
    }

    public Task RecordTurnFailureAsync(
        ReplSessionContext session,
        string input,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exception);

        string text = string.IsNullOrWhiteSpace(input)
            ? exception.Message
            : $"Input: {input}{Environment.NewLine}Error: {exception.Message}";

        return AppendSafeAsync(
            CreateRecord(
                session,
                "turn_failed",
                text: text,
                errorType: exception.GetType().FullName),
            cancellationToken);
    }

    private async Task AppendSafeAsync(
        SessionEventRecord record,
        CancellationToken cancellationToken)
    {
        try
        {
            await AppendAsync(record, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Event logging is best-effort and should not break the active turn.
        }
    }

    private async Task AppendAsync(
        SessionEventRecord record,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string json = JsonSerializer.Serialize(
            record,
            SessionEventLogJsonContext.Default.SessionEventRecord);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            string storagePath = GetStoragePath(record.SectionId);
            EnsureStorageDirectory(storagePath);

            await using FileStream stream = new(
                storagePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous);
            await using StreamWriter writer = new(stream, Utf8NoBom);
            await writer.WriteLineAsync(json.AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);
            FilePermissionHelper.EnsurePrivateFile(storagePath);
        }
        finally
        {
            _gate.Release();
        }
    }

    private SessionEventRecord CreateRecord(
        ReplSessionContext session,
        string eventType,
        string? text = null,
        string? toolCallId = null,
        string? toolName = null,
        string? toolArgumentsJson = null,
        string? toolStatus = null,
        string? toolMessage = null,
        string? toolResultJson = null,
        string? errorType = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);

        return new SessionEventRecord(
            _timeProvider.GetUtcNow(),
            session.SectionId,
            session.ParentSessionId,
            eventType.Trim(),
            session.AgentProfileName,
            session.ActiveModelId,
            session.WorkingDirectory,
            NormalizeText(text),
            NormalizeText(toolCallId),
            NormalizeText(toolName),
            NormalizeJsonText(toolArgumentsJson),
            NormalizeText(toolStatus),
            NormalizeText(toolMessage),
            NormalizeJsonText(toolResultJson),
            NormalizeText(errorType));
    }

    private static string NormalizeSectionId(string sectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionId);

        if (!Guid.TryParse(sectionId.Trim(), out Guid parsedSectionId))
        {
            throw new ArgumentException(
                "Section id must be a valid GUID.",
                nameof(sectionId));
        }

        return parsedSectionId.ToString("D");
    }

    private static void EnsureStorageDirectory(string storagePath)
    {
        string? directoryPath = Path.GetDirectoryName(storagePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            FilePermissionHelper.EnsurePrivateDirectory(directoryPath);
        }
    }

    private static string? NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return SecretRedactor.Redact(value)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
    }

    private static string? NormalizeJsonText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : SecretRedactor.Redact(value.Trim());
    }
}
