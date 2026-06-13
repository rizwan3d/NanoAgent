using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;

namespace NanoAgent.Application.Tools;

internal sealed class ApplyPatchTool(IWorkspaceFileService workspaceFileService) : ITool
{
    public string Description => 
    """
        Use the `apply_patch` tool to edit files.

        A valid patch MUST have this exact outer structure:

        *** Begin Patch
        [file operations]
        *** End Patch

        Each file operation MUST start with exactly one action header:

        *** Add File: <path>
        *** Delete File: <path>
        *** Update File: <path>

        Rules:

        1. `*** Add File: <path>`
           - Creates a new file.
           - Every file-content line that follows MUST start with `+`.
           - Lines without `+` are invalid.

        2. `*** Delete File: <path>`
           - Deletes an existing file.
           - No content lines may follow this operation.

        3. `*** Update File: <path>`
            - Edits an existing file.
            - Each edit hunk MUST start with either:
            @@
            or
            @@ <plain anchor text>

            - The `@@` line is ONLY a locator.
            - The `@@` line NEVER removes, adds, or preserves file content.
            - Do NOT put patch content on the `@@` line.

            INVALID:
            @@ -old line
            @@ +new line
            @@  context line
        
            VALID:
            @@
             context line
            -old line
            +new line
        
            VALID with locator:
            @@ public void Save()
             context line
            -old line
            +new line

        - If using `@@ <plain anchor text>`, the anchor text MUST NOT begin with `+`, `-`, or a space.
        - If you want to keep a file line, repeat it inside the hunk with a leading space.
        - If you want to remove a file line, repeat it inside the hunk with a leading `-`.
        - If you want to add a file line, write it inside the hunk with a leading `+`.
        - Every file line inside a hunk MUST start with exactly one of: space, `+`, or `-`.

        - Include unchanged context lines around each change so the edit matches a unique location.
        - If the same context appears more than once, add more context or use a distinctive `@@ <plain anchor text>` locator.
        - Do not use line numbers as a fallback.
        - Do not guess file content. Read the current file before generating the patch.

        - Hunk locator text after `@@` MUST NOT start with `+`, `-`, or a space.
        - These are invalid:
          @@ -old line
          @@ +new line
          @@  context line
          @@ -10,7 +10,6 @@
        - To remove, add, or preserve a line, put it inside the hunk on the following lines with `-`, `+`, or ` `.

    - May include:
     *** Move to: <new path>

    - `*** Move to:` is only valid inside an `*** Update File` operation.

    Optional markers inside an `*** Update File` hunk:

    - `*** End of File` on its own line anchors the preceding hunk to the end of the file.
    - `\ No newline at end of file` directly after a content line indicates the resulting file has no trailing newline.

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
    - New files MUST use `+` at the start of every content line.
    - Delete operations MUST NOT include file content.
    - Patches missing `*** Begin Patch` or `*** End Patch` are invalid.
    - Patches with unknown headers are invalid.
    - Hunk locator lines beginning with `@@ -`, `@@ +`, or `@@  ` are invalid.
    - A failed patch MUST NOT be followed by destructive fallback edits such as line-number deletion.
    - After a failed patch, re-read the file and generate a new patch using exact current context.
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
        catch (Exception exception) when (TryBuildPatchApplyFailureGuidance(exception, out string? repairGuidance))
        {
            return ToolResultFactory.InvalidArguments(
                "patch_apply_failed",
                repairGuidance!,
                new ToolRenderPayload(
                    "Patch rejected",
                    repairGuidance!));
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

    private static bool TryBuildPatchApplyFailureGuidance(
        Exception exception,
        out string? guidance)
    {
        guidance = null;

        string message = GetExceptionMessage(exception);
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        if (ContainsAny(
                message,
                "target context was not found",
                "context",
                "was not found"))
        {
            guidance =
                $"{message} " +
                "Read the current file contents again and rebuild the patch from exact current lines. " +
                "For a focused retry, keep one file section, include a small amount of unchanged surrounding text, " +
                "and then resubmit the full patch.";
            return true;
        }

        if (ContainsAny(
                message,
                "matched multiple locations",
                "matched multiple lines",
                "matched multiple location",
                "matched multiple line"))
        {
            guidance =
                $"{message} " +
                "Make the patch more specific before retrying. Add more unchanged surrounding lines, " +
                "use a more distinctive '@@ ...' locator, or split the edit into smaller file sections " +
                "so only one location matches.";
            return true;
        }

        if (message.Contains("independent replacements overlap", StringComparison.OrdinalIgnoreCase))
        {
            guidance =
                $"{message} " +
                "Split the patch into smaller hunks or rebuild it from the current file contents so each replacement targets a separate, non-overlapping range.";
            return true;
        }

        if (message.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
        {
            guidance =
                $"{message} " +
                "Verify the path and operation before retrying. Use '*** Add File:' for new files, " +
                "'*** Update File:' only for existing files, and re-check the current working directory if the path was relative.";
            return true;
        }

        if (message.Contains("destination already exists", StringComparison.OrdinalIgnoreCase))
        {
            guidance =
                $"{message} " +
                "Choose a different move target, delete the destination first if that is truly intended, " +
                "or keep the edit in place and retry with an update-only patch.";
            return true;
        }

        if (message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            guidance =
                $"{message} " +
                "Use '*** Update File:' for existing files, choose a different path for '*** Add File:', " +
                "or delete the existing file first if replacing it is intended.";
            return true;
        }

        if (message.Contains("excluded by .nanoagent/.nanoignore", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("excluded by .gitignore", StringComparison.OrdinalIgnoreCase))
        {
            guidance =
                $"{message} " +
                "Select a path that is allowed by the workspace ignore rules, or update the ignore rules before retrying if the file should be editable.";
            return true;
        }

        return false;

        static string GetExceptionMessage(Exception exception)
        {
            if (exception is AggregateException aggregateException)
            {
                string aggregateMessages = string.Join(
                    " ",
                    aggregateException
                        .Flatten()
                        .InnerExceptions
                        .Select(GetExceptionMessage)
                        .Where(static value => !string.IsNullOrWhiteSpace(value)));

                if (!string.IsNullOrWhiteSpace(aggregateMessages))
                {
                    return aggregateMessages.Trim();
                }
            }

            string message = exception.Message?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(message))
            {
                return message;
            }

            return exception.InnerException is null
                ? string.Empty
                : GetExceptionMessage(exception.InnerException);
        }

        static bool ContainsAny(string value, params string[] fragments)
        {
            return fragments.Any(fragment =>
                value.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        }
    }

}
