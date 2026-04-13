using System.Text;

namespace NanoAgent;

internal sealed class ApplyPatchToolHandler : IToolHandler
{
    private const string BeginPatchMarker = "*** Begin Patch";
    private const string EndPatchMarker = "*** End Patch";

    public string Name => "apply_patch";

    public ChatToolDefinition Definition => new()
    {
        Function = new ChatToolFunctionDefinition
        {
            Name = Name,
            Description = "Apply a multi-file structured patch using the Codex patch format that begins with '*** Begin Patch'.",
            Parameters = new ChatToolParameters
            {
                AdditionalProperties = false,
                Required = ["patch"],
                Properties = new Dictionary<string, ChatToolParameterProperty>
                {
                    ["patch"] = new()
                    {
                        Type = "string",
                        Description = "The structured patch content to apply. It must begin with '*** Begin Patch' and end with '*** End Patch'.",
                        MinLength = 1
                    }
                }
            }
        }
    };

    public string Execute(ChatToolCall toolCall)
    {
        ApplyPatchToolArguments? arguments = ToolArgumentParser.Parse(
            toolCall,
            Name,
            FileToolJsonContext.Default.ApplyPatchToolArguments,
            out string? errorMessage);

        if (errorMessage is not null)
        {
            return errorMessage;
        }

        if (arguments is null || string.IsNullOrWhiteSpace(arguments.Patch))
        {
            return ToolExecutionResults.Error(Name, "'patch' is required.");
        }

        try
        {
            if (!IsStructuredPatch(arguments.Patch))
            {
                return ToolExecutionResults.Error(Name, "apply_patch requires the structured Codex patch format beginning with '*** Begin Patch'.");
            }

            return ApplyStructuredPatch(arguments.Patch);
        }
        catch (Exception exception)
        {
            return ToolExecutionResults.Error(Name, $"Unable to apply patch. {exception.Message}");
        }
    }

    private static bool IsStructuredPatch(string patch)
    {
        string normalized = ToolRuntime.NormalizeNewlines(patch).TrimStart();
        return normalized.StartsWith(BeginPatchMarker, StringComparison.Ordinal);
    }

    private static string ApplyStructuredPatch(string patch)
    {
        StructuredPatchDocument document = StructuredPatchDocument.Parse(patch);
        List<string> changes = [];

        foreach (PatchOperation operation in document.Operations)
        {
            switch (operation)
            {
                case AddFileOperation addFile:
                    ApplyAddFile(addFile);
                    changes.Add($"ADD {ToolRuntime.ResolvePath(addFile.Path)}");
                    break;

                case DeleteFileOperation deleteFile:
                    ApplyDeleteFile(deleteFile);
                    changes.Add($"DELETE {ToolRuntime.ResolvePath(deleteFile.Path)}");
                    break;

                case UpdateFileOperation updateFile:
                    string updateSummary = ApplyUpdateFile(updateFile);
                    changes.Add(updateSummary);
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported patch operation type: {operation.GetType().Name}");
            }
        }

        return ToolExecutionResults.Success("apply_patch", result =>
        {
            result.Message = changes.Count == 0 ? "Patch applied with no changes." : "Patch applied.";
            result.Changes = changes.ToArray();
        });
    }

    private static void ApplyAddFile(AddFileOperation operation)
    {
        string fullPath = ToolRuntime.ResolvePath(operation.Path);
        if (File.Exists(fullPath))
        {
            throw new InvalidOperationException($"Cannot add file because it already exists: {fullPath}");
        }

        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, operation.Content);
    }

    private static void ApplyDeleteFile(DeleteFileOperation operation)
    {
        string fullPath = ToolRuntime.ResolvePath(operation.Path);
        if (!File.Exists(fullPath))
        {
            throw new InvalidOperationException($"Cannot delete file because it does not exist: {fullPath}");
        }

        File.Delete(fullPath);
    }

    private static string ApplyUpdateFile(UpdateFileOperation operation)
    {
        string sourcePath = ToolRuntime.ResolvePath(operation.Path);
        if (!File.Exists(sourcePath))
        {
            throw new InvalidOperationException($"Cannot update file because it does not exist: {sourcePath}");
        }

        string originalContent = File.ReadAllText(sourcePath);
        string newline = ToolRuntime.DetectPreferredNewline(originalContent);
        string normalizedContent = ToolRuntime.NormalizeNewlines(originalContent);
        string updatedNormalizedContent = StructuredPatchApplicator.Apply(normalizedContent, operation.Hunks);
        string updatedContent = ToolRuntime.RestoreNewlines(updatedNormalizedContent, newline);

        string targetPath = operation.MoveToPath is null
            ? sourcePath
            : ToolRuntime.ResolvePath(operation.MoveToPath);

        if (operation.MoveToPath is not null
            && !string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase)
            && File.Exists(targetPath))
        {
            throw new InvalidOperationException($"Cannot move file because the target already exists: {targetPath}");
        }

        string? targetDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        File.WriteAllText(targetPath, updatedContent);

        if (operation.MoveToPath is not null
            && !string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(sourcePath);
            return $"MOVE {sourcePath} -> {targetPath}";
        }

        return $"UPDATE {sourcePath}";
    }

    private abstract record PatchOperation(string Path);

    private sealed record AddFileOperation(string Path, string Content) : PatchOperation(Path);

    private sealed record DeleteFileOperation(string Path) : PatchOperation(Path);

    private sealed record UpdateFileOperation(string Path, string? MoveToPath, IReadOnlyList<PatchHunk> Hunks) : PatchOperation(Path);

    private sealed record PatchHunk(IReadOnlyList<PatchLine> Lines);

    private sealed record PatchLine(PatchLineKind Kind, string Text);

    private enum PatchLineKind
    {
        Context,
        Remove,
        Add
    }

    private sealed class StructuredPatchDocument
    {
        public required IReadOnlyList<PatchOperation> Operations { get; init; }

        public static StructuredPatchDocument Parse(string patch)
        {
            string normalized = ToolRuntime.NormalizeNewlines(patch);
            List<string> lines = normalized.Split('\n').ToList();

            if (lines.Count == 0 || !string.Equals(lines[0], BeginPatchMarker, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Structured patch must start with '*** Begin Patch'.");
            }

            int endIndex = lines.FindLastIndex(line => string.Equals(line, EndPatchMarker, StringComparison.Ordinal));
            if (endIndex < 0)
            {
                throw new InvalidOperationException("Structured patch must end with '*** End Patch'.");
            }

            List<PatchOperation> operations = [];
            int index = 1;

            while (index < endIndex)
            {
                string line = lines[index];

                if (string.IsNullOrWhiteSpace(line))
                {
                    index++;
                    continue;
                }

                if (line.StartsWith("*** Add File: ", StringComparison.Ordinal))
                {
                    operations.Add(ParseAddFile(lines, ref index, endIndex));
                    continue;
                }

                if (line.StartsWith("*** Delete File: ", StringComparison.Ordinal))
                {
                    operations.Add(ParseDeleteFile(lines, ref index));
                    continue;
                }

                if (line.StartsWith("*** Update File: ", StringComparison.Ordinal))
                {
                    operations.Add(ParseUpdateFile(lines, ref index, endIndex));
                    continue;
                }

                throw new InvalidOperationException($"Unexpected line in structured patch: {line}");
            }

            return new StructuredPatchDocument
            {
                Operations = operations
            };
        }

        private static AddFileOperation ParseAddFile(IReadOnlyList<string> lines, ref int index, int endIndex)
        {
            string path = lines[index]["*** Add File: ".Length..].Trim();
            index++;
            List<string> contentLines = [];

            while (index < endIndex && !IsOperationHeader(lines[index]))
            {
                string line = lines[index];
                if (!line.StartsWith("+", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Add File lines must start with '+'. Invalid line: {line}");
                }

                contentLines.Add(line[1..]);
                index++;
            }

            return new AddFileOperation(path, string.Join('\n', contentLines));
        }

        private static DeleteFileOperation ParseDeleteFile(IReadOnlyList<string> lines, ref int index)
        {
            string path = lines[index]["*** Delete File: ".Length..].Trim();
            index++;
            return new DeleteFileOperation(path);
        }

        private static UpdateFileOperation ParseUpdateFile(IReadOnlyList<string> lines, ref int index, int endIndex)
        {
            string path = lines[index]["*** Update File: ".Length..].Trim();
            index++;

            string? moveToPath = null;
            if (index < endIndex && lines[index].StartsWith("*** Move to: ", StringComparison.Ordinal))
            {
                moveToPath = lines[index]["*** Move to: ".Length..].Trim();
                index++;
            }

            List<PatchHunk> hunks = [];
            List<PatchLine>? currentHunk = null;

            while (index < endIndex && !IsOperationHeader(lines[index]))
            {
                string line = lines[index];

                if (line.StartsWith("@@", StringComparison.Ordinal))
                {
                    if (currentHunk is not null && currentHunk.Count > 0)
                    {
                        hunks.Add(new PatchHunk(currentHunk));
                    }

                    currentHunk = [];
                    index++;
                    continue;
                }

                if (string.Equals(line, "*** End of File", StringComparison.Ordinal))
                {
                    index++;
                    continue;
                }

                currentHunk ??= [];
                currentHunk.Add(ParsePatchLine(line));
                index++;
            }

            if (currentHunk is not null && currentHunk.Count > 0)
            {
                hunks.Add(new PatchHunk(currentHunk));
            }

            return new UpdateFileOperation(path, moveToPath, hunks);
        }

        private static PatchLine ParsePatchLine(string line)
        {
            if (line.StartsWith(" ", StringComparison.Ordinal))
            {
                return new PatchLine(PatchLineKind.Context, line[1..]);
            }

            if (line.StartsWith("-", StringComparison.Ordinal))
            {
                return new PatchLine(PatchLineKind.Remove, line[1..]);
            }

            if (line.StartsWith("+", StringComparison.Ordinal))
            {
                return new PatchLine(PatchLineKind.Add, line[1..]);
            }

            throw new InvalidOperationException($"Invalid patch line: {line}");
        }

        private static bool IsOperationHeader(string line) =>
            line.StartsWith("*** Add File: ", StringComparison.Ordinal)
            || line.StartsWith("*** Delete File: ", StringComparison.Ordinal)
            || line.StartsWith("*** Update File: ", StringComparison.Ordinal)
            || string.Equals(line, EndPatchMarker, StringComparison.Ordinal);
    }

    private static class StructuredPatchApplicator
    {
        public static string Apply(string content, IReadOnlyList<PatchHunk> hunks)
        {
            List<string> sourceLines = SplitLines(content);
            List<string> output = [];
            int sourceIndex = 0;

            foreach (PatchHunk hunk in hunks)
            {
                int matchIndex = FindMatchIndex(sourceLines, sourceIndex, hunk);
                if (matchIndex < 0)
                {
                    throw new InvalidOperationException("Failed to locate hunk context while applying structured patch.");
                }

                for (int i = sourceIndex; i < matchIndex; i++)
                {
                    output.Add(sourceLines[i]);
                }

                int cursor = matchIndex;
                foreach (PatchLine line in hunk.Lines)
                {
                    switch (line.Kind)
                    {
                        case PatchLineKind.Context:
                            EnsureLineMatches(sourceLines, cursor, line.Text, "context");
                            output.Add(sourceLines[cursor]);
                            cursor++;
                            break;

                        case PatchLineKind.Remove:
                            EnsureLineMatches(sourceLines, cursor, line.Text, "removal");
                            cursor++;
                            break;

                        case PatchLineKind.Add:
                            output.Add(line.Text);
                            break;
                    }
                }

                sourceIndex = cursor;
            }

            for (int i = sourceIndex; i < sourceLines.Count; i++)
            {
                output.Add(sourceLines[i]);
            }

            return string.Join('\n', output);
        }

        private static int FindMatchIndex(IReadOnlyList<string> sourceLines, int startIndex, PatchHunk hunk)
        {
            List<PatchLine> anchorLines = hunk.Lines
                .Where(line => line.Kind is PatchLineKind.Context or PatchLineKind.Remove)
                .ToList();

            if (anchorLines.Count == 0)
            {
                return startIndex;
            }

            for (int candidate = startIndex; candidate <= sourceLines.Count - anchorLines.Count; candidate++)
            {
                bool matches = true;
                for (int i = 0; i < anchorLines.Count; i++)
                {
                    if (!string.Equals(sourceLines[candidate + i], anchorLines[i].Text, StringComparison.Ordinal))
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    return candidate;
                }
            }

            return -1;
        }

        private static void EnsureLineMatches(IReadOnlyList<string> sourceLines, int index, string expected, string role)
        {
            if (index >= sourceLines.Count)
            {
                throw new InvalidOperationException($"Structured patch {role} exceeded the end of the file.");
            }

            if (!string.Equals(sourceLines[index], expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Structured patch {role} mismatch. Expected '{expected}' but found '{sourceLines[index]}'.");
            }
        }

        private static List<string> SplitLines(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return [];
            }

            List<string> lines = content.Split('\n').ToList();
            if (lines.Count > 0 && lines[^1].Length == 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            return lines;
        }
    }
}
