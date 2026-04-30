using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;

namespace NanoAgent.Application.Tools;

internal sealed class CodeIntelligenceTool : ITool
{
    private const int DefaultTimeoutSeconds = 10;
    private const int MaxTimeoutSeconds = 30;
    private const string SupportedActionsText =
        "document_symbols, symbols_list, definition, definition_find, references, references_find, implementation_find, hover, call_hierarchy, rename_symbol, diagnostics_list, test_discover, dependency_graph";

    private static readonly IReadOnlySet<string> PositionActions = new HashSet<string>(
        [
            "definition",
            "definition_find",
            "references",
            "references_find",
            "implementation_find",
            "hover",
            "call_hierarchy",
            "rename_symbol"
        ],
        StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> NewNameActions = new HashSet<string>(
        ["rename_symbol"],
        StringComparer.Ordinal);

    private readonly ICodeIntelligenceService _codeIntelligenceService;

    public CodeIntelligenceTool(ICodeIntelligenceService codeIntelligenceService)
    {
        _codeIntelligenceService = codeIntelligenceService;
    }

    public string Description => "Query installed language servers for read-only code intelligence such as symbols, definitions, implementations, references, call hierarchy, diagnostics, rename previews, tests, dependencies, and hover text.";

    public string Name => AgentToolNames.CodeIntelligence;

    public string PermissionRequirements => """
        {
          "approvalMode": "Automatic",
          "toolTags": ["read"],
          "filePaths": [
            {
              "argumentName": "path",
              "kind": "Read",
              "allowedRoots": ["."]
            }
          ]
        }
        """;

    public string Schema => """
        {
          "type": "object",
          "properties": {
            "action": {
              "type": "string",
              "enum": ["document_symbols", "symbols_list", "definition", "definition_find", "references", "references_find", "implementation_find", "hover", "call_hierarchy", "rename_symbol", "diagnostics_list", "test_discover", "dependency_graph"],
              "description": "The code intelligence query to run."
            },
            "path": {
              "type": "string",
              "description": "Source file path relative to the current session working directory."
            },
            "line": {
              "type": "integer",
              "minimum": 1,
              "description": "One-based line number. Required for definition, definition_find, references, references_find, implementation_find, hover, call_hierarchy, and rename_symbol."
            },
            "character": {
              "type": "integer",
              "minimum": 1,
              "description": "One-based character position. Required for definition, definition_find, references, references_find, implementation_find, hover, call_hierarchy, and rename_symbol."
            },
            "includeDeclaration": {
              "type": "boolean",
              "description": "When action is references or references_find, include the symbol declaration in the result."
            },
            "query": {
              "type": "string",
              "description": "Workspace symbol query for symbols_list. Defaults to an empty query."
            },
            "newName": {
              "type": "string",
              "description": "Replacement symbol name for rename_symbol. The tool previews edits and does not modify files."
            },
            "callDirection": {
              "type": "string",
              "enum": ["incoming", "outgoing", "both", "prepare"],
              "description": "Call hierarchy direction for call_hierarchy. Defaults to both."
            },
            "timeoutSeconds": {
              "type": "integer",
              "minimum": 1,
              "maximum": 30,
              "description": "Maximum time to wait for the language server. Defaults to 10 seconds."
            }
          },
          "required": ["action", "path"],
          "additionalProperties": false
        }
        """;

    public async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!ToolArguments.TryGetNonEmptyString(context.Arguments, "action", out string? action) ||
            !TryNormalizeAction(action!, out string? normalizedAction))
        {
            return ToolResultFactory.InvalidArguments(
                "invalid_action",
                $"Tool 'code_intelligence' requires action to be one of: {SupportedActionsText}.",
                new ToolRenderPayload(
                    "Invalid code_intelligence arguments",
                    "Provide a supported action."));
        }

        if (!ToolArguments.TryGetNonEmptyString(context.Arguments, "path", out string? path))
        {
            return ToolResultFactory.InvalidArguments(
                "missing_path",
                "Tool 'code_intelligence' requires a non-empty 'path' string.",
                new ToolRenderPayload(
                    "Invalid code_intelligence arguments",
                    "Provide a non-empty source file path."));
        }

        if (!TryReadPosition(context, normalizedAction!, out int? line, out int? character, out ToolResult? invalidPosition))
        {
            return invalidPosition!;
        }

        if (!TryReadNewName(context, normalizedAction!, out string? newName, out ToolResult? invalidNewName))
        {
            return invalidNewName!;
        }

        if (!TryReadCallDirection(context, normalizedAction!, out string? callDirection, out ToolResult? invalidCallDirection))
        {
            return invalidCallDirection!;
        }

        if (!TryReadTimeout(context, out int timeoutSeconds, out ToolResult? invalidTimeout))
        {
            return invalidTimeout!;
        }

        string safePath;
        try
        {
            safePath = context.Session.ResolvePathFromWorkingDirectory(path!);
        }
        catch (InvalidOperationException exception)
        {
            return ToolResultFactory.InvalidArguments(
                "path_outside_workspace",
                exception.Message,
                new ToolRenderPayload(
                    "Invalid code_intelligence arguments",
                    exception.Message));
        }

        try
        {
            CodeIntelligenceResult result = await _codeIntelligenceService.QueryAsync(
                new CodeIntelligenceRequest(
                    normalizedAction!,
                    safePath,
                    line,
                    character,
                    ToolArguments.GetBoolean(context.Arguments, "includeDeclaration"),
                    timeoutSeconds,
                    ToolArguments.GetOptionalString(context.Arguments, "query"),
                    newName,
                    callDirection),
                cancellationToken);

            SessionStateToolRecorder.RecordCodeIntelligence(context.Session, result);

            return ToolResultFactory.Success(
                CreateSuccessMessage(result),
                result,
                ToolJsonContext.Default.CodeIntelligenceResult,
                new ToolRenderPayload(
                    CreateRenderTitle(result),
                    CreateRenderText(result)));
        }
        catch (CodeIntelligenceUnavailableException exception)
        {
            return ToolResultFactory.NotFound(
                "language_server_unavailable",
                exception.Message,
                new ToolRenderPayload(
                    "Code intelligence unavailable",
                    FormatUnavailableDetails(exception)));
        }
        catch (FileNotFoundException exception)
        {
            return ToolResultFactory.NotFound(
                "file_not_found",
                exception.Message,
                new ToolRenderPayload(
                    "Source file not found",
                    exception.Message));
        }
        catch (IOException exception)
        {
            return ToolResultFactory.ExecutionError(
                "code_intelligence_io_error",
                exception.Message,
                new ToolRenderPayload(
                    "Code intelligence failed",
                    exception.Message));
        }
        catch (UnauthorizedAccessException exception)
        {
            return ToolResultFactory.PermissionDenied(
                "code_intelligence_access_denied",
                exception.Message,
                new ToolRenderPayload(
                    "Code intelligence access denied",
                    exception.Message));
        }
    }

    private static bool TryNormalizeAction(
        string value,
        out string? action)
    {
        action = value.Trim().ToLowerInvariant().Replace('-', '_');
        return action switch
        {
            "document_symbols" or
            "symbols_list" or
            "definition" or
            "definition_find" or
            "references" or
            "references_find" or
            "implementation_find" or
            "hover" or
            "call_hierarchy" or
            "rename_symbol" or
            "diagnostics_list" or
            "test_discover" or
            "dependency_graph" => true,
            _ => false
        };
    }

    private static bool TryReadPosition(
        ToolExecutionContext context,
        string action,
        out int? line,
        out int? character,
        out ToolResult? invalidResult)
    {
        line = null;
        character = null;
        invalidResult = null;

        if (!PositionActions.Contains(action))
        {
            return true;
        }

        if (!ToolArguments.TryGetInt32(context.Arguments, "line", out int requestedLine) ||
            !ToolArguments.TryGetInt32(context.Arguments, "character", out int requestedCharacter) ||
            requestedLine < 1 ||
            requestedCharacter < 1)
        {
            invalidResult = ToolResultFactory.InvalidArguments(
                "position_required",
                $"Tool 'code_intelligence' requires positive one-based 'line' and 'character' values for action '{action}'.",
                new ToolRenderPayload(
                    "Invalid code_intelligence arguments",
                    "Provide positive one-based line and character values."));
            return false;
        }

        line = requestedLine;
        character = requestedCharacter;
        return true;
    }

    private static bool TryReadNewName(
        ToolExecutionContext context,
        string action,
        out string? newName,
        out ToolResult? invalidResult)
    {
        newName = null;
        invalidResult = null;

        if (!NewNameActions.Contains(action))
        {
            return true;
        }

        if (!ToolArguments.TryGetNonEmptyString(context.Arguments, "newName", out newName))
        {
            invalidResult = ToolResultFactory.InvalidArguments(
                "new_name_required",
                $"Tool 'code_intelligence' requires a non-empty 'newName' value for action '{action}'.",
                new ToolRenderPayload(
                    "Invalid code_intelligence arguments",
                    "Provide the replacement symbol name."));
            return false;
        }

        return true;
    }

    private static bool TryReadCallDirection(
        ToolExecutionContext context,
        string action,
        out string? callDirection,
        out ToolResult? invalidResult)
    {
        callDirection = null;
        invalidResult = null;

        if (!string.Equals(action, "call_hierarchy", StringComparison.Ordinal))
        {
            return true;
        }

        string? requestedDirection = ToolArguments.GetOptionalString(context.Arguments, "callDirection");
        if (string.IsNullOrWhiteSpace(requestedDirection))
        {
            callDirection = "both";
            return true;
        }

        callDirection = requestedDirection.Trim().ToLowerInvariant().Replace('-', '_');
        if (callDirection is "incoming" or "outgoing" or "both" or "prepare")
        {
            return true;
        }

        invalidResult = ToolResultFactory.InvalidArguments(
            "invalid_call_direction",
            "Tool 'code_intelligence' requires callDirection to be one of: incoming, outgoing, both, prepare.",
            new ToolRenderPayload(
                "Invalid code_intelligence arguments",
                "Provide a supported call hierarchy direction."));
        return false;
    }

    private static bool TryReadTimeout(
        ToolExecutionContext context,
        out int timeoutSeconds,
        out ToolResult? invalidResult)
    {
        invalidResult = null;
        timeoutSeconds = DefaultTimeoutSeconds;

        if (!ToolArguments.TryGetInt32(context.Arguments, "timeoutSeconds", out int requestedTimeout))
        {
            return true;
        }

        if (requestedTimeout < 1 || requestedTimeout > MaxTimeoutSeconds)
        {
            invalidResult = ToolResultFactory.InvalidArguments(
                "invalid_timeout",
                $"Tool 'code_intelligence' requires timeoutSeconds between 1 and {MaxTimeoutSeconds}.",
                new ToolRenderPayload(
                    "Invalid code_intelligence arguments",
                    $"timeoutSeconds must be between 1 and {MaxTimeoutSeconds}."));
            return false;
        }

        timeoutSeconds = requestedTimeout;
        return true;
    }

    private static string CreateSuccessMessage(CodeIntelligenceResult result)
    {
        if (string.Equals(result.Action, "hover", StringComparison.Ordinal))
        {
            return string.IsNullOrWhiteSpace(result.HoverText)
                ? $"No hover information found for '{result.Path}'."
                : $"Found hover information for '{result.Path}'.";
        }

        return $"Found {result.Items.Count} code intelligence {(result.Items.Count == 1 ? "result" : "results")} for '{result.Path}'.";
    }

    private static string CreateRenderTitle(CodeIntelligenceResult result)
    {
        return $"{result.Action}: {result.Path}";
    }

    private static string CreateRenderText(CodeIntelligenceResult result)
    {
        List<string> lines = [];
        lines.Add($"Language: {result.LanguageId}");
        lines.Add($"Server: {result.ServerName}");

        if (result.Warnings.Count > 0)
        {
            lines.Add("Warnings:");
            lines.AddRange(result.Warnings.Select(static warning => $"- {warning}"));
        }

        if (!string.IsNullOrWhiteSpace(result.HoverText))
        {
            lines.Add("Hover:");
            lines.Add(result.HoverText!);
        }

        if (result.Items.Count > 0)
        {
            lines.Add("Results:");
            lines.AddRange(result.Items.Select(FormatItem));
        }

        if (result.Items.Count == 0 && string.IsNullOrWhiteSpace(result.HoverText))
        {
            lines.Add("No results found.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatUnavailableDetails(CodeIntelligenceUnavailableException exception)
    {
        if (exception.Attempts.Count == 0)
        {
            return exception.Message;
        }

        return exception.Message +
            Environment.NewLine +
            string.Join(Environment.NewLine, exception.Attempts.Select(static attempt => $"- {attempt}"));
    }

    private static string FormatItem(CodeIntelligenceItem item)
    {
        string label = string.IsNullOrWhiteSpace(item.Name)
            ? item.Kind
            : $"{item.Kind} {item.Name}";
        string detail = string.IsNullOrWhiteSpace(item.Detail)
            ? string.Empty
            : $" - {item.Detail}";
        string container = string.IsNullOrWhiteSpace(item.ContainerName)
            ? string.Empty
            : $" ({item.ContainerName})";

        return $"- {label}{container}: {item.Path}:{item.StartLine}:{item.StartCharacter}{detail}";
    }
}
