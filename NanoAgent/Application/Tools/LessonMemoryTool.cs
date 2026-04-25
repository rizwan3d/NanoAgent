using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;

namespace NanoAgent.Application.Tools;

internal sealed class LessonMemoryTool : ITool
{
    private readonly ILessonMemoryService _lessonMemoryService;

    public LessonMemoryTool(ILessonMemoryService lessonMemoryService)
    {
        _lessonMemoryService = lessonMemoryService;
    }

    public string Description => "Save, search, list, edit, or delete persistent workspace lessons about mistakes, failures, and fixes. Lessons are stored in .nanoagent/memory/lessons.jsonl and are also retrieved automatically for relevant future prompts.";

    public string Name => AgentToolNames.LessonMemory;

    public string PermissionRequirements => """
        {
          "approvalMode": "Automatic",
          "toolTags": ["memory"]
        }
        """;

    public string Schema => """
        {
          "type": "object",
          "properties": {
            "action": {
              "type": "string",
              "enum": ["save", "search", "list", "edit", "delete"],
              "description": "Memory operation to run."
            },
            "id": {
              "type": "string",
              "description": "Lesson id for edit or delete."
            },
            "query": {
              "type": "string",
              "description": "Search query for relevant lessons."
            },
            "trigger": {
              "type": "string",
              "description": "Short symptom or situation that should retrieve this lesson, such as an error code or failing command."
            },
            "problem": {
              "type": "string",
              "description": "Mistake, failure, or root cause the lesson is about."
            },
            "lesson": {
              "type": "string",
              "description": "Concrete future guidance to avoid or fix the mistake."
            },
            "tags": {
              "type": "array",
              "items": { "type": "string" },
              "description": "Optional retrieval tags such as build, test, csharp, dependency-injection, npm, or error codes."
            },
            "limit": {
              "type": "integer",
              "description": "Maximum lessons to return for search or list. Defaults to 10."
            },
            "includeFixed": {
              "type": "boolean",
              "description": "Whether search/list should include failures that were later marked fixed. Defaults to true."
            },
            "isFixed": {
              "type": "boolean",
              "description": "Optional fixed state when saving or editing a failure observation."
            },
            "fixSummary": {
              "type": "string",
              "description": "Optional summary of what fixed the failure."
            }
          },
          "required": ["action"],
          "additionalProperties": false
        }
        """;

    public async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!ToolArguments.TryGetNonEmptyString(context.Arguments, "action", out string? action))
        {
            return InvalidArguments(
                "missing_action",
                "Tool 'lesson_memory' requires an action: save, search, list, edit, or delete.");
        }

        return action!.ToLowerInvariant() switch
        {
            "save" => await SaveAsync(context, cancellationToken),
            "search" => await SearchAsync(context, cancellationToken),
            "list" => await ListAsync(context, cancellationToken),
            "edit" => await EditAsync(context, cancellationToken),
            "delete" => await DeleteAsync(context, cancellationToken),
            _ => InvalidArguments(
                "invalid_action",
                $"Tool 'lesson_memory' received unsupported action '{action}'.")
        };
    }

    private async Task<ToolResult> SaveAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!TryGetLessonFields(context, requireAll: true, out string? trigger, out string? problem, out string? lesson, out ToolResult? invalidResult))
        {
            return invalidResult!;
        }

        LessonMemoryEntry entry = await _lessonMemoryService.SaveAsync(
            new LessonMemorySaveRequest(
                trigger!,
                problem!,
                lesson!,
                ToolArguments.GetOptionalStringArray(context.Arguments, "tags"),
                IsFixed: ToolArguments.GetBoolean(context.Arguments, "isFixed"),
                FixSummary: ToolArguments.GetOptionalString(context.Arguments, "fixSummary")),
            cancellationToken);

        return Success(
            "save",
            $"Saved lesson '{entry.Id}'.",
            [entry],
            $"Saved lesson: {entry.Id}",
            FormatLessons([entry]));
    }

    private async Task<ToolResult> SearchAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!ToolArguments.TryGetNonEmptyString(context.Arguments, "query", out string? query))
        {
            return InvalidArguments(
                "missing_query",
                "Tool 'lesson_memory' search requires a non-empty 'query' string.");
        }

        IReadOnlyList<LessonMemoryEntry> lessons = await _lessonMemoryService.SearchAsync(
            query!,
            GetLimit(context),
            GetIncludeFixed(context),
            cancellationToken);

        return Success(
            "search",
            $"Found {lessons.Count} relevant {(lessons.Count == 1 ? "lesson" : "lessons")}.",
            lessons,
            $"Lesson search: {query}",
            FormatLessons(lessons));
    }

    private async Task<ToolResult> ListAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<LessonMemoryEntry> lessons = await _lessonMemoryService.ListAsync(
            GetLimit(context),
            GetIncludeFixed(context),
            cancellationToken);

        return Success(
            "list",
            $"Listed {lessons.Count} {(lessons.Count == 1 ? "lesson" : "lessons")}.",
            lessons,
            "Lesson memory",
            FormatLessons(lessons));
    }

    private async Task<ToolResult> EditAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!ToolArguments.TryGetNonEmptyString(context.Arguments, "id", out string? id))
        {
            return InvalidArguments(
                "missing_id",
                "Tool 'lesson_memory' edit requires a non-empty 'id' string.");
        }

        if (!TryGetLessonFields(context, requireAll: false, out string? trigger, out string? problem, out string? lesson, out ToolResult? invalidResult))
        {
            return invalidResult!;
        }

        bool? isFixed = ToolArguments.TryGetBoolean(context.Arguments, "isFixed", out bool fixedValue)
            ? fixedValue
            : null;

        LessonMemoryEntry? entry = await _lessonMemoryService.EditAsync(
            new LessonMemoryEditRequest(
                id!,
                trigger,
                problem,
                lesson,
                context.Arguments.TryGetProperty("tags", out _) ? ToolArguments.GetOptionalStringArray(context.Arguments, "tags") : null,
                IsFixed: isFixed,
                FixSummary: ToolArguments.GetOptionalString(context.Arguments, "fixSummary")),
            cancellationToken);

        if (entry is null)
        {
            return ToolResultFactory.NotFound(
                "lesson_not_found",
                $"Lesson '{id}' was not found.",
                new ToolRenderPayload(
                    "Lesson not found",
                    $"No lesson with id '{id}' exists in {_lessonMemoryService.GetStoragePath()}."));
        }

        return Success(
            "edit",
            $"Edited lesson '{entry.Id}'.",
            [entry],
            $"Edited lesson: {entry.Id}",
            FormatLessons([entry]));
    }

    private async Task<ToolResult> DeleteAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!ToolArguments.TryGetNonEmptyString(context.Arguments, "id", out string? id))
        {
            return InvalidArguments(
                "missing_id",
                "Tool 'lesson_memory' delete requires a non-empty 'id' string.");
        }

        bool deleted = await _lessonMemoryService.DeleteAsync(id!, cancellationToken);
        if (!deleted)
        {
            return ToolResultFactory.NotFound(
                "lesson_not_found",
                $"Lesson '{id}' was not found.",
                new ToolRenderPayload(
                    "Lesson not found",
                    $"No lesson with id '{id}' exists in {_lessonMemoryService.GetStoragePath()}."));
        }

        return Success(
            "delete",
            $"Deleted lesson '{id}'.",
            [],
            "Lesson deleted",
            $"Deleted lesson '{id}' from {_lessonMemoryService.GetStoragePath()}.");
    }

    private static bool TryGetLessonFields(
        ToolExecutionContext context,
        bool requireAll,
        out string? trigger,
        out string? problem,
        out string? lesson,
        out ToolResult? invalidResult)
    {
        trigger = ToolArguments.GetOptionalString(context.Arguments, "trigger");
        problem = ToolArguments.GetOptionalString(context.Arguments, "problem");
        lesson = ToolArguments.GetOptionalString(context.Arguments, "lesson");
        invalidResult = null;

        if (!requireAll)
        {
            if (context.Arguments.TryGetProperty("trigger", out _) && string.IsNullOrWhiteSpace(trigger))
            {
                invalidResult = InvalidArguments(
                    "empty_trigger",
                    "Tool 'lesson_memory' edit received an empty 'trigger' string.");
                return false;
            }

            if (context.Arguments.TryGetProperty("problem", out _) && string.IsNullOrWhiteSpace(problem))
            {
                invalidResult = InvalidArguments(
                    "empty_problem",
                    "Tool 'lesson_memory' edit received an empty 'problem' string.");
                return false;
            }

            if (context.Arguments.TryGetProperty("lesson", out _) && string.IsNullOrWhiteSpace(lesson))
            {
                invalidResult = InvalidArguments(
                    "empty_lesson",
                    "Tool 'lesson_memory' edit received an empty 'lesson' string.");
                return false;
            }

            return true;
        }

        if (string.IsNullOrWhiteSpace(trigger))
        {
            invalidResult = InvalidArguments(
                "missing_trigger",
                "Tool 'lesson_memory' save requires a non-empty 'trigger' string.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(problem))
        {
            invalidResult = InvalidArguments(
                "missing_problem",
                "Tool 'lesson_memory' save requires a non-empty 'problem' string.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(lesson))
        {
            invalidResult = InvalidArguments(
                "missing_lesson",
                "Tool 'lesson_memory' save requires a non-empty 'lesson' string.");
            return false;
        }

        return true;
    }

    private static int GetLimit(ToolExecutionContext context)
    {
        return ToolArguments.TryGetInt32(context.Arguments, "limit", out int limit)
            ? limit
            : 10;
    }

    private static bool GetIncludeFixed(ToolExecutionContext context)
    {
        return ToolArguments.GetBoolean(context.Arguments, "includeFixed", defaultValue: true);
    }

    private ToolResult Success(
        string action,
        string message,
        IReadOnlyList<LessonMemoryEntry> lessons,
        string renderTitle,
        string renderText)
    {
        LessonMemoryToolResult result = new(
            action,
            message,
            _lessonMemoryService.GetStoragePath(),
            lessons,
            lessons.Count);

        return ToolResultFactory.Success(
            message,
            result,
            ToolJsonContext.Default.LessonMemoryToolResult,
            new ToolRenderPayload(
                renderTitle,
                renderText));
    }

    private static ToolResult InvalidArguments(
        string code,
        string message)
    {
        return ToolResultFactory.InvalidArguments(
            code,
            message,
            new ToolRenderPayload(
                "Invalid lesson_memory arguments",
                message));
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
                string tags = lesson.Tags.Length == 0
                    ? "no tags"
                    : string.Join(", ", lesson.Tags);
                string status = lesson.IsFixed ? "fixed" : "active";
                return $"- {lesson.Id} [{lesson.Kind}, {status}, {tags}]: {lesson.Lesson}";
            }));
    }
}
