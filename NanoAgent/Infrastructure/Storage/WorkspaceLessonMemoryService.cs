using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;

namespace NanoAgent.Infrastructure.Storage;

internal sealed partial class WorkspaceLessonMemoryService : ILessonMemoryService
{
    private const int DefaultPromptLessonLimit = 5;
    private const int MaxPromptFieldCharacters = 260;
    private const int MaxSearchTokens = 24;
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _timeProvider;
    private readonly IWorkspaceRootProvider _workspaceRootProvider;

    public WorkspaceLessonMemoryService(
        IWorkspaceRootProvider workspaceRootProvider,
        TimeProvider timeProvider)
    {
        _workspaceRootProvider = workspaceRootProvider;
        _timeProvider = timeProvider;
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

        DateTimeOffset now = _timeProvider.GetUtcNow();
        LessonMemoryEntry entry = NormalizeEntry(new LessonMemoryEntry(
            CreateId(),
            now,
            now,
            NormalizeKind(request.Kind),
            RequireText(request.Trigger, nameof(request.Trigger)),
            RequireText(request.Problem, nameof(request.Problem)),
            RequireText(request.Lesson, nameof(request.Lesson)),
            NormalizeTags(request.Tags),
            NormalizeOptionalText(request.ToolName),
            NormalizeOptionalText(request.Command),
            NormalizeOptionalText(request.FailureSignature),
            NormalizeOptionalText(request.Fingerprint),
            request.IsFixed,
            request.IsFixed ? now : null,
            NormalizeOptionalText(request.FixSummary)));

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
                Trigger = request.Trigger is null ? current.Trigger : RequireText(request.Trigger, nameof(request.Trigger)),
                Problem = request.Problem is null ? current.Problem : RequireText(request.Problem, nameof(request.Problem)),
                Lesson = request.Lesson is null ? current.Lesson : RequireText(request.Lesson, nameof(request.Lesson)),
                Tags = request.Tags is null ? current.Tags : NormalizeTags(request.Tags),
                ToolName = request.ToolName is null ? current.ToolName : NormalizeOptionalText(request.ToolName),
                Command = request.Command is null ? current.Command : NormalizeOptionalText(request.Command),
                FailureSignature = request.FailureSignature is null ? current.FailureSignature : NormalizeOptionalText(request.FailureSignature),
                Fingerprint = request.Fingerprint is null ? current.Fingerprint : NormalizeOptionalText(request.Fingerprint),
                IsFixed = isFixed,
                FixedAtUtc = fixedAt,
                FixSummary = request.FixSummary is null ? current.FixSummary : NormalizeOptionalText(request.FixSummary)
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

        return builder.ToString().Trim();
    }

    public async Task ObserveToolResultAsync(
        ToolInvocationResult invocationResult,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocationResult);
        cancellationToken.ThrowIfCancellationRequested();

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

        string fingerprint = CreateToolFingerprint(invocationResult.ToolName);
        if (invocationResult.Result.IsSuccess)
        {
            await MarkFixedByFingerprintAsync(
                fingerprint,
                $"Tool '{invocationResult.ToolName}' later completed successfully.",
                cancellationToken);
            return;
        }

        await SaveOrUpdateFailureAsync(
            fingerprint,
            trigger: $"{invocationResult.ToolName} {invocationResult.Result.Status}",
            problem: $"Tool '{invocationResult.ToolName}' failed with status {invocationResult.Result.Status}. {invocationResult.Result.Message}",
            lesson: "Previous tool failure observed automatically. Check the arguments, permissions, and current state before retrying the same tool pattern.",
            tags: ["auto", "failure", "tool", invocationResult.ToolName],
            toolName: invocationResult.ToolName,
            command: null,
            failureSignature: invocationResult.Result.Status.ToString(),
            cancellationToken);
    }

    private async Task ObserveShellCommandAsync(
        ShellCommandExecutionResult result,
        CancellationToken cancellationToken)
    {
        string fingerprint = CreateShellFingerprint(result.Command);

        if (result.ExitCode == 0)
        {
            await MarkFixedByFingerprintAsync(
                fingerprint,
                $"Command `{NormalizeWhitespace(result.Command)}` later exited 0.",
                cancellationToken);
            return;
        }

        if (!IsTrackableShellFailure(result.Command))
        {
            return;
        }

        string signature = ExtractFailureSignature(result) ?? $"exit {result.ExitCode.ToString(CultureInfo.InvariantCulture)}";
        string category = DetectShellCategory(result.Command);
        string command = NormalizeWhitespace(result.Command);
        await SaveOrUpdateFailureAsync(
            fingerprint,
            trigger: $"{command} -> {signature}",
            problem: $"Command `{command}` failed with exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}. Signature: {signature}.",
            lesson: "Build/test/toolchain failure observed automatically. Check this failure signature and the matching command before retrying or changing unrelated code.",
            tags: ["auto", "failure", category, GetFirstCommandName(result.Command), signature],
            toolName: AgentToolNames.ShellCommand,
            command: command,
            failureSignature: signature,
            cancellationToken);
    }

    private async Task SaveOrUpdateFailureAsync(
        string fingerprint,
        string trigger,
        string problem,
        string lesson,
        IReadOnlyList<string> tags,
        string toolName,
        string? command,
        string? failureSignature,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            LessonMemoryEntry[] entries = await LoadAsync(cancellationToken);
            int index = Array.FindLastIndex(entries, entry =>
                !entry.IsFixed &&
                string.Equals(entry.Kind, "failure", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.Fingerprint, fingerprint, StringComparison.Ordinal));

            if (index >= 0)
            {
                entries[index] = NormalizeEntry(entries[index] with
                {
                    UpdatedAtUtc = now,
                    Trigger = trigger,
                    Problem = problem,
                    Lesson = lesson,
                    Tags = NormalizeTags(entries[index].Tags.Concat(tags)),
                    ToolName = toolName,
                    Command = NormalizeOptionalText(command),
                    FailureSignature = NormalizeOptionalText(failureSignature),
                    Fingerprint = fingerprint
                });
                await RewriteAsync(entries, cancellationToken);
                return;
            }

            LessonMemoryEntry entry = NormalizeEntry(new LessonMemoryEntry(
                CreateId(),
                now,
                now,
                "failure",
                trigger,
                problem,
                lesson,
                NormalizeTags(tags),
                toolName,
                NormalizeOptionalText(command),
                NormalizeOptionalText(failureSignature),
                fingerprint));

            await AppendAsync(entry, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task MarkFixedByFingerprintAsync(
        string fingerprint,
        string fixSummary,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            LessonMemoryEntry[] entries = await LoadAsync(cancellationToken);
            bool changed = false;

            for (int index = 0; index < entries.Length; index++)
            {
                LessonMemoryEntry entry = entries[index];
                if (entry.IsFixed ||
                    !string.Equals(entry.Kind, "failure", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(entry.Fingerprint, fingerprint, StringComparison.Ordinal))
                {
                    continue;
                }

                entries[index] = NormalizeEntry(entry with
                {
                    UpdatedAtUtc = now,
                    IsFixed = true,
                    FixedAtUtc = now,
                    FixSummary = fixSummary,
                    Tags = NormalizeTags(entry.Tags.Concat(["fixed"]))
                });
                changed = true;
            }

            if (changed)
            {
                await RewriteAsync(entries, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
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

        await using FileStream stream = new(
            storagePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous);
        await using StreamWriter writer = new(stream, Utf8NoBom);

        foreach (LessonMemoryEntry entry in entries)
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
        return WhitespaceRegex()
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

    private static string CreateToolFingerprint(string toolName)
    {
        return $"tool:{toolName.Trim().ToLowerInvariant()}";
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

    [GeneratedRegex(@"\b(?:CS|TS|MSB|NU|NETSDK|CA|IDE|BC|FS)\d{3,6}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DiagnosticCodeRegex();

    [GeneratedRegex(@"[A-Za-z0-9_+\-.#]+", RegexOptions.CultureInvariant)]
    private static partial Regex SearchTokenRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
