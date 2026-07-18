using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;
using NanoAgent.Application.Utilities;

namespace NanoAgent.Application.Tools;

internal sealed class FileReadTool : ITool
{
    private const int DefaultLimit = 2_000;
    private readonly IWorkspaceFileService _workspaceFileService;

    public FileReadTool(IWorkspaceFileService workspaceFileService)
    {
        _workspaceFileService = workspaceFileService;
    }

    public string Description => "Read a UTF-8 text file from the current session working directory in the workspace.";

    public string Name => AgentToolNames.FileRead;

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
            "path": {
              "type": "string",
              "description": "Path to the file, relative to the current session working directory."
            },
            "offset": {
              "type": "integer",
              "description": "One-based line offset to start reading from. Defaults to 1."
            },
            "limit": {
              "type": "integer",
              "description": "Maximum number of lines to return. Defaults to 2000."
            }
          },
          "required": ["path"],
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
                "Tool 'file_read' requires a non-empty 'path' string.",
                new ToolRenderPayload(
                    "Invalid file_read arguments",
                    "Provide a non-empty 'path' string."));
        }

        if (!TryGetOffset(context, out int offset, out ToolResult? invalidResult) ||
            !TryGetLimit(context, out int limit, out invalidResult))
        {
            return invalidResult!;
        }

        string safePath = context.Session.ResolvePathFromWorkingDirectory(path!);

        Application.Tools.Models.WorkspaceFileReadResult result = await _workspaceFileService.ReadFileAsync(
            safePath,
            offset,
            limit,
            cancellationToken);
        if (SecretRedactor.IsEnvironmentFilePath(result.Path))
        {
            string redactedContent = RedactNumberedEnvironmentFileContent(result.Content);
            result = result with
            {
                Content = redactedContent
            };
        }

        SessionStateToolRecorder.RecordFileRead(context.Session, result);

        return ToolResultFactory.Success(
            $"Read file '{result.Path}'.",
            result,
            ToolJsonContext.Default.WorkspaceFileReadResult,
            new ToolRenderPayload(
                $"File: {result.Path}",
                BuildRenderText(result)));
    }

    private static bool TryGetOffset(
        ToolExecutionContext context,
        out int offset,
        out ToolResult? invalidResult)
    {
        invalidResult = null;
        offset = 1;

        if (!context.Arguments.TryGetProperty("offset", out _))
        {
            return true;
        }

        if (!ToolArguments.TryGetInt32(context.Arguments, "offset", out int requestedOffset) ||
            requestedOffset <= 0)
        {
            invalidResult = ToolResultFactory.InvalidArguments(
                "invalid_offset",
                "Tool 'file_read' requires 'offset' to be a positive integer.",
                new ToolRenderPayload(
                    "Invalid file_read arguments",
                    "Provide a positive 'offset' value."));
            return false;
        }

        offset = requestedOffset;
        return true;
    }

    private static bool TryGetLimit(
        ToolExecutionContext context,
        out int limit,
        out ToolResult? invalidResult)
    {
        invalidResult = null;
        limit = DefaultLimit;

        if (!context.Arguments.TryGetProperty("limit", out _))
        {
            return true;
        }

        if (!ToolArguments.TryGetInt32(context.Arguments, "limit", out int requestedLimit) ||
            requestedLimit <= 0)
        {
            invalidResult = ToolResultFactory.InvalidArguments(
                "invalid_limit",
                "Tool 'file_read' requires 'limit' to be a positive integer.",
                new ToolRenderPayload(
                    "Invalid file_read arguments",
                    "Provide a positive 'limit' value."));
            return false;
        }

        limit = requestedLimit;
        return true;
    }

    private static string BuildRenderText(
        WorkspaceFileReadResult result)
    {
        string[] lines =
        [
            $"<path>{result.Path}</path>",
            "<content>",
            result.Content,
            "</content>",
            string.Empty,
            $"Showing lines {result.StartLine}-{result.EndLine} of {result.TotalLines}."
        ];

        return result.NextOffset is int nextOffset
            ? string.Join("\n", lines) + $"\nUse offset={nextOffset} to continue."
            : string.Join("\n", lines);
    }

    private static string RedactNumberedEnvironmentFileContent(
        string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        string[] lines = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            int separatorIndex = line.IndexOf(": ", StringComparison.Ordinal);
            if (separatorIndex < 0)
            {
                lines[index] = SecretRedactor.RedactEnvironmentFileContent(line);
                continue;
            }

            string prefix = line[..(separatorIndex + 2)];
            string value = line[(separatorIndex + 2)..];
            lines[index] = prefix + SecretRedactor.RedactEnvironmentFileContent(value);
        }

        return string.Join("\n", lines);
    }
}
