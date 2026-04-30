using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;

namespace NanoAgent.Application.Tools;

internal sealed class ApplyPatchTool(IWorkspaceFileService workspaceFileService) : ITool
{
    public string Description => "Apply a focused multi-file patch from the current session working directory within the workspace. Patch text must start with *** Begin Patch and end with *** End Patch.";

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
              "description": "Patch text using the apply_patch format. File paths in patch headers are relative to the current session working directory. The first non-empty line must be exactly *** Begin Patch and the final non-empty line must be exactly *** End Patch."
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
            lines[index] = ResolvePatchHeaderPath(lines[index], "*** Add File: ", session);
            lines[index] = ResolvePatchHeaderPath(lines[index], "*** Delete File: ", session);
            lines[index] = ResolvePatchHeaderPath(lines[index], "*** Update File: ", session);
            lines[index] = ResolvePatchHeaderPath(lines[index], "*** Move to: ", session);
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

        return header + session.ResolvePathFromWorkingDirectory(path);
    }

    private static string BuildPatchRepairGuidance(string parserMessage)
    {
        string normalizedMessage = string.IsNullOrWhiteSpace(parserMessage)
            ? "Patch text is not valid apply_patch format."
            : parserMessage.Trim();

        return
            $"{normalizedMessage} " +
            "Call apply_patch again with corrected patch text. " +
            "The patch argument must include the complete intended patch, its first non-empty line must be exactly '*** Begin Patch', and its final non-empty line must be exactly '*** End Patch'.";
    }

}
