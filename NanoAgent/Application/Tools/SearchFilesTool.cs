using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

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
              "description": "File name or relative path text to search for. Optional only when mode is \"glob_only\"."
            },
            "path": {
              "type": "string",
              "description": "Optional file or directory path relative to the current session working directory."
            },
            "mode": {
              "type": "string",
              "enum": ["substring", "fuzzy", "exact", "regex", "glob_only"],
              "description": "Search mode. Defaults to \"substring\" unless legacy flags imply another mode."
            },
            "caseSensitive": {
              "type": "boolean",
              "description": "Whether to use case-sensitive matching."
            },
            "regex": {
              "type": "boolean",
              "description": "Legacy alias for mode=\"regex\"."
            },
            "wholeWord": {
              "type": "boolean",
              "description": "When true, only match whole words in the filename or relative path."
            },
            "glob": {
              "type": "string",
              "description": "Legacy include glob applied to relative paths before matching, for example \"**/*.cs\" or \"*.json\"."
            },
            "includeGlobs": {
              "type": "array",
              "items": {
                "type": "string"
              },
              "description": "Optional include globs. A file must match at least one include glob when provided."
            },
            "excludeGlobs": {
              "type": "array",
              "items": {
                "type": "string"
              },
              "description": "Optional exclude globs applied after include globs."
            },
            "fuzzy": {
              "type": "boolean",
              "description": "Legacy alias for mode=\"fuzzy\"."
            },
            "offset": {
              "type": "integer",
              "minimum": 0,
              "description": "Zero-based result offset. Cannot be combined with cursor."
            },
            "cursor": {
              "type": "string",
              "description": "Opaque pagination cursor returned by a previous search_files result. Cannot be combined with offset."
            },
            "includeHidden": {
              "type": "boolean",
              "description": "When true, include hidden and dot-prefixed files and directories."
            },
            "includeGenerated": {
              "type": "boolean",
              "description": "When true, include generated, build-output, and vendor/dependency files."
            },
            "includeIgnored": {
              "type": "boolean",
              "description": "When true, include files ignored by .gitignore or .nanoignore."
            },
            "limit": {
              "type": "integer",
              "minimum": 1,
              "maximum": 200,
              "description": "Maximum number of matching files to return. Defaults to 200."
            }
          },
          "additionalProperties": false
        }
        """;

    public async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryCreateRequest(context, out WorkspaceFileSearchRequest? request, out ToolResult? invalidRequest))
        {
            return invalidRequest!;
        }

        WorkspaceFileSearchResult result = await _workspaceFileService.SearchFilesAsync(
            request!,
            cancellationToken);
        SessionStateToolRecorder.RecordFileSearch(context.Session, result);

        string renderText = CreateRenderText(result);
        string searchLabel = FormatSearchLabel(result.Query, result.Mode);

        return ToolResultFactory.Success(
            $"Found {result.Matches.Count} matching {(result.Matches.Count == 1 ? "file" : "files")} for {searchLabel}.",
            result,
            ToolJsonContext.Default.WorkspaceFileSearchResult,
            new ToolRenderPayload(
                $"File search for {searchLabel}",
                renderText));
    }

    private static bool TryCreateRequest(
        ToolExecutionContext context,
        out WorkspaceFileSearchRequest? request,
        out ToolResult? invalidResult)
    {
        request = null;
        invalidResult = null;

        string? modeInput = ToolArguments.GetOptionalString(context.Arguments, "mode");
        if (!string.IsNullOrWhiteSpace(modeInput) &&
            !WorkspaceFileSearchModes.IsSupported(modeInput))
        {
            invalidResult = InvalidArguments(
                "invalid_mode",
                "Tool 'search_files' requires 'mode' to be one of substring, fuzzy, exact, regex, or glob_only.",
                "Provide 'mode' as one of substring, fuzzy, exact, regex, or glob_only.");
            return false;
        }

        bool fuzzy = ToolArguments.GetBoolean(context.Arguments, "fuzzy");
        bool regex = ToolArguments.GetBoolean(context.Arguments, "regex");
        string mode = ResolveMode(modeInput, fuzzy, regex);

        if (!ValidateLegacyModeFlags(modeInput, mode, fuzzy, regex, out invalidResult))
        {
            return false;
        }

        bool wholeWord = ToolArguments.GetBoolean(context.Arguments, "wholeWord");
        if (wholeWord &&
            (string.Equals(mode, WorkspaceFileSearchModes.Fuzzy, StringComparison.Ordinal) ||
             string.Equals(mode, WorkspaceFileSearchModes.GlobOnly, StringComparison.Ordinal)))
        {
            invalidResult = InvalidArguments(
                "invalid_whole_word",
                "Tool 'search_files' supports 'wholeWord' only with substring, exact, or regex modes.",
                "Remove 'wholeWord' or use mode substring, exact, or regex.");
            return false;
        }

        if (!TryReadLimit(context, out int limit, out invalidResult) ||
            !TryReadOffset(context, out int offset, out invalidResult) ||
            !TryReadCursor(context, out string? cursor, out invalidResult))
        {
            return false;
        }

        if (offset > 0 && !string.IsNullOrWhiteSpace(cursor))
        {
            invalidResult = InvalidArguments(
                "offset_cursor_conflict",
                "Tool 'search_files' does not allow 'offset' and 'cursor' together.",
                "Provide either 'offset' or 'cursor', but not both.");
            return false;
        }

        if (!TryReadLegacyGlob(context, out string? glob, out invalidResult) ||
            !TryReadGlobArray(context, "includeGlobs", out string[] includeGlobs, out invalidResult) ||
            !TryReadGlobArray(context, "excludeGlobs", out string[] excludeGlobs, out invalidResult))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(glob))
        {
            includeGlobs = [glob!, .. includeGlobs];
        }

        string? query = ToolArguments.GetOptionalString(context.Arguments, "query");
        if (string.IsNullOrWhiteSpace(query))
        {
            if (!string.Equals(mode, WorkspaceFileSearchModes.GlobOnly, StringComparison.Ordinal))
            {
                invalidResult = InvalidArguments(
                    "missing_query",
                    "Tool 'search_files' requires a non-empty 'query' string unless mode is 'glob_only'.",
                    "Provide a non-empty 'query' string or set mode to 'glob_only'.");
                return false;
            }

            if (includeGlobs.Length == 0)
            {
                invalidResult = InvalidArguments(
                    "missing_glob_filters",
                    "Tool 'search_files' requires at least one include glob when mode is 'glob_only'.",
                    "Provide 'glob' or 'includeGlobs' when mode is 'glob_only'.");
                return false;
            }

            query = string.Empty;
        }

        if (string.Equals(mode, WorkspaceFileSearchModes.Regex, StringComparison.Ordinal) &&
            !TryValidateRegexQuery(query!, ToolArguments.GetBoolean(context.Arguments, "caseSensitive"), wholeWord, out invalidResult))
        {
            return false;
        }

        request = new WorkspaceFileSearchRequest(
            query!,
            context.Session.ResolvePathFromWorkingDirectory(
                ToolArguments.GetOptionalString(context.Arguments, "path")),
            ToolArguments.GetBoolean(context.Arguments, "caseSensitive"),
            glob,
            fuzzy,
            limit,
            mode,
            regex,
            wholeWord,
            offset,
            cursor,
            ToolArguments.GetBoolean(context.Arguments, "includeHidden"),
            ToolArguments.GetBoolean(context.Arguments, "includeGenerated"),
            ToolArguments.GetBoolean(context.Arguments, "includeIgnored"),
            includeGlobs,
            excludeGlobs);

        return true;
    }

    private static string CreateRenderText(WorkspaceFileSearchResult result)
    {
        StringBuilder options = new();
        options.Append("Search options: ")
            .Append("mode=").Append(result.Mode)
            .Append(", limit=").Append(result.Limit)
            .Append(", offset=").Append(result.Offset)
            .Append(", caseSensitive=").Append(result.CaseSensitive.ToString().ToLowerInvariant())
            .Append(", wholeWord=").Append(result.WholeWord.ToString().ToLowerInvariant())
            .Append(", includeHidden=").Append(result.IncludeHidden.ToString().ToLowerInvariant())
            .Append(", includeGenerated=").Append(result.IncludeGenerated.ToString().ToLowerInvariant())
            .Append(", includeIgnored=").Append(result.IncludeIgnored.ToString().ToLowerInvariant())
            .Append(", hasMore=").Append(result.HasMore.ToString().ToLowerInvariant());

        if (!string.IsNullOrWhiteSpace(result.Glob))
        {
            options.Append(", glob=").Append(result.Glob);
        }

        if (result.IncludeGlobs?.Count > 0)
        {
            options.Append(", includeGlobs=").Append(string.Join(", ", result.IncludeGlobs));
        }

        if (result.ExcludeGlobs?.Count > 0)
        {
            options.Append(", excludeGlobs=").Append(string.Join(", ", result.ExcludeGlobs));
        }

        if (!string.IsNullOrWhiteSpace(result.NextCursor))
        {
            options.Append(", nextCursor=").Append(result.NextCursor);
        }

        options.Append(", totalMatches=").Append(result.TotalMatchCount);

        List<string> lines = [options.ToString()];

        if (result.Matches.Count == 0)
        {
            lines.Add("No matching files found.");
            return string.Join(Environment.NewLine, lines);
        }

        lines.AddRange(result.Matches.Select(static match =>
            $"{match.Path} [score={match.Score}, matchKind={match.MatchKind}]"));
        return string.Join(Environment.NewLine, lines);
    }

    private static bool ValidateLegacyModeFlags(
        string? modeInput,
        string mode,
        bool fuzzy,
        bool regex,
        out ToolResult? invalidResult)
    {
        invalidResult = null;

        if (string.IsNullOrWhiteSpace(modeInput))
        {
            return true;
        }

        if (fuzzy && !string.Equals(mode, WorkspaceFileSearchModes.Fuzzy, StringComparison.Ordinal))
        {
            invalidResult = InvalidArguments(
                "conflicting_fuzzy_mode",
                "Tool 'search_files' received 'fuzzy=true' together with a non-fuzzy 'mode'.",
                "Remove 'fuzzy' or set 'mode' to 'fuzzy'.");
            return false;
        }

        if (regex && !string.Equals(mode, WorkspaceFileSearchModes.Regex, StringComparison.Ordinal))
        {
            invalidResult = InvalidArguments(
                "conflicting_regex_mode",
                "Tool 'search_files' received 'regex=true' together with a non-regex 'mode'.",
                "Remove 'regex' or set 'mode' to 'regex'.");
            return false;
        }

        return true;
    }

    private static string ResolveMode(
        string? modeInput,
        bool fuzzy,
        bool regex)
    {
        if (!string.IsNullOrWhiteSpace(modeInput))
        {
            return modeInput!;
        }

        if (regex)
        {
            return WorkspaceFileSearchModes.Regex;
        }

        if (fuzzy)
        {
            return WorkspaceFileSearchModes.Fuzzy;
        }

        return WorkspaceFileSearchModes.Substring;
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
            invalidResult = InvalidArguments(
                "invalid_limit",
                $"Tool 'search_files' requires 'limit' to be between 1 and {MaxLimit}.",
                $"Provide a 'limit' value between 1 and {MaxLimit}.");
            return false;
        }

        limit = requestedLimit;
        return true;
    }

    private static bool TryReadOffset(
        ToolExecutionContext context,
        out int offset,
        out ToolResult? invalidResult)
    {
        invalidResult = null;
        offset = 0;

        if (!context.Arguments.TryGetProperty("offset", out _))
        {
            return true;
        }

        if (!ToolArguments.TryGetInt32(context.Arguments, "offset", out int requestedOffset) ||
            requestedOffset < 0)
        {
            invalidResult = InvalidArguments(
                "invalid_offset",
                "Tool 'search_files' requires 'offset' to be a non-negative integer.",
                "Provide a non-negative 'offset' value.");
            return false;
        }

        offset = requestedOffset;
        return true;
    }

    private static bool TryReadCursor(
        ToolExecutionContext context,
        out string? cursor,
        out ToolResult? invalidResult)
    {
        invalidResult = null;
        cursor = null;

        if (!context.Arguments.TryGetProperty("cursor", out _))
        {
            return true;
        }

        if (!ToolArguments.TryGetNonEmptyString(context.Arguments, "cursor", out cursor))
        {
            invalidResult = InvalidArguments(
                "invalid_cursor",
                "Tool 'search_files' requires 'cursor' to be a non-empty string when provided.",
                "Provide a non-empty 'cursor' string.");
            return false;
        }

        if (!TryDecodeCursor(cursor!, out _))
        {
            invalidResult = InvalidArguments(
                "invalid_cursor",
                "Tool 'search_files' received an invalid pagination cursor.",
                "Use the 'nextCursor' value returned by a previous search_files result.");
            return false;
        }

        return true;
    }

    private static bool TryReadLegacyGlob(
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
            invalidResult = InvalidArguments(
                "invalid_glob",
                "Tool 'search_files' requires 'glob' to be a non-empty string when provided.",
                "Provide a non-empty 'glob' string.");
            return false;
        }

        if (!TryValidateGlobPattern(glob!, out string error))
        {
            invalidResult = InvalidArguments(
                "invalid_glob",
                $"Tool 'search_files' received an invalid 'glob': {error}",
                $"Fix the 'glob' value. {error}");
            return false;
        }

        return true;
    }

    private static bool TryReadGlobArray(
        ToolExecutionContext context,
        string propertyName,
        out string[] values,
        out ToolResult? invalidResult)
    {
        invalidResult = null;
        values = [];

        if (!context.Arguments.TryGetProperty(propertyName, out JsonElement property))
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            invalidResult = InvalidArguments(
                "invalid_glob_array",
                $"Tool 'search_files' requires '{propertyName}' to be an array of non-empty glob strings.",
                $"Provide '{propertyName}' as an array of non-empty glob strings.");
            return false;
        }

        List<string> parsedValues = [];
        int index = 0;
        foreach (JsonElement item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                invalidResult = InvalidArguments(
                    "invalid_glob_array",
                    $"Tool 'search_files' requires '{propertyName}[{index}]' to be a string.",
                    $"Provide '{propertyName}' as an array of non-empty glob strings.");
                return false;
            }

            string value = item.GetString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                invalidResult = InvalidArguments(
                    "invalid_glob_array",
                    $"Tool 'search_files' requires '{propertyName}[{index}]' to be a non-empty glob string.",
                    $"Replace '{propertyName}[{index}]' with a non-empty glob string.");
                return false;
            }

            if (!TryValidateGlobPattern(value, out string error))
            {
                invalidResult = InvalidArguments(
                    "invalid_glob_array",
                    $"Tool 'search_files' received an invalid glob in '{propertyName}[{index}]': {error}",
                    $"Fix '{propertyName}[{index}]'. {error}");
                return false;
            }

            parsedValues.Add(value);
            index++;
        }

        values = parsedValues.ToArray();
        return true;
    }

    private static bool TryValidateRegexQuery(
        string query,
        bool caseSensitive,
        bool wholeWord,
        out ToolResult? invalidResult)
    {
        invalidResult = null;

        try
        {
            _ = new Regex(
                wholeWord
                    ? WrapWholeWordPattern(query)
                    : query,
                GetRegexOptions(caseSensitive));
            return true;
        }
        catch (ArgumentException exception)
        {
            invalidResult = InvalidArguments(
                "invalid_regex",
                $"Tool 'search_files' received an invalid regex query: {exception.Message}",
                "Provide a valid regular expression for 'query'.");
            return false;
        }
    }

    private static bool TryValidateGlobPattern(
        string value,
        out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "The pattern cannot be blank.";
            return false;
        }

        string trimmed = value.Trim();
        string normalized = trimmed.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized.Trim('/')))
        {
            error = "The pattern must contain at least one non-separator character.";
            return false;
        }

        for (int index = 0; index < normalized.Length; index++)
        {
            char character = normalized[index];
            if (character == '[')
            {
                int closingIndex = normalized.IndexOf(']', index + 1);
                if (closingIndex < 0)
                {
                    error = "Character class '[' is missing a closing ']'.";
                    return false;
                }

                if (closingIndex == index + 1)
                {
                    error = "Character classes must not be empty.";
                    return false;
                }

                index = closingIndex;
                continue;
            }

            if (character == ']')
            {
                error = "Character class ']' appears without a matching '['.";
                return false;
            }
        }

        return true;
    }

    private static string WrapWholeWordPattern(string pattern)
    {
        return $@"(?<![\p{{L}}\p{{N}}_])(?:{pattern})(?![\p{{L}}\p{{N}}_])";
    }

    private static RegexOptions GetRegexOptions(bool caseSensitive)
    {
        return caseSensitive
            ? RegexOptions.CultureInvariant
            : RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;
    }

    private static bool TryDecodeCursor(
        string cursor,
        out int offset)
    {
        offset = 0;

        try
        {
            byte[] bytes = Convert.FromBase64String(cursor);
            string text = Encoding.UTF8.GetString(bytes);
            return int.TryParse(text, out offset) && offset >= 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string FormatSearchLabel(
        string query,
        string mode)
    {
        return string.IsNullOrWhiteSpace(query)
            ? $"{mode} search"
            : $"'{query}'";
    }

    private static ToolResult InvalidArguments(
        string code,
        string message,
        string detail)
    {
        return ToolResultFactory.InvalidArguments(
            code,
            message,
            new ToolRenderPayload(
                "Invalid search_files arguments",
                detail));
    }
}
