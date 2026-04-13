using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace NanoAgent;

internal sealed class OpenAiCompatibleToolCallPresenter
{
    private readonly IChatConsole _chatConsole;
    private readonly AppRuntimeOptions _runtimeOptions;

    public OpenAiCompatibleToolCallPresenter(IChatConsole chatConsole, AppRuntimeOptions runtimeOptions)
    {
        _chatConsole = chatConsole;
        _runtimeOptions = runtimeOptions;
    }

    public void RenderBeforeExecution(ChatToolCall toolCall)
    {
        MaybeRenderMutedToolCall(toolCall);
        MaybeRenderUserFacingCommand(toolCall);
        WriteVerbose(FormatToolCallVerboseMessage(toolCall));
    }

    public void RenderAfterExecution(ChatToolCall toolCall, string toolResult)
    {
        MaybeRenderUserFacingFileChange(toolCall, toolResult);
        WriteVerbose(FormatToolResultVerboseMessage(toolCall, toolResult));
    }

    private void WriteVerbose(string message)
    {
        if (!_runtimeOptions.Verbose)
        {
            return;
        }

        _chatConsole.RenderVerboseMessage(message);
    }

    private void MaybeRenderUserFacingCommand(ChatToolCall toolCall)
    {
        if (_runtimeOptions.Verbose)
        {
            return;
        }

        if (!string.Equals(toolCall.Function.Name, "run_command", StringComparison.Ordinal))
        {
            return;
        }

        string? command = TryReadJsonStringProperty(toolCall.Function.Arguments, "command");
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        _chatConsole.RenderCommandMessage(command);
    }

    private void MaybeRenderMutedToolCall(ChatToolCall toolCall)
    {
        if (_runtimeOptions.Verbose)
        {
            return;
        }

        if (string.Equals(toolCall.Function.Name, "run_command", StringComparison.Ordinal)
            || string.Equals(toolCall.Function.Name, "write_file", StringComparison.Ordinal)
            || string.Equals(toolCall.Function.Name, "edit_file", StringComparison.Ordinal)
            || string.Equals(toolCall.Function.Name, "apply_patch", StringComparison.Ordinal))
        {
            return;
        }

        _chatConsole.RenderMutedToolCall(toolCall.Function.Name);
    }

    private void MaybeRenderUserFacingFileChange(ChatToolCall toolCall, string toolResult)
    {
        if (_runtimeOptions.Verbose || IsToolError(toolResult))
        {
            return;
        }

        switch (toolCall.Function.Name)
        {
            case "write_file":
                RenderWriteFile(toolCall);
                break;
            case "edit_file":
                RenderEditFile(toolCall, toolResult);
                break;
            case "apply_patch":
                RenderApplyPatch(toolCall);
                break;
        }
    }

    private void RenderWriteFile(ChatToolCall toolCall)
    {
        WriteFileToolArguments? arguments = ParseToolArguments(
            toolCall.Function.Arguments,
            FileToolJsonContext.Default.WriteFileToolArguments);

        if (arguments is null || string.IsNullOrWhiteSpace(arguments.Path))
        {
            return;
        }

        string displayPath = ToDisplayPath(arguments.Path);
        List<FilePreviewLine> previewLines = BuildPreviewLines(arguments.Content ?? string.Empty, 1, out int hiddenLineCount);
        int writtenLines = CountLines(arguments.Content ?? string.Empty);

        _chatConsole.RenderFileOperationMessage(
            "Write",
            displayPath,
            $"Wrote {writtenLines} lines to {displayPath}",
            previewLines,
            hiddenLineCount);
    }

    private void RenderEditFile(ChatToolCall toolCall, string toolResult)
    {
        EditFileToolArguments? arguments = ParseToolArguments(
            toolCall.Function.Arguments,
            FileToolJsonContext.Default.EditFileToolArguments);

        if (arguments is null || string.IsNullOrWhiteSpace(arguments.Path))
        {
            return;
        }

        string displayPath = ToDisplayPath(arguments.Path);
        string diff = TryParseToolResult(toolResult)?.Diff ?? string.Empty;
        List<FilePreviewLine> previewLines = BuildDiffPreviewLines(diff, out int hiddenLineCount);

        _chatConsole.RenderFileOperationMessage(
            "Edit",
            displayPath,
            $"Edited {displayPath}",
            previewLines,
            hiddenLineCount);
    }

    private void RenderApplyPatch(ChatToolCall toolCall)
    {
        ApplyPatchToolArguments? arguments = ParseToolArguments(
            toolCall.Function.Arguments,
            FileToolJsonContext.Default.ApplyPatchToolArguments);

        if (arguments is null || string.IsNullOrWhiteSpace(arguments.Patch))
        {
            return;
        }

        List<PatchFilePreview> previews = BuildPatchPreviews(arguments.Patch);
        if (previews.Count == 0)
        {
            _chatConsole.RenderFileOperationMessage("ApplyPatch", "<multiple files>", "Applied patch", [], 0);
            return;
        }

        foreach (PatchFilePreview preview in previews.Take(3))
        {
            _chatConsole.RenderFileOperationMessage(
                "ApplyPatch",
                preview.Path,
                preview.Summary,
                preview.Lines,
                preview.HiddenLineCount);
        }
    }

    private static T? ParseToolArguments<T>(string json, JsonTypeInfo<T> typeInfo) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize(json, typeInfo);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<FilePreviewLine> BuildPreviewLines(string content, int startLineNumber, out int hiddenLineCount)
    {
        string[] lines = NormalizePreviewContent(content);
        int previewCount = Math.Min(lines.Length, 10);
        List<FilePreviewLine> previewLines = new(previewCount);

        for (int i = 0; i < previewCount; i++)
        {
            previewLines.Add(new FilePreviewLine(startLineNumber + i, lines[i]));
        }

        hiddenLineCount = Math.Max(0, lines.Length - previewCount);
        return previewLines;
    }

    private static List<FilePreviewLine> BuildDiffPreviewLines(string diff, out int hiddenLineCount)
    {
        if (string.IsNullOrWhiteSpace(diff))
        {
            hiddenLineCount = 0;
            return [];
        }

        string[] lines = diff.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        List<FilePreviewLine> previewLines = [];

        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith("diff --git ", StringComparison.Ordinal)
                || line.StartsWith("index ", StringComparison.Ordinal)
                || line.StartsWith("--- ", StringComparison.Ordinal)
                || line.StartsWith("+++ ", StringComparison.Ordinal))
            {
                continue;
            }

            if (previewLines.Count >= 10)
            {
                break;
            }

            if (line.StartsWith("@@", StringComparison.Ordinal)
                || (line.StartsWith("+", StringComparison.Ordinal) && !line.StartsWith("+++", StringComparison.Ordinal))
                || (line.StartsWith("-", StringComparison.Ordinal) && !line.StartsWith("---", StringComparison.Ordinal))
                || line.StartsWith(" ", StringComparison.Ordinal))
            {
                previewLines.Add(new FilePreviewLine(null, line));
            }
        }

        hiddenLineCount = Math.Max(0, lines.Count(line =>
            line.StartsWith("@@", StringComparison.Ordinal)
            || (line.StartsWith("+", StringComparison.Ordinal) && !line.StartsWith("+++", StringComparison.Ordinal))
            || (line.StartsWith("-", StringComparison.Ordinal) && !line.StartsWith("---", StringComparison.Ordinal))
            || line.StartsWith(" ", StringComparison.Ordinal)) - previewLines.Count);

        return previewLines;
    }

    private static List<PatchFilePreview> BuildPatchPreviews(string patch)
    {
        List<PatchFilePreview> previews = [];
        string[] lines = patch.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        PatchFilePreviewBuilder? current = null;
        string currentOperation = "ApplyPatch";
        int lineNumber = 1;

        foreach (string line in lines)
        {
            if (line.StartsWith("*** Add File: ", StringComparison.Ordinal))
            {
                if (current is not null)
                {
                    previews.Add(current.Build());
                }

                currentOperation = "Add";
                string path = line["*** Add File: ".Length..].Trim();
                current = new PatchFilePreviewBuilder(path, $"Added {path}");
                lineNumber = 1;
                continue;
            }

            if (line.StartsWith("*** Delete File: ", StringComparison.Ordinal))
            {
                if (current is not null)
                {
                    previews.Add(current.Build());
                }

                string path = line["*** Delete File: ".Length..].Trim();
                previews.Add(new PatchFilePreview(path, $"Deleted {path}", [], 0));
                current = null;
                currentOperation = "Delete";
                continue;
            }

            if (line.StartsWith("*** Update File: ", StringComparison.Ordinal))
            {
                if (current is not null)
                {
                    previews.Add(current.Build());
                }

                currentOperation = "Update";
                string path = line["*** Update File: ".Length..].Trim();
                current = new PatchFilePreviewBuilder(path, $"Updated {path}");
                lineNumber = 1;
                continue;
            }

            if (line.StartsWith("*** Move to: ", StringComparison.Ordinal))
            {
                if (current is not null)
                {
                    string moveTarget = line["*** Move to: ".Length..].Trim();
                    current.SetSummary($"Moved {current.Path} -> {moveTarget}");
                }

                continue;
            }

            if (current is null)
            {
                continue;
            }

            if (line.StartsWith("@@", StringComparison.Ordinal) || string.Equals(line, "*** End of File", StringComparison.Ordinal))
            {
                continue;
            }

            if (currentOperation == "Add" && line.StartsWith("+", StringComparison.Ordinal))
            {
                current.AddLine(lineNumber++, line[1..]);
                continue;
            }

            if ((currentOperation == "Update" || currentOperation == "ApplyPatch")
                && (line.StartsWith(" ", StringComparison.Ordinal)
                    || (line.StartsWith("+", StringComparison.Ordinal) && !line.StartsWith("+++", StringComparison.Ordinal))
                    || (line.StartsWith("-", StringComparison.Ordinal) && !line.StartsWith("---", StringComparison.Ordinal))))
            {
                current.AddLine(null, line);
            }
        }

        if (current is not null)
        {
            previews.Add(current.Build());
        }

        return previews.Where(preview => !string.IsNullOrWhiteSpace(preview.Path)).ToList();
    }

    private static string SummarizeToolResult(string toolResult)
    {
        ToolExecutionResult? parsed = TryParseToolResult(toolResult);
        if (parsed is not null)
        {
            if (string.Equals(parsed.Tool, "run_command", StringComparison.Ordinal))
            {
                return $"tool={parsed.Tool} exit_code={parsed.ExitCode} command={parsed.Command}";
            }

            return $"tool={parsed.Tool} status={parsed.Status}" +
                   (string.IsNullOrWhiteSpace(parsed.Message) ? string.Empty : $" message={parsed.Message}");
        }

        string normalized = toolResult.Replace("\r\n", "\n", StringComparison.Ordinal);
        string firstLine = normalized.Split('\n', 2)[0];
        return firstLine.Length <= 120 ? firstLine : firstLine[..117] + "...";
    }

    private static string FormatToolCallVerboseMessage(ChatToolCall toolCall)
    {
        if (!string.Equals(toolCall.Function.Name, "run_command", StringComparison.Ordinal))
        {
            return $"tool call: {toolCall.Function.Name} {toolCall.Function.Arguments}";
        }

        string command = TryReadJsonStringProperty(toolCall.Function.Arguments, "command") ?? toolCall.Function.Arguments;
        return $"tool call: run_command\ncommand: {command}\narguments: {toolCall.Function.Arguments}";
    }

    private static string FormatToolResultVerboseMessage(ChatToolCall toolCall, string toolResult)
    {
        ToolExecutionResult? parsed = TryParseToolResult(toolResult);
        if (string.Equals(toolCall.Function.Name, "run_command", StringComparison.Ordinal) && parsed is not null)
        {
            string stdout = parsed.Stdout ?? "<empty>";
            string stderr = parsed.Stderr ?? "<empty>";

            return
                "tool result:\n" +
                $"COMMAND: {parsed.Command}\n" +
                $"SHELL: {parsed.Shell}\n" +
                $"EXECUTED: {parsed.Executed}\n" +
                $"WORKDIR: {parsed.Workdir}\n" +
                $"EXIT_CODE: {parsed.ExitCode}\n" +
                $"STDOUT:\n{stdout}\n" +
                $"STDERR:\n{stderr}";
        }

        return $"tool result: {SummarizeToolResult(toolResult)}";
    }

    private static bool IsToolError(string toolResult)
    {
        ToolExecutionResult? parsed = TryParseToolResult(toolResult);
        return parsed is not null
            ? string.Equals(parsed.Status, "error", StringComparison.OrdinalIgnoreCase)
            : toolResult.StartsWith("Tool error:", StringComparison.Ordinal);
    }

    private static ToolExecutionResult? TryParseToolResult(string toolResult) =>
        ToolExecutionResults.TryParse(toolResult, out ToolExecutionResult? parsed) ? parsed : null;

    private static string[] NormalizePreviewContent(string content) =>
        content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

    private static int CountLines(string content) => NormalizePreviewContent(content).Length;

    private static string ToDisplayPath(string path)
    {
        string fullPath = ToolRuntime.ResolvePath(path);
        string currentDirectory = Path.GetFullPath(Environment.CurrentDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (fullPath.StartsWith(currentDirectory, StringComparison.OrdinalIgnoreCase))
        {
            string relativePath = Path.GetRelativePath(currentDirectory, fullPath);
            return relativePath.Replace('/', Path.DirectorySeparatorChar);
        }

        return fullPath;
    }

    private static string? TryReadJsonStringProperty(string json, string propertyName)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!document.RootElement.TryGetProperty(propertyName, out JsonElement property))
            {
                return null;
            }

            return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record PatchFilePreview(
        string Path,
        string Summary,
        List<FilePreviewLine> Lines,
        int HiddenLineCount);

    private sealed class PatchFilePreviewBuilder
    {
        private readonly string _path;
        private string _summary;
        private readonly List<FilePreviewLine> _lines = [];
        private int _hiddenLineCount;

        public PatchFilePreviewBuilder(string path, string? summary = null)
        {
            _path = path;
            _summary = string.IsNullOrWhiteSpace(summary) ? $"Applied patch to {path}" : summary;
        }

        public string Path => _path;

        public void AddLine(int? lineNumber, string text)
        {
            if (_lines.Count < 10)
            {
                _lines.Add(new FilePreviewLine(lineNumber, text));
                return;
            }

            _hiddenLineCount++;
        }

        public void SetSummary(string summary)
        {
            _summary = summary;
        }

        public PatchFilePreview Build() =>
            new(_path, _summary, _lines, _hiddenLineCount);
    }
}
