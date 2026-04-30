using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Utilities;
using System.Text;
using System.Text.Json;

namespace NanoAgent.Infrastructure.Storage;

internal sealed class WorkspaceToolAuditLogService : IToolAuditLogService
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ToolAuditSettings _settings;
    private readonly IWorkspaceRootProvider _workspaceRootProvider;

    public WorkspaceToolAuditLogService(
        IWorkspaceRootProvider workspaceRootProvider,
        ToolAuditSettings? settings = null)
    {
        _workspaceRootProvider = workspaceRootProvider;
        _settings = NormalizeSettings(settings ?? new ToolAuditSettings());
    }

    public string GetStoragePath()
    {
        return Path.Combine(
            Path.GetFullPath(_workspaceRootProvider.GetWorkspaceRoot()),
            ".nanoagent",
            "logs",
            "tool-audit.jsonl");
    }

    public async Task RecordAsync(
        ConversationToolCall toolCall,
        ToolInvocationResult invocationResult,
        ReplSessionContext session,
        ConversationExecutionPhase executionPhase,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(toolCall);
        ArgumentNullException.ThrowIfNull(invocationResult);
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_settings.Enabled)
        {
            return;
        }

        ToolAuditRecord record = new(
            completedAtUtc,
            session.SessionId,
            session.AgentProfileName,
            executionPhase.ToString(),
            toolCall.Id,
            toolCall.Name,
            invocationResult.Result.Status.ToString(),
            Math.Max(0, (long)(completedAtUtc - startedAtUtc).TotalMilliseconds),
            session.WorkingDirectory,
            PrepareField(toolCall.ArgumentsJson, _settings.MaxArgumentsChars),
            PrepareField(invocationResult.Result.Message, maxChars: 2_000),
            PrepareField(invocationResult.Result.JsonResult, _settings.MaxResultChars));

        string json = JsonSerializer.Serialize(
            record,
            ToolAuditLogJsonContext.Default.ToolAuditRecord);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            string storagePath = GetStoragePath();
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
        }
        finally
        {
            _gate.Release();
        }
    }

    private string PrepareField(
        string value,
        int maxChars)
    {
        string prepared = _settings.RedactSecrets
            ? SecretRedactor.Redact(value)
            : value;

        return TrimField(prepared, maxChars);
    }

    private static void EnsureStorageDirectory(string storagePath)
    {
        string? directoryPath = Path.GetDirectoryName(storagePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }
    }

    private static ToolAuditSettings NormalizeSettings(ToolAuditSettings settings)
    {
        settings.MaxArgumentsChars = settings.MaxArgumentsChars <= 0
            ? 12_000
            : Math.Min(settings.MaxArgumentsChars, 250_000);
        settings.MaxResultChars = settings.MaxResultChars <= 0
            ? 12_000
            : Math.Min(settings.MaxResultChars, 250_000);
        return settings;
    }

    private static string TrimField(
        string value,
        int maxChars)
    {
        if (maxChars <= 0 || value.Length <= maxChars)
        {
            return value;
        }

        return value[..Math.Max(0, maxChars - 3)] + "...";
    }
}
