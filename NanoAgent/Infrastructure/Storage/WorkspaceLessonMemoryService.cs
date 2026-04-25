using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;
using NanoAgent.Application.Utilities;

namespace NanoAgent.Infrastructure.Storage;

internal sealed partial class WorkspaceLessonMemoryService : ILessonMemoryService
{
    private const int DefaultPromptLessonLimit = 5;
    private const int MaxPendingFailureObservations = 50;
    private const int MaxPromptFieldCharacters = 260;
    private const int MaxSearchTokens = 24;
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _pendingFailureGate = new();
    private readonly Dictionary<string, PendingFailureObservation> _pendingFailures = new(StringComparer.Ordinal);
    private readonly MemorySettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly IWorkspaceRootProvider _workspaceRootProvider;

    public WorkspaceLessonMemoryService(
        IWorkspaceRootProvider workspaceRootProvider,
        TimeProvider timeProvider,
        MemorySettings? settings = null)
    {
        _workspaceRootProvider = workspaceRootProvider;
        _timeProvider = timeProvider;
        _settings = NormalizeSettings(settings ?? new MemorySettings());
    }

    public string GetStoragePath()
    {
        return Path.Combine(
            Path.GetFullPath(_workspaceRootProvider.GetWorkspaceRoot()),
            ".nanoagent",
            "memory",
            "lessons.jsonl");
    }

    public async Task<LessonMemoryEntry> SaveAsync(
        LessonMemorySaveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (_settings.Disabled)
        {
            throw new InvalidOperationException("Lesson memory is disabled.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        LessonMemoryEntry entry = NormalizeEntry(new LessonMemoryEntry(
            CreateId(),
            now,
            now,
            NormalizeKind(request.Kind),
            RedactIfNeeded(RequireText(request.Trigger, nameof(request.Trigger))),
            RedactIfNeeded(RequireText(request.Problem, nameof(request.Problem))),
            RedactIfNeeded(RequireText(request.Lesson, nameof(request.Lesson))),
            NormalizeTags(request.Tags),
            NormalizeOptionalText(request.ToolName),
            RedactOptionalIfNeeded(NormalizeOptionalText(request.Command)),
            RedactOptionalIfNeeded(NormalizeOptionalText(request.FailureSignature)),
            NormalizeOptionalText(request.Fingerprint),
            request.IsFixed,
            request.IsFixed ? now : null,
            RedactOptionalIfNeeded(NormalizeOptionalText(request.FixSummary))));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await AppendAsync(entry, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        return entry;
    }

    public async Task<IReadOnlyList<LessonMemoryEntry>> SearchAsync(
        string query,
        int limit,
        bool includeFixed,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_settings.Disabled)
        {
            return [];
        }

        int safeLimit = NormalizeLimit(limit);
        string[] tokens = Tokenize(query);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            LessonMemoryEntry[] entries = await LoadAsync(cancellationToken);
            IEnumerable<(LessonMemoryEntry Entry, int Score)> scored = entries
                .Where(entry => includeFixed || !entry.IsFixed)
                .Select(entry => (Entry: entry, Score: ScoreEntry(entry, tokens, query)));

            if (tokens.Length > 0)
            {
                scored = scored.Where(item => item.Score > 0);
            }

            return scored
                .OrderByDescending(static item => item.Score)
                .ThenByDescending(static item => item.Entry.UpdatedAtUtc)
                .Take(safeLimit)
                .Select(static item => item.Entry)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<LessonMemoryEntry>> ListAsync(
        int limit,
        bool includeFixed,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_settings.Disabled)
        {
            return [];
        }

        int safeLimit = NormalizeLimit(limit);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            return (await LoadAsync(cancellationToken))
                .Where(entry => includeFixed || !entry.IsFixed)
                .OrderByDescending(static entry => entry.UpdatedAtUtc)
                .Take(safeLimit)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<LessonMemoryEntry?> EditAsync(
        LessonMemoryEditRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (_settings.Disabled)
        {
            return null;
        }

        string id = RequireText(request.Id, nameof(request.Id));
        DateTimeOffset now = _timeProvider.GetUtcNow();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            LessonMemoryEntry[] entries = await LoadAsync(cancellationToken);
            int index = Array.FindIndex(entries, entry => string.Equals(entry.Id, id, StringComparison.Ordinal));
            if (index < 0)
            {
                return null;
            }

            LessonMemoryEntry current = entries[index];
            bool isFixed = request.IsFixed ?? current.IsFixed;
            DateTimeOffset? fixedAt = current.FixedAtUtc;
            if (request.IsFixed is true && current.FixedAtUtc is null)
            {
                fixedAt = now;
            }
            else if (request.IsFixed is false)
            {
                fixedAt = null;
            }

            LessonMemoryEntry updated = NormalizeEntry(current with
            {
                UpdatedAtUtc = now,
                Kind = request.Kind is null ? current.Kind : NormalizeKind(request.Kind),
                Trigger = request.Trigger is null ? current.Trigger : RedactIfNeeded(RequireText(request.Trigger, nameof(request.Trigger))),
                Problem = request.Problem is null ? current.Problem : RedactIfNeeded(RequireText(request.Problem, nameof(request.Problem))),
                Lesson = request.Lesson is null ? current.Lesson : RedactIfNeeded(RequireText(request.Lesson, nameof(request.Lesson))),
                Tags = request.Tags is null ? current.Tags : NormalizeTags(request.Tags),
                ToolName = request.ToolName is null ? current.ToolName : NormalizeOptionalText(request.ToolName),
                Command = request.Command is null ? current.Command : RedactOptionalIfNeeded(NormalizeOptionalText(request.Command)),
                FailureSignature = request.FailureSignature is null ? current.FailureSignature : RedactOptionalIfNeeded(NormalizeOptionalText(request.FailureSignature)),
                Fingerprint = request.Fingerprint is null ? current.Fingerprint : NormalizeOptionalText(request.Fingerprint),
                IsFixed = isFixed,
                FixedAtUtc = fixedAt,
                FixSummary = request.FixSummary is null ? current.FixSummary : RedactOptionalIfNeeded(NormalizeOptionalText(request.FixSummary))
            });

            entries[index] = updated;
            await RewriteAsync(entries, cancellationToken);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(
        string id,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_settings.Disabled)
        {
            return false;
        }

        string normalizedId = RequireText(id, nameof(id));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            LessonMemoryEntry[] entries = await LoadAsync(cancellationToken);
            LessonMemoryEntry[] retained = entries
                .Where(entry => !string.Equals(entry.Id, normalizedId, StringComparison.Ordinal))
                .ToArray();

            if (retained.Length == entries.Length)
            {
                return false;
            }

            await RewriteAsync(retained, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> CreatePromptAsync(
        string query,
        CancellationToken cancellationToken)
    {
        if (_settings.Disabled)
        {
            return null;
        }

        IReadOnlyList<LessonMemoryEntry> lessons = await SearchAsync(
            query,
            DefaultPromptLessonLimit,
            includeFixed: true,
            cancellationToken);

        if (lessons.Count == 0)
        {
            return null;
        }

        StringBuilder builder = new();
        builder.AppendLine("Relevant lesson memory:");
        builder.AppendLine("Persistent workspace lessons from .nanoagent/memory/lessons.jsonl. Use them as starting hypotheses, then verify against current files and fresh tool output.");

        foreach (LessonMemoryEntry lesson in lessons)
        {
            builder
                .Append("- [")
                .Append(lesson.Id)
                .Append("; ")
                .Append(lesson.Kind)
                .Append("; ")
                .Append(lesson.IsFixed ? "fixed" : "active")
                .Append("] ");

            if (!string.IsNullOrWhiteSpace(lesson.Trigger))
            {
                builder
                    .Append("Trigger: ")
                    .Append(TrimForPrompt(lesson.Trigger))
                    .Append(". ");
            }

            if (!string.IsNullOrWhiteSpace(lesson.Problem))
            {
                builder
                    .Append("Problem: ")
                    .Append(TrimForPrompt(lesson.Problem))
                    .Append(". ");
            }

            builder
                .Append("Lesson: ")
                .Append(TrimForPrompt(lesson.Lesson));

            if (!string.IsNullOrWhiteSpace(lesson.FixSummary))
            {
                builder
                    .Append(". Fix: ")
                    .Append(TrimForPrompt(lesson.FixSummary));
            }

            if (lesson.Tags.Length > 0)
            {
                builder
                    .Append(". Tags: ")
                    .Append(string.Join(", ", lesson.Tags));
            }

            builder.AppendLine();
        }

        return TrimPrompt(builder.ToString().Trim());
    }

    public async Task ObserveToolResultAsync(
        ConversationToolCall toolCall,
        ToolInvocationResult invocationResult,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(toolCall);
        ArgumentNullException.ThrowIfNull(invocationResult);
        cancellationToken.ThrowIfCancellationRequested();

        if (_settings.Disabled ||
            !_settings.AllowAutoFailureObservation)
        {
            return;
        }

        if (string.Equals(invocationResult.ToolName, AgentToolNames.LessonMemory, StringComparison.Ordinal))
        {
            return;
        }

        if (string.Equals(invocationResult.ToolName, AgentToolNames.ShellCommand, StringComparison.Ordinal) &&
            TryReadShellCommandResult(invocationResult, out ShellCommandExecutionResult? shellResult) &&
            shellResult is not null)
        {
            await ObserveShellCommandAsync(shellResult, cancellationToken);
            return;
        }

        if (invocationResult.Result.IsSuccess)
        {
            await ObserveSuccessfulToolAsync(toolCall, invocationResult, cancellationToken);
            return;
        }

        if (TryCreatePendingToolFailure(toolCall, invocationResult, out PendingFailureObservation? observation) &&
            observation is not null)
        {
            RememberPendingFailure(observation);
        }
    }

    private async Task ObserveShellCommandAsync(
        ShellCommandExecutionResult result,
        CancellationToken cancellationToken)
    {
        string fingerprint = CreateShellFingerprint(result.Command);

        if (result.ExitCode == 0)
        {
            if (TryTakePendingFailure(
                    AgentToolNames.ShellCommand,
                    fingerprint,
                    out PendingFailureObservation? observation) &&
                observation is not null)
            {
                string successSummary = RedactIfNeeded(
                    $"command `{NormalizeWhitespace(result.Command)}` exited 0 in `{NormalizeWhitespace(result.WorkingDirectory)}`");
                await SaveOrUpdateResolvedLessonAsync(
                    observation,
                    BuildResolvedLesson(observation, successSummary),
                    $"Corrected successful attempt: {successSummary}.",
                    cancellationToken);
            }

            return;
        }

        if (!IsTrackableShellFailure(result.Command))
        {
            return;
        }

        string signature = ExtractFailureSignature(result) ?? $"exit {result.ExitCode.ToString(CultureInfo.InvariantCulture)}";
        string redactedSignature = RedactIfNeeded(signature);
        string category = DetectShellCategory(result.Command);
        string command = NormalizeWhitespace(result.Command);
        string attemptSummary = RedactIfNeeded(
            $"command `{command}` exited {result.ExitCode.ToString(CultureInfo.InvariantCulture)} in `{NormalizeWhitespace(result.WorkingDirectory)}`");
        RememberPendingFailure(new PendingFailureObservation(
            fingerprint,
            _timeProvider.GetUtcNow(),
            AgentToolNames.ShellCommand,
            RedactIfNeeded($"{command} -> {signature}"),
            RedactIfNeeded($"Command `{command}` failed with exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}. Signature: {signature}."),
            attemptSummary,
            command,
            signature,
            NormalizeTags(["auto", "failure", "resolved", category, GetFirstCommandName(result.Command), redactedSignature])));
    }

    private async Task ObserveSuccessfulToolAsync(
        ConversationToolCall toolCall,
        ToolInvocationResult invocationResult,
        CancellationToken cancellationToken)
    {
        if (!TryTakePendingFailure(
                invocationResult.ToolName,
                preferredFingerprint: null,
                out PendingFailureObservation? observation) ||
            observation is null)
        {
            return;
        }

        string successSummary = SummarizeToolCall(toolCall, invocationResult);
        await SaveOrUpdateResolvedLessonAsync(
            observation,
            BuildResolvedLesson(observation, successSummary),
            $"Corrected successful attempt: {successSummary}.",
            cancellationToken);
    }

    private bool TryCreatePendingToolFailure(
        ConversationToolCall toolCall,
        ToolInvocationResult invocationResult,
        out PendingFailureObservation? observation)
    {
        observation = null;

        if (!IsTrackableToolFailure(invocationResult))
        {
            return false;
        }

        string toolName = invocationResult.ToolName;
        ToolErrorPayload? error = TryReadToolErrorPayload(invocationResult.Result, out ToolErrorPayload? payload)
            ? payload
            : null;
        string failureSignature = NormalizeOptionalText(error?.Code) ?? invocationResult.Result.Status.ToString();
        string redactedFailureSignature = RedactIfNeeded(failureSignature);
        string failureMessage = NormalizeOptionalText(error?.Message) ?? invocationResult.Result.Message;
        string attemptSummary = SummarizeToolCall(toolCall, invocationResult);
        observation = new PendingFailureObservation(
            CreateToolFingerprint(toolName, failureSignature),
            _timeProvider.GetUtcNow(),
            toolName,
            RedactIfNeeded($"{toolName} {failureSignature}"),
            RedactIfNeeded($"Tool `{toolName}` failed with {failureSignature}: {failureMessage}. Failed pattern: {attemptSummary}."),
            attemptSummary,
            Command: null,
            FailureSignature: failureSignature,
            Tags: NormalizeTags(["auto", "failure", "resolved", "tool", toolName, redactedFailureSignature]));
        return true;
    }

    private async Task SaveOrUpdateResolvedLessonAsync(
        PendingFailureObservation observation,
        string lesson,
        string fixSummary,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            LessonMemoryEntry[] entries = await LoadAsync(cancellationToken);
            int index = Array.FindLastIndex(entries, entry =>
                string.Equals(entry.Fingerprint, observation.Fingerprint, StringComparison.Ordinal));

            if (index >= 0)
            {
                entries[index] = NormalizeEntry(entries[index] with
                {
                    UpdatedAtUtc = now,
                    Kind = "lesson",
                    Trigger = RedactIfNeeded(observation.Trigger),
                    Problem = RedactIfNeeded(observation.Problem),
                    Lesson = RedactIfNeeded(lesson),
                    Tags = NormalizeTags(entries[index].Tags.Concat(observation.Tags).Concat(["fixed"])),
                    ToolName = observation.ToolName,
                    Command = RedactOptionalIfNeeded(NormalizeOptionalText(observation.Command)),
                    FailureSignature = RedactOptionalIfNeeded(NormalizeOptionalText(observation.FailureSignature)),
                    Fingerprint = observation.Fingerprint,
                    IsFixed = true,
                    FixedAtUtc = entries[index].FixedAtUtc ?? now,
                    FixSummary = RedactIfNeeded(fixSummary)
                });
                await RewriteAsync(entries, cancellationToken);
                return;
            }

            LessonMemoryEntry entry = NormalizeEntry(new LessonMemoryEntry(
                CreateId(),
                now,
                now,
                "lesson",
                RedactIfNeeded(observation.Trigger),
                RedactIfNeeded(observation.Problem),
                RedactIfNeeded(lesson),
                NormalizeTags(observation.Tags.Concat(["fixed"])),
                observation.ToolName,
                RedactOptionalIfNeeded(NormalizeOptionalText(observation.Command)),
                RedactOptionalIfNeeded(NormalizeOptionalText(observation.FailureSignature)),
                observation.Fingerprint,
                IsFixed: true,
                FixedAtUtc: now,
                FixSummary: RedactIfNeeded(fixSummary)));

            await RewriteAsync(entries.Append(entry).ToArray(), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void RememberPendingFailure(PendingFailureObservation observation)
    {
        lock (_pendingFailureGate)
        {
            _pendingFailures[observation.Fingerprint] = observation;
            if (_pendingFailures.Count <= MaxPendingFailureObservations)
            {
                return;
            }

            string oldestKey = _pendingFailures
                .OrderBy(static item => item.Value.ObservedAtUtc)
                .First()
                .Key;
            _pendingFailures.Remove(oldestKey);
        }
    }

    private bool TryTakePendingFailure(
        string toolName,
        string? preferredFingerprint,
        out PendingFailureObservation? observation)
    {
        lock (_pendingFailureGate)
        {
            if (!string.IsNullOrWhiteSpace(preferredFingerprint) &&
                _pendingFailures.Remove(preferredFingerprint, out observation))
            {
                return true;
            }

            string? latestKey = null;
            PendingFailureObservation? latestObservation = null;
            foreach (KeyValuePair<string, PendingFailureObservation> item in _pendingFailures)
            {
                if (!string.Equals(item.Value.ToolName, toolName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (latestObservation is null ||
                    item.Value.ObservedAtUtc > latestObservation.ObservedAtUtc)
                {
                    latestKey = item.Key;
                    latestObservation = item.Value;
                }
            }

            if (latestKey is null || latestObservation is null)
            {
                observation = null;
                return false;
            }

            observation = latestObservation;
            _pendingFailures.Remove(latestKey);
            return true;
        }
    }

    private string BuildResolvedLesson(
        PendingFailureObservation observation,
        string successSummary)
    {
        string signature = string.IsNullOrWhiteSpace(observation.FailureSignature)
            ? observation.ToolName
            : $"{observation.ToolName} {observation.FailureSignature}";

        return RedactIfNeeded(
            $"When {signature} appears again, do not repeat the failed pattern ({observation.AttemptSummary}). Use the corrected successful pattern: {successSummary}.");
    }

    private string SummarizeToolCall(
        ConversationToolCall toolCall,
        ToolInvocationResult invocationResult)
    {
        string argumentSummary = SummarizeToolArguments(toolCall);
        string resultSummary = TrimForPrompt(invocationResult.Result.Message);
        string summary = string.IsNullOrWhiteSpace(argumentSummary)
            ? $"result: {resultSummary}"
            : $"{argumentSummary}; result: {resultSummary}";

        return RedactIfNeeded(summary);
    }

    private static string SummarizeToolArguments(ConversationToolCall toolCall)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(toolCall.ArgumentsJson);
            JsonElement arguments = document.RootElement;
            if (arguments.ValueKind != JsonValueKind.Object)
            {
                return "arguments were not a JSON object";
            }

            return toolCall.Name switch
            {
                AgentToolNames.ApplyPatch => SummarizePatchArguments(arguments),
                AgentToolNames.FileRead => SummarizePathArguments(arguments, "file_read"),
                AgentToolNames.FileDelete => SummarizePathArguments(arguments, "file_delete"),
                AgentToolNames.FileWrite => SummarizeFileWriteArguments(arguments),
                AgentToolNames.DirectoryList => SummarizeDirectoryListArguments(arguments),
                AgentToolNames.SearchFiles => SummarizeSearchArguments(arguments, "search_files"),
                AgentToolNames.TextSearch => SummarizeSearchArguments(arguments, "text_search"),
                AgentToolNames.ShellCommand => SummarizeShellArguments(arguments),
                _ => $"arguments: {TrimForPrompt(arguments.GetRawText())}"
            };
        }
        catch (JsonException)
        {
            return $"arguments JSON was invalid ({toolCall.ArgumentsJson.Length.ToString(CultureInfo.InvariantCulture)} chars)";
        }
    }

    private static string SummarizePatchArguments(JsonElement arguments)
    {
        string? patch = GetString(arguments, "patch");
        if (string.IsNullOrWhiteSpace(patch))
        {
            return "apply_patch without patch text";
        }

        string[] headers = patch
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static line =>
                line.StartsWith("*** Add File: ", StringComparison.Ordinal) ||
                line.StartsWith("*** Delete File: ", StringComparison.Ordinal) ||
                line.StartsWith("*** Update File: ", StringComparison.Ordinal) ||
                line.StartsWith("*** Move to: ", StringComparison.Ordinal))
            .Take(5)
            .ToArray();

        string targetSummary = headers.Length == 0
            ? "no file headers"
            : string.Join(", ", headers);
        return $"apply_patch ({patch.Length.ToString(CultureInfo.InvariantCulture)} chars, {targetSummary})";
    }

    private static string SummarizePathArguments(
        JsonElement arguments,
        string toolName)
    {
        return $"{toolName} path `{GetString(arguments, "path") ?? "<missing>"}`";
    }

    private static string SummarizeFileWriteArguments(JsonElement arguments)
    {
        string path = GetString(arguments, "path") ?? "<missing>";
        string? content = GetString(arguments, "content");
        string overwrite = FormatBoolean(GetBoolean(arguments, "overwrite"), defaultValue: "false");
        string contentLength = content is null
            ? "missing content"
            : $"{content.Length.ToString(CultureInfo.InvariantCulture)} chars";
        return $"file_write path `{path}`, {contentLength}, overwrite {overwrite}";
    }

    private static string SummarizeDirectoryListArguments(JsonElement arguments)
    {
        string path = GetString(arguments, "path") ?? ".";
        string recursive = FormatBoolean(GetBoolean(arguments, "recursive"), defaultValue: "false");
        return $"directory_list path `{path}`, recursive {recursive}";
    }

    private static string SummarizeSearchArguments(
        JsonElement arguments,
        string toolName)
    {
        string query = GetString(arguments, "query") ?? "<missing>";
        string path = GetString(arguments, "path") ?? ".";
        string caseSensitive = FormatBoolean(GetBoolean(arguments, "caseSensitive"), defaultValue: "false");
        return $"{toolName} query `{query}`, path `{path}`, caseSensitive {caseSensitive}";
    }

    private static string SummarizeShellArguments(JsonElement arguments)
    {
        string command = GetString(arguments, "command") ?? "<missing>";
        string workingDirectory = GetString(arguments, "workingDirectory") ?? ".";
        return $"shell_command `{command}` in `{workingDirectory}`";
    }

    private static string? GetString(
        JsonElement arguments,
        string propertyName)
    {
        return arguments.TryGetProperty(propertyName, out JsonElement value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool? GetBoolean(
        JsonElement arguments,
        string propertyName)
    {
        return arguments.TryGetProperty(propertyName, out JsonElement value) &&
               value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
    }

    private static string FormatBoolean(
        bool? value,
        string defaultValue)
    {
        return value is null
            ? defaultValue
            : value.Value ? "true" : "false";
    }

    private static bool IsTrackableToolFailure(ToolInvocationResult invocationResult)
    {
        if (invocationResult.Result.IsSuccess ||
            invocationResult.Result.Status == ToolResultStatus.NotFound)
        {
            return false;
        }

        ToolErrorPayload? error = TryReadToolErrorPayload(invocationResult.Result, out ToolErrorPayload? payload)
            ? payload
            : null;
        string code = error?.Code ?? invocationResult.Result.Status.ToString();
        string message = error?.Message ?? invocationResult.Result.Message;

        if (IsMissingRequiredArgumentFailure(code, message) ||
            IsFileLocationMiss(invocationResult.ToolName, code, message) ||
            IsUserPermissionNoise(code))
        {
            return false;
        }

        return invocationResult.Result.Status is
            ToolResultStatus.InvalidArguments or
            ToolResultStatus.ExecutionError or
            ToolResultStatus.PermissionDenied;
    }

    private static bool IsMissingRequiredArgumentFailure(
        string code,
        string message)
    {
        return code.StartsWith("missing_", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("requires a non-empty", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFileLocationMiss(
        string toolName,
        string code,
        string message)
    {
        if (toolName is not (
                AgentToolNames.FileRead or
                AgentToolNames.FileDelete or
                AgentToolNames.DirectoryList or
                AgentToolNames.SearchFiles or
                AgentToolNames.TextSearch))
        {
            return false;
        }

        return code.Contains("not_found", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("cannot find path", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("could not find file", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("no such file", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUserPermissionNoise(string code)
    {
        return code.Equals("permission_denied_by_user", StringComparison.OrdinalIgnoreCase) ||
            code.Equals("permission_request_cancelled", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadToolErrorPayload(
        ToolResult result,
        out ToolErrorPayload? error)
    {
        try
        {
            error = JsonSerializer.Deserialize(
                result.JsonResult,
                ToolJsonContext.Default.ToolErrorPayload);
            return error is not null;
        }
        catch (JsonException)
        {
            error = null;
            return false;
        }
    }

    private async Task<LessonMemoryEntry[]> LoadAsync(CancellationToken cancellationToken)
    {
        string storagePath = GetStoragePath();
        if (!File.Exists(storagePath))
        {
            return [];
        }

        string[] lines = await File.ReadAllLinesAsync(storagePath, Encoding.UTF8, cancellationToken);
        Dictionary<string, LessonMemoryEntry> entries = new(StringComparer.Ordinal);

        foreach (string line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            LessonMemoryEntry? entry;
            try
            {
                entry = JsonSerializer.Deserialize(
                    line,
                    LessonMemoryJsonContext.Default.LessonMemoryEntry);
            }
            catch (JsonException)
            {
                continue;
            }

            if (entry is null || string.IsNullOrWhiteSpace(entry.Id))
            {
                continue;
            }

            entries[entry.Id.Trim()] = NormalizeEntry(entry);
        }

        return entries.Values
            .OrderBy(static entry => entry.CreatedAtUtc)
            .ToArray();
    }

    private async Task AppendAsync(
        LessonMemoryEntry entry,
        CancellationToken cancellationToken)
    {
        string storagePath = GetStoragePath();
        EnsureStorageDirectory(storagePath);

        if (_settings.MaxEntries > 0 && File.Exists(storagePath))
        {
            LessonMemoryEntry[] existingEntries = await LoadAsync(cancellationToken);
            if (existingEntries.Length + 1 > _settings.MaxEntries)
            {
                LessonMemoryEntry[] retained = existingEntries
                    .Skip(Math.Max(0, existingEntries.Length + 1 - _settings.MaxEntries))
                    .Append(entry)
                    .ToArray();
                await RewriteAsync(retained, cancellationToken);
                return;
            }
        }

        await using FileStream stream = new(
            storagePath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous);
        await using StreamWriter writer = new(stream, Utf8NoBom);
        string json = JsonSerializer.Serialize(
            entry,
            LessonMemoryJsonContext.Default.LessonMemoryEntry);
        await writer.WriteLineAsync(json.AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }

    private async Task RewriteAsync(
        IReadOnlyList<LessonMemoryEntry> entries,
        CancellationToken cancellationToken)
    {
        string storagePath = GetStoragePath();
        EnsureStorageDirectory(storagePath);
        LessonMemoryEntry[] retainedEntries = _settings.MaxEntries > 0 && entries.Count > _settings.MaxEntries
            ? entries
                .Skip(entries.Count - _settings.MaxEntries)
                .ToArray()
            : entries.ToArray();

        await using FileStream stream = new(
            storagePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous);
        await using StreamWriter writer = new(stream, Utf8NoBom);

        foreach (LessonMemoryEntry entry in retainedEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string json = JsonSerializer.Serialize(
                NormalizeEntry(entry),
                LessonMemoryJsonContext.Default.LessonMemoryEntry);
            await writer.WriteLineAsync(json.AsMemory(), cancellationToken);
        }

        await writer.FlushAsync(cancellationToken);
    }

    private static void EnsureStorageDirectory(string storagePath)
    {
        string? directoryPath = Path.GetDirectoryName(storagePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }
    }

    private static bool TryReadShellCommandResult(
        ToolInvocationResult invocationResult,
        out ShellCommandExecutionResult? result)
    {
        try
        {
            result = JsonSerializer.Deserialize(
                invocationResult.Result.JsonResult,
                ToolJsonContext.Default.ShellCommandExecutionResult);
            return result is not null;
        }
        catch (JsonException)
        {
            result = null;
            return false;
        }
    }

    private static int ScoreEntry(
        LessonMemoryEntry entry,
        IReadOnlyList<string> tokens,
        string query)
    {
        if (tokens.Count == 0)
        {
            return 1;
        }

        string haystack = CreateSearchHaystack(entry);
        string normalizedQuery = NormalizeSearchText(query);
        int score = string.IsNullOrWhiteSpace(normalizedQuery) ||
                    !haystack.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
            ? 0
            : 8;

        foreach (string token in tokens)
        {
            if (entry.Tags.Any(tag => string.Equals(tag, token, StringComparison.OrdinalIgnoreCase)))
            {
                score += 7;
            }

            if (ContainsToken(entry.Trigger, token))
            {
                score += 5;
            }

            if (ContainsToken(entry.Problem, token))
            {
                score += 4;
            }

            if (ContainsToken(entry.Lesson, token))
            {
                score += 3;
            }

            if (ContainsToken(entry.Command, token) ||
                ContainsToken(entry.FailureSignature, token) ||
                ContainsToken(entry.ToolName, token))
            {
                score += 3;
            }
        }

        if (string.Equals(entry.Kind, "lesson", StringComparison.OrdinalIgnoreCase))
        {
            score += 2;
        }

        if (!entry.IsFixed)
        {
            score += 1;
        }

        return score;
    }

    private static string CreateSearchHaystack(LessonMemoryEntry entry)
    {
        return NormalizeSearchText(string.Join(
            " ",
            [
                entry.Kind,
                entry.Trigger,
                entry.Problem,
                entry.Lesson,
                string.Join(" ", entry.Tags),
                entry.ToolName ?? string.Empty,
                entry.Command ?? string.Empty,
                entry.FailureSignature ?? string.Empty,
                entry.FixSummary ?? string.Empty
            ]));
    }

    private static bool ContainsToken(
        string? value,
        string token)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            NormalizeSearchText(value).Contains(token, StringComparison.OrdinalIgnoreCase);
    }

    private static string[] Tokenize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return SearchTokenRegex()
            .Matches(value)
            .Select(static match => match.Value.ToLowerInvariant())
            .Where(static token => token.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxSearchTokens)
            .ToArray();
    }

    private static string NormalizeSearchText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return NormalizeWhitespace(value).ToLowerInvariant();
    }

    private static LessonMemoryEntry NormalizeEntry(LessonMemoryEntry entry)
    {
        return entry with
        {
            Id = RequireText(entry.Id, nameof(entry.Id)),
            Kind = NormalizeKind(entry.Kind),
            Trigger = RequireText(entry.Trigger, nameof(entry.Trigger)),
            Problem = RequireText(entry.Problem, nameof(entry.Problem)),
            Lesson = RequireText(entry.Lesson, nameof(entry.Lesson)),
            Tags = NormalizeTags(entry.Tags),
            ToolName = NormalizeOptionalText(entry.ToolName),
            Command = NormalizeOptionalText(entry.Command),
            FailureSignature = NormalizeOptionalText(entry.FailureSignature),
            Fingerprint = NormalizeOptionalText(entry.Fingerprint),
            FixSummary = NormalizeOptionalText(entry.FixSummary)
        };
    }

    private string RedactIfNeeded(string value)
    {
        if (!_settings.RedactSecrets)
        {
            return value;
        }

        return SecretRedactor.Redact(value);
    }

    private string? RedactOptionalIfNeeded(string? value)
    {
        return value is null
            ? null
            : RedactIfNeeded(value);
    }

    private string TrimPrompt(string prompt)
    {
        if (_settings.MaxPromptChars <= 0 ||
            prompt.Length <= _settings.MaxPromptChars)
        {
            return prompt;
        }

        return prompt[..Math.Max(0, _settings.MaxPromptChars - 3)].TrimEnd() + "...";
    }

    private static MemorySettings NormalizeSettings(MemorySettings settings)
    {
        settings.MaxEntries = settings.MaxEntries <= 0
            ? 500
            : Math.Min(settings.MaxEntries, 10_000);
        settings.MaxPromptChars = settings.MaxPromptChars <= 0
            ? 12_000
            : Math.Min(settings.MaxPromptChars, 100_000);
        return settings;
    }

    private static string NormalizeKind(string? value)
    {
        string normalized = string.IsNullOrWhiteSpace(value)
            ? "lesson"
            : NormalizeWhitespace(value).ToLowerInvariant();

        return normalized is "lesson" or "failure"
            ? normalized
            : "lesson";
    }

    private static string[] NormalizeTags(IEnumerable<string>? tags)
    {
        return (tags ?? [])
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Select(static tag => NormalizeWhitespace(tag).ToLowerInvariant())
            .Where(static tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToArray();
    }

    private static string RequireText(
        string? value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value must not be empty.", parameterName);
        }

        return NormalizeWhitespace(value);
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : NormalizeWhitespace(value);
    }

    private static string NormalizeWhitespace(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : WhitespaceRegex()
            .Replace(value.Trim(), " ");
    }

    private static string TrimForPrompt(string value)
    {
        string normalized = NormalizeWhitespace(value);
        return normalized.Length <= MaxPromptFieldCharacters
            ? normalized
            : normalized[..Math.Max(0, MaxPromptFieldCharacters - 3)].TrimEnd() + "...";
    }

    private static int NormalizeLimit(int limit)
    {
        return limit <= 0
            ? 10
            : Math.Min(limit, 50);
    }

    private static string CreateId()
    {
        return $"les_{Guid.NewGuid():N}"[..16];
    }

    private static string CreateToolFingerprint(
        string toolName,
        string failureSignature)
    {
        return $"tool:{toolName.Trim().ToLowerInvariant()}:{NormalizeSearchText(failureSignature)}";
    }

    private static string CreateShellFingerprint(string command)
    {
        return $"shell:{NormalizeSearchText(command)}";
    }

    private static bool IsTrackableShellFailure(string command)
    {
        string normalized = NormalizeSearchText(command);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (normalized.Contains(" --version", StringComparison.Ordinal) ||
            normalized.Contains(" --help", StringComparison.Ordinal) ||
            normalized.Contains(" --info", StringComparison.Ordinal) ||
            normalized.EndsWith(" version", StringComparison.Ordinal) ||
            normalized.EndsWith(" help", StringComparison.Ordinal) ||
            normalized.EndsWith(" info", StringComparison.Ordinal))
        {
            return false;
        }

        string firstCommand = GetFirstCommandName(command);
        if (firstCommand is "rg" or "grep" or "find" or "findstr" or "where" or "which" or "select-string")
        {
            return false;
        }

        return normalized.Contains(" build", StringComparison.Ordinal) ||
            normalized.Contains(" test", StringComparison.Ordinal) ||
            normalized.Contains(" lint", StringComparison.Ordinal) ||
            normalized.Contains(" compile", StringComparison.Ordinal) ||
            normalized.Contains(" restore", StringComparison.Ordinal) ||
            normalized.Contains(" typecheck", StringComparison.Ordinal) ||
            normalized.Contains(" pytest", StringComparison.Ordinal) ||
            normalized.StartsWith("dotnet ", StringComparison.Ordinal) ||
            normalized.StartsWith("msbuild", StringComparison.Ordinal) ||
            normalized.StartsWith("tsc", StringComparison.Ordinal) ||
            normalized.StartsWith("pytest", StringComparison.Ordinal) ||
            normalized.StartsWith("cargo ", StringComparison.Ordinal) ||
            normalized.StartsWith("go test", StringComparison.Ordinal) ||
            normalized.StartsWith("mvn ", StringComparison.Ordinal) ||
            normalized.StartsWith("gradle ", StringComparison.Ordinal) ||
            normalized.StartsWith("make", StringComparison.Ordinal) ||
            normalized.StartsWith("npm ", StringComparison.Ordinal) ||
            normalized.StartsWith("pnpm ", StringComparison.Ordinal) ||
            normalized.StartsWith("yarn ", StringComparison.Ordinal) ||
            normalized.StartsWith("bun ", StringComparison.Ordinal) ||
            normalized.StartsWith("csc", StringComparison.Ordinal) ||
            normalized.StartsWith("javac", StringComparison.Ordinal) ||
            normalized.StartsWith("gcc", StringComparison.Ordinal) ||
            normalized.StartsWith("g++", StringComparison.Ordinal) ||
            normalized.StartsWith("clang", StringComparison.Ordinal) ||
            normalized.StartsWith("ruff ", StringComparison.Ordinal);
    }

    private static string DetectShellCategory(string command)
    {
        string normalized = NormalizeSearchText(command);
        if (normalized.Contains(" test", StringComparison.Ordinal) ||
            normalized.Contains(" pytest", StringComparison.Ordinal) ||
            normalized.StartsWith("pytest", StringComparison.Ordinal) ||
            normalized.StartsWith("go test", StringComparison.Ordinal))
        {
            return "test";
        }

        if (normalized.Contains(" lint", StringComparison.Ordinal) ||
            normalized.StartsWith("ruff ", StringComparison.Ordinal))
        {
            return "lint";
        }

        if (normalized.Contains(" restore", StringComparison.Ordinal))
        {
            return "restore";
        }

        return "build";
    }

    private static string GetFirstCommandName(string command)
    {
        string[] tokens = command
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length == 0)
        {
            return "shell";
        }

        string commandName = tokens[0]
            .Trim('"', '\'')
            .Replace('\\', '/');
        int slashIndex = commandName.LastIndexOf('/');
        if (slashIndex >= 0 && slashIndex < commandName.Length - 1)
        {
            commandName = commandName[(slashIndex + 1)..];
        }

        return string.IsNullOrWhiteSpace(commandName)
            ? "shell"
            : commandName.ToLowerInvariant();
    }

    private static string? ExtractFailureSignature(ShellCommandExecutionResult result)
    {
        string output = $"{result.StandardError}{Environment.NewLine}{result.StandardOutput}";
        Match diagnosticCode = DiagnosticCodeRegex().Match(output);
        if (diagnosticCode.Success)
        {
            return diagnosticCode.Value.ToUpperInvariant();
        }

        string? line = output
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(static candidate =>
                candidate.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("exception", StringComparison.OrdinalIgnoreCase));

        return line is null
            ? null
            : TrimForPrompt(line);
    }

    private sealed record PendingFailureObservation(
        string Fingerprint,
        DateTimeOffset ObservedAtUtc,
        string ToolName,
        string Trigger,
        string Problem,
        string AttemptSummary,
        string? Command,
        string? FailureSignature,
        string[] Tags);

    [GeneratedRegex(@"\b(?:CS|TS|MSB|NU|NETSDK|CA|IDE|BC|FS)\d{3,6}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DiagnosticCodeRegex();

    [GeneratedRegex(@"[A-Za-z0-9_+\-.#]+", RegexOptions.CultureInvariant)]
    private static partial Regex SearchTokenRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
