using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;

namespace NanoAgent.Application.Tools;

internal sealed class CodebaseIndexTool : ITool
{
    private readonly ICodebaseIndexService _codebaseIndexService;

    public CodebaseIndexTool(ICodebaseIndexService codebaseIndexService)
    {
        _codebaseIndexService = codebaseIndexService;
    }

    public string Description => "Build, inspect, and search NanoAgent's local codebase index. The index is stored under .nanoagent/cache, refreshes incrementally when searched or built, respects workspace ignore rules, and helps answer repository-wide code questions without copying another editor's implementation.";

    public string Name => AgentToolNames.CodebaseIndex;

    public string PermissionRequirements => """
        {
          "approvalMode": "Automatic",
          "toolTags": ["read", "codebase_index"]
        }
        """;

    public string Schema => """
        {
          "type": "object",
          "properties": {
            "action": {
              "type": "string",
              "enum": ["status", "build", "search", "list"],
              "description": "Index operation to run. Search refreshes the index incrementally before ranking matches."
            },
            "query": {
              "type": "string",
              "description": "Natural-language or code-symbol query for search."
            },
            "limit": {
              "type": "integer",
              "description": "Maximum number of files or matches to return. Defaults to 10 for search and 200 for list."
            },
            "includeSnippets": {
              "type": "boolean",
              "description": "Whether search results should include matching source snippets. Defaults to true."
            },
            "force": {
              "type": "boolean",
              "description": "Whether build should rebuild every indexed file even if metadata appears unchanged."
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
                "Tool 'codebase_index' requires an action: status, build, search, or list.");
        }

        return action!.ToLowerInvariant() switch
        {
            "status" => await StatusAsync(cancellationToken),
            "build" => await BuildAsync(context, cancellationToken),
            "search" => await SearchAsync(context, cancellationToken),
            "list" => await ListAsync(context, cancellationToken),
            _ => InvalidArguments(
                "invalid_action",
                $"Tool 'codebase_index' received unsupported action '{action}'.")
        };
    }

    private async Task<ToolResult> StatusAsync(CancellationToken cancellationToken)
    {
        CodebaseIndexStatusResult result = await _codebaseIndexService.GetStatusAsync(cancellationToken);
        string statusText = result.Exists
            ? result.IsStale
                ? $"Index is stale: {result.NewFileCount} new, {result.ChangedFileCount} changed, {result.DeletedFileCount} deleted."
                : "Index is up to date."
            : "Index has not been built yet.";

        return ToolResultFactory.Success(
            statusText,
            result,
            ToolJsonContext.Default.CodebaseIndexStatusResult,
            new ToolRenderPayload(
                "Codebase index status",
                FormatStatus(result)));
    }

    private async Task<ToolResult> BuildAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        CodebaseIndexBuildResult result = await _codebaseIndexService.BuildAsync(
            ToolArguments.GetBoolean(context.Arguments, "force"),
            cancellationToken);

        return ToolResultFactory.Success(
            $"Indexed {result.IndexedFileCount} files.",
            result,
            ToolJsonContext.Default.CodebaseIndexBuildResult,
            new ToolRenderPayload(
                "Codebase index built",
                $"Indexed {result.IndexedFileCount} files: +{result.AddedFileCount}, ~{result.UpdatedFileCount}, -{result.RemovedFileCount}, reused {result.ReusedFileCount}, skipped {result.SkippedFileCount}."));
    }

    private async Task<ToolResult> SearchAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!ToolArguments.TryGetNonEmptyString(context.Arguments, "query", out string? query))
        {
            return InvalidArguments(
                "missing_query",
                "Tool 'codebase_index' search requires a non-empty 'query' string.");
        }

        CodebaseIndexSearchResult result = await _codebaseIndexService.SearchAsync(
            query!,
            GetLimit(context, defaultValue: 10),
            ToolArguments.GetBoolean(context.Arguments, "includeSnippets", defaultValue: true),
            cancellationToken);

        string message = result.Matches.Count == 0
            ? $"No indexed codebase matches found for '{result.Query}'."
            : $"Found {result.Matches.Count} indexed codebase matches for '{result.Query}'.";

        return ToolResultFactory.Success(
            message,
            result,
            ToolJsonContext.Default.CodebaseIndexSearchResult,
            new ToolRenderPayload(
                $"Codebase index search: {result.Query}",
                FormatSearch(result)));
    }

    private async Task<ToolResult> ListAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        CodebaseIndexListResult result = await _codebaseIndexService.ListAsync(
            GetLimit(context, defaultValue: 200),
            cancellationToken);

        return ToolResultFactory.Success(
            $"Listed {result.ReturnedFileCount} indexed files.",
            result,
            ToolJsonContext.Default.CodebaseIndexListResult,
            new ToolRenderPayload(
                "Indexed files",
                result.Files.Count == 0
                    ? "No files are indexed yet."
                    : string.Join(Environment.NewLine, result.Files)));
    }

    private static int GetLimit(
        ToolExecutionContext context,
        int defaultValue)
    {
        return ToolArguments.TryGetInt32(context.Arguments, "limit", out int limit)
            ? limit
            : defaultValue;
    }

    private static string FormatStatus(CodebaseIndexStatusResult result)
    {
        List<string> lines =
        [
            $"Index path: {result.IndexPath}",
            $"Built: {(result.BuiltAtUtc is null ? "never" : result.BuiltAtUtc.Value.ToString("u"))}",
            $"Indexed files: {result.IndexedFileCount}",
            $"Workspace files: {result.WorkspaceFileCount}",
            $"Skipped files: {result.SkippedFileCount}",
            $"Stale: {result.IsStale}"
        ];

        AddSamples(lines, "New", result.SampleNewFiles);
        AddSamples(lines, "Changed", result.SampleChangedFiles);
        AddSamples(lines, "Deleted", result.SampleDeletedFiles);
        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatSearch(CodebaseIndexSearchResult result)
    {
        if (result.Matches.Count == 0)
        {
            return result.IndexWasUpdated
                ? "Index refreshed; no matching files found."
                : "No matching files found.";
        }

        List<string> lines = [];
        if (result.IndexWasUpdated)
        {
            lines.Add("Index refreshed before search.");
        }

        foreach (CodebaseIndexSearchMatch match in result.Matches)
        {
            lines.Add($"- {match.Path} [{match.Language}] score {match.Score:0.##}");
            if (match.Symbols.Count > 0)
            {
                lines.Add($"  symbols: {string.Join(", ", match.Symbols.Take(5))}");
            }

            foreach (CodebaseIndexSnippet snippet in match.Snippets.Take(2))
            {
                lines.Add($"  {snippet.LineNumber}: {snippet.Text}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static void AddSamples(
        List<string> lines,
        string label,
        IReadOnlyList<string> samples)
    {
        if (samples.Count == 0)
        {
            return;
        }

        lines.Add($"{label}: {string.Join(", ", samples)}");
    }

    private static ToolResult InvalidArguments(
        string code,
        string message)
    {
        return ToolResultFactory.InvalidArguments(
            code,
            message,
            new ToolRenderPayload(
                "Invalid codebase_index arguments",
                message));
    }
}
