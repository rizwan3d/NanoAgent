using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;

namespace NanoAgent.Application.Tools;

internal sealed class SearchFilesTool : ITool
{
    private readonly IWorkspaceFileService _workspaceFileService;

    public SearchFilesTool(IWorkspaceFileService workspaceFileService)
    {
        _workspaceFileService = workspaceFileService;
    }

    public string Description => "Search for files in the current workspace by name or relative path fragment.";

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
              "description": "Optional file or directory path relative to the workspace root."
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
                "Tool 'search_files' requires a non-empty 'query' string.",
                new ToolRenderPayload(
                    "Invalid search_files arguments",
                    "Provide a non-empty 'query' string."));
        }

        WorkspaceFileSearchResult result = await _workspaceFileService.SearchFilesAsync(
            new WorkspaceFileSearchRequest(
                query!,
                ToolArguments.GetOptionalString(context.Arguments, "path"),
                ToolArguments.GetBoolean(context.Arguments, "caseSensitive")),
            cancellationToken);

        string renderText = result.Matches.Count == 0
            ? "No matching files found."
            : string.Join(Environment.NewLine, result.Matches);

        return ToolResultFactory.Success(
            $"Found {result.Matches.Count} matching {(result.Matches.Count == 1 ? "file" : "files")} for '{result.Query}'.",
            result,
            ToolJsonContext.Default.WorkspaceFileSearchResult,
            new ToolRenderPayload(
                $"File search for '{result.Query}'",
                renderText));
    }

}
