using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using System.Text;

namespace NanoAgent.Application.Commands;

internal sealed class LessonsCommandHandler : IReplCommandHandler
{
    private const int DefaultListLimit = 10;
    private readonly ILessonMemoryService _lessonMemoryService;
    private readonly MemorySettings _memorySettings;
    private readonly IWorkspaceSettingsWriter _workspaceSettingsWriter;

    public LessonsCommandHandler(
        ILessonMemoryService lessonMemoryService,
        MemorySettings memorySettings,
        IWorkspaceSettingsWriter workspaceSettingsWriter)
    {
        _lessonMemoryService = lessonMemoryService;
        _memorySettings = memorySettings;
        _workspaceSettingsWriter = workspaceSettingsWriter;
    }

    public string CommandName => "lessons";

    public string Description => "Manage local lesson memory from the shell. Lesson memory is command-only and off by default.";

    public string Usage => "/lessons [status|on|off|list [limit]|search <query>|save <trigger> | <problem> | <lesson>|edit <id> <trigger> | <problem> | <lesson>|delete <id>]";

    public async Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (context.Arguments.Count == 0)
        {
            return ReplCommandResult.Continue(BuildStatusMessage(), ReplFeedbackKind.Info);
        }

        return context.Arguments[0].Trim().ToLowerInvariant() switch
        {
            "status" => ReplCommandResult.Continue(BuildStatusMessage(), ReplFeedbackKind.Info),
            "help" or "-h" or "--help" => ReplCommandResult.Continue(Usage, ReplFeedbackKind.Info),
            "on" or "enable" => await SetEnabledAsync(context, enabled: true, cancellationToken),
            "off" or "disable" => await SetEnabledAsync(context, enabled: false, cancellationToken),
            "list" => await ListAsync(context, cancellationToken),
            "search" => await SearchAsync(context, cancellationToken),
            "save" or "add" => await SaveAsync(context, cancellationToken),
            "edit" => await EditAsync(context, cancellationToken),
            "delete" or "remove" => await DeleteAsync(context, cancellationToken),
            _ => ReplCommandResult.Continue(
                $"Unknown lessons action '{context.Arguments[0]}'.\nUsage: {Usage}",
                ReplFeedbackKind.Error)
        };
    }

    private async Task<ReplCommandResult> SetEnabledAsync(
        ReplCommandContext context,
        bool enabled,
        CancellationToken cancellationToken)
    {
        if (context.Arguments.Count != 1)
        {
            return ReplCommandResult.Continue(
                enabled ? "Usage: /lessons on" : "Usage: /lessons off",
                ReplFeedbackKind.Error);
        }

        MemorySettings updated = CloneSettings();
        updated.LessonsEnabled = enabled;
        updated.AllowAutoFailureObservation = false;

        await _workspaceSettingsWriter.SaveMemorySettingsAsync(
            context.Session.WorkspacePath,
            updated,
            cancellationToken);
        ApplySettings(updated);

        return ReplCommandResult.Continue(
            enabled
                ? "Lesson memory enabled for this workspace. It remains command-only and will not be injected automatically into prompts."
                : "Lesson memory disabled for this workspace.",
            ReplFeedbackKind.Info);
    }

    private async Task<ReplCommandResult> ListAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ReplCommandResult? disabledResult = EnsureLessonsEnabled();
        if (disabledResult is not null)
        {
            return disabledResult;
        }

        if (context.Arguments.Count > 2 ||
            (context.Arguments.Count == 2 && !int.TryParse(context.Arguments[1], out _)))
        {
            return ReplCommandResult.Continue(
                "Usage: /lessons list [limit]",
                ReplFeedbackKind.Error);
        }

        int limit = context.Arguments.Count == 2
            ? int.Parse(context.Arguments[1])
            : DefaultListLimit;
        IReadOnlyList<LessonMemoryEntry> lessons = await _lessonMemoryService.ListAsync(
            limit,
            includeFixed: true,
            cancellationToken);

        return ReplCommandResult.Continue(
            $"Lesson memory ({lessons.Count}):\n{FormatLessons(lessons)}",
            ReplFeedbackKind.Info);
    }

    private async Task<ReplCommandResult> SearchAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ReplCommandResult? disabledResult = EnsureLessonsEnabled();
        if (disabledResult is not null)
        {
            return disabledResult;
        }

        string query = GetRemainderAfterFirstArgument(context.ArgumentText);
        if (string.IsNullOrWhiteSpace(query))
        {
            return ReplCommandResult.Continue(
                "Usage: /lessons search <query>",
                ReplFeedbackKind.Error);
        }

        IReadOnlyList<LessonMemoryEntry> lessons = await _lessonMemoryService.SearchAsync(
            query,
            DefaultListLimit,
            includeFixed: true,
            cancellationToken);

        return ReplCommandResult.Continue(
            $"Lesson search: {query}\n{FormatLessons(lessons)}",
            ReplFeedbackKind.Info);
    }

    private async Task<ReplCommandResult> SaveAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ReplCommandResult? disabledResult = EnsureLessonsEnabled();
        if (disabledResult is not null)
        {
            return disabledResult;
        }

        string payload = GetRemainderAfterFirstArgument(context.ArgumentText);
        if (!TryParseLessonFields(payload, out string? trigger, out string? problem, out string? lesson))
        {
            return ReplCommandResult.Continue(
                "Usage: /lessons save <trigger> | <problem> | <lesson>",
                ReplFeedbackKind.Error);
        }

        LessonMemoryEntry entry = await _lessonMemoryService.SaveAsync(
            new LessonMemorySaveRequest(trigger!, problem!, lesson!),
            cancellationToken);

        return ReplCommandResult.Continue(
            $"Saved lesson '{entry.Id}'.\n{FormatLessons([entry])}",
            ReplFeedbackKind.Info);
    }

    private async Task<ReplCommandResult> EditAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ReplCommandResult? disabledResult = EnsureLessonsEnabled();
        if (disabledResult is not null)
        {
            return disabledResult;
        }

        string payload = GetRemainderAfterFirstArgument(context.ArgumentText);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return ReplCommandResult.Continue(
                "Usage: /lessons edit <id> <trigger> | <problem> | <lesson>",
                ReplFeedbackKind.Error);
        }

        int separatorIndex = payload.IndexOf(' ');
        if (separatorIndex <= 0)
        {
            return ReplCommandResult.Continue(
                "Usage: /lessons edit <id> <trigger> | <problem> | <lesson>",
                ReplFeedbackKind.Error);
        }

        string id = payload[..separatorIndex].Trim();
        string fieldPayload = payload[(separatorIndex + 1)..].Trim();
        if (!TryParseLessonFields(fieldPayload, out string? trigger, out string? problem, out string? lesson))
        {
            return ReplCommandResult.Continue(
                "Usage: /lessons edit <id> <trigger> | <problem> | <lesson>",
                ReplFeedbackKind.Error);
        }

        LessonMemoryEntry? entry = await _lessonMemoryService.EditAsync(
            new LessonMemoryEditRequest(id, trigger, problem, lesson),
            cancellationToken);
        if (entry is null)
        {
            return ReplCommandResult.Continue(
                $"Lesson '{id}' was not found.",
                ReplFeedbackKind.Error);
        }

        return ReplCommandResult.Continue(
            $"Edited lesson '{entry.Id}'.\n{FormatLessons([entry])}",
            ReplFeedbackKind.Info);
    }

    private async Task<ReplCommandResult> DeleteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ReplCommandResult? disabledResult = EnsureLessonsEnabled();
        if (disabledResult is not null)
        {
            return disabledResult;
        }

        if (context.Arguments.Count != 2 || string.IsNullOrWhiteSpace(context.Arguments[1]))
        {
            return ReplCommandResult.Continue(
                "Usage: /lessons delete <id>",
                ReplFeedbackKind.Error);
        }

        string id = context.Arguments[1].Trim();
        bool deleted = await _lessonMemoryService.DeleteAsync(id, cancellationToken);
        if (!deleted)
        {
            return ReplCommandResult.Continue(
                $"Lesson '{id}' was not found.",
                ReplFeedbackKind.Error);
        }

        return ReplCommandResult.Continue(
            $"Deleted lesson '{id}'.",
            ReplFeedbackKind.Info);
    }

    private ReplCommandResult? EnsureLessonsEnabled()
    {
        return _memorySettings.LessonsEnabled
            ? null
            : ReplCommandResult.Continue(
                "Lesson memory is off for this workspace. Use /lessons on to enable it.",
                ReplFeedbackKind.Warning);
    }

    private string BuildStatusMessage()
    {
        StringBuilder builder = new();
        builder.AppendLine("Lesson memory");
        builder.AppendLine($"Status: {(_memorySettings.LessonsEnabled ? "on" : "off")}");
        builder.AppendLine("Mode: command-only");
        builder.AppendLine($"Storage: {_lessonMemoryService.GetStoragePath()}");
        builder.AppendLine("Automatic prompt injection: off");
        builder.Append("Usage: ").Append(Usage);
        return builder.ToString();
    }

    private MemorySettings CloneSettings()
    {
        return new MemorySettings
        {
            AllowAutoFailureObservation = _memorySettings.AllowAutoFailureObservation,
            AllowAutoManualLessons = _memorySettings.AllowAutoManualLessons,
            Disabled = _memorySettings.Disabled,
            LessonsEnabled = _memorySettings.LessonsEnabled,
            MaxEntries = _memorySettings.MaxEntries,
            MaxPromptChars = _memorySettings.MaxPromptChars,
            RedactSecrets = _memorySettings.RedactSecrets,
            RequireApprovalForWrites = _memorySettings.RequireApprovalForWrites
        };
    }

    private void ApplySettings(MemorySettings settings)
    {
        _memorySettings.AllowAutoFailureObservation = settings.AllowAutoFailureObservation;
        _memorySettings.AllowAutoManualLessons = settings.AllowAutoManualLessons;
        _memorySettings.Disabled = settings.Disabled;
        _memorySettings.LessonsEnabled = settings.LessonsEnabled;
        _memorySettings.MaxEntries = settings.MaxEntries;
        _memorySettings.MaxPromptChars = settings.MaxPromptChars;
        _memorySettings.RedactSecrets = settings.RedactSecrets;
        _memorySettings.RequireApprovalForWrites = settings.RequireApprovalForWrites;
    }

    private static string GetRemainderAfterFirstArgument(string argumentText)
    {
        int separatorIndex = argumentText.IndexOf(' ');
        return separatorIndex < 0
            ? string.Empty
            : argumentText[(separatorIndex + 1)..].Trim();
    }

    private static bool TryParseLessonFields(
        string payload,
        out string? trigger,
        out string? problem,
        out string? lesson)
    {
        trigger = null;
        problem = null;
        lesson = null;

        string[] parts = payload
            .Split('|', StringSplitOptions.TrimEntries)
            .Where(static part => !string.IsNullOrWhiteSpace(part))
            .ToArray();
        if (parts.Length != 3)
        {
            return false;
        }

        trigger = parts[0];
        problem = parts[1];
        lesson = parts[2];
        return true;
    }

    private static string FormatLessons(IReadOnlyList<LessonMemoryEntry> lessons)
    {
        if (lessons.Count == 0)
        {
            return "No lessons found.";
        }

        return string.Join(
            Environment.NewLine,
            lessons.Select(static lesson =>
            {
                string status = lesson.IsFixed ? "fixed" : "active";
                return $"- {lesson.Id} [{status}] Trigger: {lesson.Trigger} Problem: {lesson.Problem} Lesson: {lesson.Lesson}";
            }));
    }
}
