using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;
using NanoAgent.Application.Utilities;

namespace NanoAgent.Application.Tools;

internal sealed class TextSearchTool : ITool
{
    private readonly IWorkspaceFileService _workspaceFileService;

    public TextSearchTool(IWorkspaceFileService workspaceFileService)
    {
        _workspaceFileService = workspaceFileService;
    }

    public string Description => "Search text recursively from the current session working directory in the workspace.";

    public string Name => AgentToolNames.TextSearch;

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
              "description": "Text to search for."
            },
            "path": {
              "type": "string",
              "description": "Optional file or directory path relative to the current session working directory."
            },
            "caseSensitive": {
              "type": "boolean",
              "description": "Whether to use case-sensitive matching."
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
                "Tool 'text_search' requires a non-empty 'query' string.",
                new ToolRenderPayload(
                    "Invalid text_search arguments",
                    "Provide a non-empty 'query' string."));
        }

        string safeQuery = query!;

        WorkspaceTextSearchResult result = await _workspaceFileService.SearchTextAsync(
            new WorkspaceTextSearchRequest(
                safeQuery,
                context.Session.ResolvePathFromWorkingDirectory(
                    ToolArguments.GetOptionalString(context.Arguments, "path")),
                ToolArguments.GetBoolean(context.Arguments, "caseSensitive")),
            cancellationToken);
        result = RedactTextSearchResult(result);
        SessionStateToolRecorder.RecordTextSearch(context.Session, result);

        string renderText = result.Matches.Count == 0
            ? "No matches found."
            : string.Join(
                Environment.NewLine,
                result.Matches.Select(match => $"{match.Path}:{match.LineNumber}: {match.LineText}"));

        return ToolResultFactory.Success(
            $"Searched for '{result.Query}' in '{result.Path}'.",
            result,
            ToolJsonContext.Default.WorkspaceTextSearchResult,
            new ToolRenderPayload(
                $"Search results for '{result.Query}'",
                renderText));
    }

    private static WorkspaceTextSearchResult RedactTextSearchResult(WorkspaceTextSearchResult result)
    {
        if (result.Matches.Count == 0)
        {
            return result;
        }

        WorkspaceTextSearchMatch[] matches = result.Matches
            .Select(static match => SecretRedactor.IsEnvironmentFilePath(match.Path)
                ? match with { LineText = SecretRedactor.RedactEnvironmentFileContent(match.LineText) }
                : match with { LineText = SecretRedactor.Redact(match.LineText) })
            .ToArray();

        return result with { Matches = matches };
    }

}
