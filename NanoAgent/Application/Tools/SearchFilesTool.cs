using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;

namespace NanoAgent.Application.Tools;

internal sealed class SearchFilesTool : ITool
{
    private const int MaxLimit = 200;

    private readonly IWorkspaceFileService _workspaceFileService;

    public SearchFilesTool(IWorkspaceFileService workspaceFileService)
    {
        _workspaceFileService = workspaceFileService;
    }

    public string Description => "Search for files from the current session working directory by name or relative path fragment.";

    public string Name => AgentToolNames.SearchFiles;

    public string PermissionRequirements => """
        {
          "approvalMode": "Automatic",
          "toolTags": ["read"],
          "filePaths": [
            {
              "argumentName": "path",
              "kind": "Search",
              "allowedRoots": ["."]
            }
          ]
        }
        """;

    public string Schema => """
        {
          "type": "object",
          "properties": {
            "query": {
              "type": "string",
              "description": "File name or relative path text to search for."
            },
            "path": {
              "type": "string",
              "description": "Optional file or directory path relative to the current session working directory."
            },
            "caseSensitive": {
              "type": "boolean",
              "description": "Whether to use case-sensitive matching."
            },
            "glob": {
              "type": "string",
              "description": "Optional glob filter applied to relative paths before matching, for example \"**/*.cs\" or \"*.json\"."
            },
            "fuzzy": {
              "type": "boolean",
              "description": "When true, use fuzzy subsequence matching and rank closer filename/path matches first."
            },
            "limit": {
              "type": "integer",
              "minimum": 1,
              "maximum": 200,
              "description": "Maximum number of matching files to return. Defaults to 200."
            }
          },
          "required": ["query"],
          "additionalProperties": false
        }
        """;

    public async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!ToolArguments.TryGetNonEmptyString(context.Arguments, "query", out string? query))
        {
            return ToolResultFactory.InvalidArguments(
                "missing_query",
                "Tool 'search_files' requires a non-empty 'query' string.",
                new ToolRenderPayload(
                    "Invalid search_files arguments",
                    "Provide a non-empty 'query' string."));
        }

        if (!TryReadLimit(context, out int limit, out ToolResult? invalidLimit))
        {
            return invalidLimit!;
        }

        if (!TryReadGlob(context, out string? glob, out ToolResult? invalidGlob))
        {
            return invalidGlob!;
        }

        WorkspaceFileSearchResult result = await _workspaceFileService.SearchFilesAsync(
            new WorkspaceFileSearchRequest(
                query!,
                context.Session.ResolvePathFromWorkingDirectory(
                    ToolArguments.GetOptionalString(context.Arguments, "path")),
                ToolArguments.GetBoolean(context.Arguments, "caseSensitive"),
                glob,
                ToolArguments.GetBoolean(context.Arguments, "fuzzy"),
                limit),
            cancellationToken);
        SessionStateToolRecorder.RecordFileSearch(context.Session, result);

        string renderText = CreateRenderText(result);

        return ToolResultFactory.Success(
            $"Found {result.Matches.Count} matching {(result.Matches.Count == 1 ? "file" : "files")} for '{result.Query}'.",
            result,
            ToolJsonContext.Default.WorkspaceFileSearchResult,
            new ToolRenderPayload(
                $"File search for '{result.Query}'",
                renderText));
    }

    private static bool TryReadLimit(
        ToolExecutionContext context,
        out int limit,
        out ToolResult? invalidResult)
    {
        invalidResult = null;
        limit = MaxLimit;

        if (!context.Arguments.TryGetProperty("limit", out _))
        {
            return true;
        }

        if (!ToolArguments.TryGetInt32(context.Arguments, "limit", out int requestedLimit) ||
            requestedLimit < 1 ||
            requestedLimit > MaxLimit)
        {
            invalidResult = ToolResultFactory.InvalidArguments(
                "invalid_limit",
                $"Tool 'search_files' requires 'limit' to be between 1 and {MaxLimit}.",
                new ToolRenderPayload(
                    "Invalid search_files arguments",
                    $"Provide a 'limit' value between 1 and {MaxLimit}."));
            return false;
        }

        limit = requestedLimit;
        return true;
    }

    private static bool TryReadGlob(
        ToolExecutionContext context,
        out string? glob,
        out ToolResult? invalidResult)
    {
        invalidResult = null;
        glob = null;

        if (!context.Arguments.TryGetProperty("glob", out _))
        {
            return true;
        }

        if (!ToolArguments.TryGetNonEmptyString(context.Arguments, "glob", out glob))
        {
            invalidResult = ToolResultFactory.InvalidArguments(
                "invalid_glob",
                "Tool 'search_files' requires 'glob' to be a non-empty string when provided.",
                new ToolRenderPayload(
                    "Invalid search_files arguments",
                    "Provide a non-empty 'glob' string."));
            return false;
        }

        return true;
    }

    private static string CreateRenderText(WorkspaceFileSearchResult result)
    {
        List<string> lines =
        [
            $"Search options: limit={result.Limit}, fuzzy={result.Fuzzy.ToString().ToLowerInvariant()}, caseSensitive={result.CaseSensitive.ToString().ToLowerInvariant()}" +
            (string.IsNullOrWhiteSpace(result.Glob) ? string.Empty : $", glob={result.Glob}")
        ];

        if (result.Matches.Count == 0)
        {
            lines.Add("No matching files found.");
            return string.Join(Environment.NewLine, lines);
        }

        lines.AddRange(result.Matches);
        return string.Join(Environment.NewLine, lines);
    }
}
