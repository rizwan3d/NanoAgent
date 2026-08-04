using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;
using System.Text.Json;

namespace NanoAgent.Application.Tools;

internal sealed class SearchAndReplaceTool(IWorkspaceFileService workspaceFileService) : ITool
{
    public string Description => "Find and replace literal text or regex matches across a single file in the current session working directory.";

    public string Name => AgentToolNames.SearchAndReplace;

    public string PermissionRequirements => """
        {
          "approvalMode": "Automatic",
          "toolTags": ["edit"],
          "filePaths": [
            {
              "argumentName": "path",
              "kind": "Write",
              "allowedRoots": ["."]
            }
          ]
        }
        """;

    public string Schema => """
        {
          "type": "object",
          "properties": {
            "path": {
              "type": "string",
              "description": "Path to the file, relative to the current session working directory."
            },
            "search": {
              "type": "string",
              "description": "Literal text or regex pattern to search for."
            },
            "replace": {
              "type": "string",
              "description": "Replacement text. Regex mode supports .NET replacement syntax like $1."
            },
            "useRegex": {
              "type": "boolean",
              "description": "Whether 'search' should be treated as a regex pattern. Defaults to false."
            },
            "caseSensitive": {
              "type": "boolean",
              "description": "Whether matching should be case-sensitive. Defaults to true."
            }
          },
          "required": ["path", "search", "replace"],
          "additionalProperties": false
        }
        """;

    public async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!ToolArguments.TryGetNonEmptyString(context.Arguments, "path", out string? path))
        {
            return ToolResultFactory.InvalidArguments(
                "missing_path",
                "Tool 'search_and_replace' requires a non-empty 'path' string.",
                new ToolRenderPayload(
                    "Invalid search_and_replace arguments",
                    "Provide a non-empty 'path' string."));
        }

        if (!ToolArguments.TryGetNonEmptyString(context.Arguments, "search", out string? search, trim: false))
        {
            return ToolResultFactory.InvalidArguments(
                "missing_search",
                "Tool 'search_and_replace' requires a non-empty 'search' string.",
                new ToolRenderPayload(
                    "Invalid search_and_replace arguments",
                    "Provide a non-empty 'search' string."));
        }

        if (!ToolArguments.TryGetString(context.Arguments, "replace", out string? replace, trim: false))
        {
            return ToolResultFactory.InvalidArguments(
                "missing_replace",
                "Tool 'search_and_replace' requires a 'replace' string.",
                new ToolRenderPayload(
                    "Invalid search_and_replace arguments",
                    "Provide a 'replace' string."));
        }

        string safePath = context.Session.ResolvePathFromWorkingDirectory(path!);
        bool useRegex = ToolArguments.GetBoolean(context.Arguments, "useRegex", defaultValue: false);
        bool caseSensitive = ToolArguments.GetBoolean(context.Arguments, "caseSensitive", defaultValue: true);

        try
        {
            WorkspaceSearchAndReplaceExecutionResult executionResult = await workspaceFileService.SearchAndReplaceWithTrackingAsync(
                safePath,
                search!,
                replace!,
                useRegex,
                caseSensitive,
                cancellationToken);

            if (executionResult.EditTransaction is not null)
            {
                context.Session.RecordFileEditTransaction(executionResult.EditTransaction);
            }

            WorkspaceSearchAndReplaceResult result = executionResult.Result;
            if (result.ReplacementCount == 0)
            {
                return ToolResult.NotFound(
                    $"No matches found in '{result.Path}'.",
                    JsonSerializer.Serialize(
                        result,
                        ToolJsonContext.Default.WorkspaceSearchAndReplaceResult),
                    new ToolRenderPayload(
                        $"No replacements made: {result.Path}",
                        $"No matches were found for the requested {(result.UseRegex ? "regex" : "text")} pattern in {result.Path}."));
            }

            SessionStateToolRecorder.RecordSearchAndReplace(context.Session, result);

            return ToolResultFactory.Success(
                $"Replaced {result.ReplacementCount} {(result.ReplacementCount == 1 ? "match" : "matches")} in '{result.Path}'.",
                result,
                ToolJsonContext.Default.WorkspaceSearchAndReplaceResult,
                new ToolRenderPayload(
                    $"Replacements applied: {result.Path}",
                    $"Replaced {result.ReplacementCount} {(result.ReplacementCount == 1 ? "match" : "matches")} in {result.Path} (+{result.AddedLineCount} -{result.RemovedLineCount})."));
        }
        catch (ArgumentException exception) when (string.Equals(exception.ParamName, "search", StringComparison.Ordinal))
        {
            return ToolResultFactory.InvalidArguments(
                "invalid_regex",
                exception.Message,
                new ToolRenderPayload(
                    "Invalid search_and_replace arguments",
                    exception.Message));
        }
    }
}
