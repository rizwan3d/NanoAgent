using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;

namespace NanoAgent.Application.Tools;

internal sealed class ApplyPatchTool(IWorkspaceFileService workspaceFileService) : ITool
{
    public string Description => """
        Use the `apply_patch` tool to edit files.

        A valid patch MUST have this exact outer structure:
        
        *** Begin Patch
        [file operations]
        *** End Patch
        
        Each file operation MUST start with exactly one action      header:
        
        *** Add File: <path>
        *** Delete File: <path>
        *** Update File: <path>
        
        Rules:
        
        1. `*** Add File: <path>`
           - Creates a new file.
           - Every file-content line that follows MUST start        with `+`.
           - Lines without `+` are invalid.
        
        2. `*** Delete File: <path>`
           - Deletes an existing file.
           - No content lines may follow this operation.
        
        3. `*** Update File: <path>`
           - Edits an existing file.
           - Each hunk starts with `@@` or `@@ <anchor text>`.
           - Every file line inside a hunk MUST start with ` `, `+`, or `-`.
           - The text after `@@` is only a locator. If you want to keep that file line, repeat it with a leading space. If you want to change it, include `-old line` and `+new line` entries in the hunk.
           - May include:
             *** Move to: <new path>
           - `*** Move to:` is only valid inside an `Update         File` operation.
        
        Example:
        
        *** Begin Patch
        *** Add File: hello.txt
        +Hello world
        
        *** Update File: src/app.py
        *** Move to: src/main.py
        @@ def greet():
        -print("Hi")
        +print("Hello, world!")
        
        *** Delete File: obsolete.txt
        *** End Patch
        
        Mandatory requirements:
        - Every operation MUST include an action header.
        - New files MUST use `+` at the start of every content      line.
        - Delete operations MUST NOT include file content.
        - Patches missing `*** Begin Patch` or `*** End Patch`      are invalid.
        - Patches with unknown headers are invalid.
        """;

    public string Name => AgentToolNames.ApplyPatch;

    public string PermissionRequirements => """
        {
          "approvalMode": "Automatic",
          "toolTags": ["edit"],
          "patch": {
            "patchArgumentName": "patch",
            "kind": "Write",
            "allowedRoots": ["."]
          }
        }
        """;

    public string Schema => """
        {
          "type": "object",
          "properties": {
            "patch": {
              "type": "string",
              "description": "Patch text in apply_patch format. File paths in headers (*** Add File, *** Delete File, *** Update File, *** Move to) are relative to the current session working directory. The patch must include *** Begin Patch, then one or more file sections, then *** End Patch. Prefix added lines with '+', including all lines in Add File sections."
            }
          },
          "required": ["patch"],
          "additionalProperties": false
        }
        """;

    public async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!ToolArguments.TryGetNonEmptyString(context.Arguments, "patch", out string? patch, trim: false))
        {
            return ToolResultFactory.InvalidArguments(
                "missing_patch",
                "Tool 'apply_patch' requires a non-empty 'patch' string.",
                new ToolRenderPayload(
                    "Invalid apply_patch arguments",
                    "Provide a non-empty 'patch' string."));
        }

        string safePatch;
        try
        {
            safePatch = ResolvePatchPathsFromWorkingDirectory(patch!, context.Session);
        }
        catch (InvalidOperationException exception)
        {
            return ToolResultFactory.InvalidArguments(
                "path_outside_workspace",
                exception.Message,
                new ToolRenderPayload(
                    "Patch rejected",
                    exception.Message));
        }

        WorkspaceApplyPatchExecutionResult executionResult;
        try
        {
            executionResult = await workspaceFileService.ApplyPatchWithTrackingAsync(
                safePatch,
                cancellationToken);
        }
        catch (FormatException exception)
        {
            string repairGuidance = BuildPatchRepairGuidance(exception.Message);
            return ToolResultFactory.InvalidArguments(
                "invalid_patch",
                repairGuidance,
                new ToolRenderPayload(
                    "Patch rejected",
                    repairGuidance));
        }
        if (executionResult.EditTransaction is not null)
        {
            context.Session.RecordFileEditTransaction(executionResult.EditTransaction);
        }
        WorkspaceApplyPatchResult result = executionResult.Result;
        SessionStateToolRecorder.RecordApplyPatch(context.Session, result);

        string renderText = result.Files.Count == 0
            ? "No files changed."
            : string.Join(
                Environment.NewLine,
                result.Files.Select(static file =>
                    file.PreviousPath is null
                        ? $"{file.Operation}: {file.Path} (+{file.AddedLineCount} -{file.RemovedLineCount})"
                        : $"{file.Operation}: {file.PreviousPath} -> {file.Path} (+{file.AddedLineCount} -{file.RemovedLineCount})"));

        return ToolResultFactory.Success(
            $"Applied patch to {result.FileCount} {(result.FileCount == 1 ? "file" : "files")}.",
            result,
            ToolJsonContext.Default.WorkspaceApplyPatchResult,
            new ToolRenderPayload(
                $"Applied patch ({result.FileCount} {(result.FileCount == 1 ? "file" : "files")})",
                renderText));
    }

    private static string ResolvePatchPathsFromWorkingDirectory(
        string patch,
        ReplSessionContext session)
    {
        string[] lines = patch
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.None);

        for (int index = 0; index < lines.Length; index++)
        {
            lines[index] = ResolvePatchHeaderPath(lines[index], "*** Add File:", session);
            lines[index] = ResolvePatchHeaderPath(lines[index], "*** Delete File:", session);
            lines[index] = ResolvePatchHeaderPath(lines[index], "*** Update File:", session);
            lines[index] = ResolvePatchHeaderPath(lines[index], "*** Move to:", session);
        }

        return string.Join("\n", lines);
    }

    private static string ResolvePatchHeaderPath(
        string line,
        string header,
        ReplSessionContext session)
    {
        if (!line.StartsWith(header, StringComparison.Ordinal))
        {
            return line;
        }

        string path = line[header.Length..].Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            return line;
        }

        return header + " " + session.ResolvePathFromWorkingDirectory(path);
    }

    private static string BuildPatchRepairGuidance(string parserMessage)
    {
        string normalizedMessage = string.IsNullOrWhiteSpace(parserMessage)
            ? "Patch text is not valid apply_patch format."
            : parserMessage.Trim();

        string extraHint = normalizedMessage.Contains(
            "Blank lines inside update patches",
            StringComparison.Ordinal)
            ? " To preserve an empty line in the target file, prefix it with a space for context, '+' for an added blank line, or '-' for a removed blank line."
            : normalizedMessage.Contains(
                "Invalid update patch line:",
                StringComparison.Ordinal)
                ? " Inside an update hunk, every file line must start with ' ' for context, '+' for additions, or '-' for removals. The text after '@@' is only a locator; if you want that line to stay, repeat it with a leading space, or use '-' and '+' lines to replace it."
                : string.Empty;

        return
            $"{normalizedMessage} " +
            extraHint +
            "Call apply_patch again with corrected patch text. " +
            "The patch argument must include the complete intended patch, its first non-empty line must be exactly '*** Begin Patch', and its final non-empty line must be exactly '*** End Patch'.";
    }

}
