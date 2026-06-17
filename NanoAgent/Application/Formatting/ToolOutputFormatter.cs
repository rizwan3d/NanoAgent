using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Utilities;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace NanoAgent.Application.Formatting;

public interface IToolOutputFormatter
{
    string DescribeCall(ConversationToolCall toolCall);

    string FormatCallPreview(ConversationToolCall toolCall);

    IReadOnlyList<string> FormatResults(ToolExecutionBatchResult toolExecutionResult);
}

public sealed class ToolOutputFormatter : IToolOutputFormatter
{
    private const int MaxShellPreviewLines = 5;
    private const int MaxToolPreviewLines = 8;
    private const int MaxWebPreviewLines = 12;
    private const int MaxSavedCallPreviewLines = 10;
    private const string Bullet = "\u2022";

    public string DescribeCall(ConversationToolCall toolCall)
    {
        string name = toolCall.Name.Trim();

        string description = name switch
        {
            "shell_command" when TryGetArgumentString(toolCall.ArgumentsJson, "command", out string command) =>
                $"command: {Truncate(ShellCommandText.NormalizeCommandText(command), 120)}",
            "file_read" when TryGetArgumentString(toolCall.ArgumentsJson, "path", out string path) =>
                $"file read: {path}",
            "file_delete" when TryGetArgumentString(toolCall.ArgumentsJson, "path", out string path) =>
                $"file delete: {path}",
            "directory_list" when TryGetArgumentString(toolCall.ArgumentsJson, "path", out string path) =>
                $"directory list: {path}",
            "directory_list" => "directory list",
            "search_files" when TryGetArgumentString(toolCall.ArgumentsJson, "query", out string query) =>
                $"file search: \"{query}\"",
            "text_search" when TryGetArgumentString(toolCall.ArgumentsJson, "query", out string query) =>
                $"text search: \"{query}\"",
            "file_write" when TryGetArgumentString(toolCall.ArgumentsJson, "path", out string path) =>
                $"file write: {path}",
            "codebase_index" when TryGetArgumentString(toolCall.ArgumentsJson, "query", out string indexQuery) =>
                $"codebase index: \"{indexQuery}\"",
            "codebase_index" when TryGetArgumentString(toolCall.ArgumentsJson, "action", out string indexAction) =>
                $"codebase index: {indexAction}",
            "headless_browser" when TryGetArgumentString(toolCall.ArgumentsJson, "url", out string browserUrl) =>
                $"headless browser: {browserUrl}",
            "agent_delegate" when TryGetArgumentString(toolCall.ArgumentsJson, "agent", out string agent) =>
                $"subagent: {agent}",
            "agent_orchestrate" when TryGetArgumentArrayCount(toolCall.ArgumentsJson, "tasks", out int taskCount) =>
                $"subagent orchestration: {taskCount} {(taskCount == 1 ? "task" : "tasks")}",
            "repo_memory" when TryGetArgumentString(toolCall.ArgumentsJson, "document", out string memoryDocument) =>
                $"repo memory: {memoryDocument}",
            "web_search" => DescribeWebSearchCall(toolCall.ArgumentsJson),
            _ => name
        };

        return SecretRedactor.Redact(description);
    }

    public string FormatCallPreview(ConversationToolCall toolCall)
    {
        ArgumentNullException.ThrowIfNull(toolCall);

        string description = DescribeCall(toolCall);
        string title = string.IsNullOrWhiteSpace(description)
            ? toolCall.Name.Trim()
            : description.Trim();
        IReadOnlyList<string> previewLines = BuildSavedCallPreviewLines(toolCall);

        StringBuilder builder = new();
        builder
            .Append(Bullet)
            .Append(" Previewed saved tool call: ")
            .Append(title);

        builder.AppendLine();
        builder.Append("  - preview:");

        List<string> lines = [
            "result output was not stored in this older section; showing saved tool arguments."
        ];
        lines.AddRange(previewLines);

        int displayedLineCount = Math.Min(MaxSavedCallPreviewLines, lines.Count);
        for (int index = 0; index < displayedLineCount; index++)
        {
            builder
                .AppendLine()
                .Append("      ")
                .Append(lines[index]);
        }

        if (lines.Count > displayedLineCount)
        {
            builder
                .AppendLine()
                .Append("    ... +")
                .Append(lines.Count - displayedLineCount)
                .Append(" lines");
        }

        return SecretRedactor.Redact(builder.ToString());
    }

    public IReadOnlyList<string> FormatResults(ToolExecutionBatchResult toolExecutionResult)
    {
        ArgumentNullException.ThrowIfNull(toolExecutionResult);

        List<string> messages = [];
        List<FileEditDisplayResult> fileEditBatch = [];

        foreach (ToolInvocationResult result in toolExecutionResult.Results)
        {
            if (IsSuccessfulPlanUpdate(result))
            {
                continue;
            }

            if (CanGroupFileEdit(result, out FileEditDisplayResult fileEdit))
            {
                fileEditBatch.Add(fileEdit);
                continue;
            }

            FlushFileEditBatch(messages, fileEditBatch);
            messages.Add(SecretRedactor.Redact(BuildToolResultMessage(result)));
        }

        FlushFileEditBatch(messages, fileEditBatch);
        return messages
            .Select(static message => SecretRedactor.Redact(message))
            .ToArray();
    }

    private static string DescribeWebSearchCall(string argumentsJson)
    {
        if (TryGetFirstArrayObjectString(argumentsJson, "search_query", "q", out string query))
        {
            return $"web search: \"{query}\"";
        }

        return "web_search";
    }

    private static IReadOnlyList<string> BuildSavedCallPreviewLines(ConversationToolCall toolCall)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(toolCall.ArgumentsJson);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return [$"arguments: {Truncate(root.ToString(), 180)}"];
            }

            List<string> lines = [];
            switch (toolCall.Name)
            {
                case "shell_command":
                    AddNamedArgumentLine(root, lines, "command");
                    AddNamedArgumentLine(root, lines, "workingDirectory");
                    AddNamedArgumentLine(root, lines, "sandbox_permissions");
                    AddNamedArgumentLine(root, lines, "justification");
                    break;

                case "file_read":
                case "file_delete":
                    AddNamedArgumentLine(root, lines, "path");
                    break;

                case "directory_list":
                    AddNamedArgumentLine(root, lines, "path");
                    AddNamedArgumentLine(root, lines, "recursive");
                    break;

                case "search_files":
                case "text_search":
                    AddNamedArgumentLine(root, lines, "query");
                    AddNamedArgumentLine(root, lines, "path");
                    if (string.Equals(toolCall.Name, "search_files", StringComparison.Ordinal))
                    {
                        AddNamedArgumentLine(root, lines, "mode");
                        AddNamedArgumentLine(root, lines, "caseSensitive");
                        AddNamedArgumentLine(root, lines, "regex");
                        AddNamedArgumentLine(root, lines, "wholeWord");
                        AddNamedArgumentLine(root, lines, "glob");
                        AddNamedArgumentLine(root, lines, "includeGlobs");
                        AddNamedArgumentLine(root, lines, "excludeGlobs");
                        AddNamedArgumentLine(root, lines, "fuzzy");
                        AddNamedArgumentLine(root, lines, "offset");
                        AddNamedArgumentLine(root, lines, "cursor");
                        AddNamedArgumentLine(root, lines, "includeHidden");
                        AddNamedArgumentLine(root, lines, "includeGenerated");
                        AddNamedArgumentLine(root, lines, "includeIgnored");
                        AddNamedArgumentLine(root, lines, "limit");
                    }
                    break;

                case "file_write":
                    AddNamedArgumentLine(root, lines, "path");
                    AddNamedArgumentLine(root, lines, "overwrite");
                    AddContentArgumentPreview(root, lines, "content", "content");
                    break;

                case "apply_patch":
                    AddContentArgumentPreview(root, lines, "patch", "patch");
                    break;

                case "codebase_index":
                    AddNamedArgumentLine(root, lines, "action");
                    AddNamedArgumentLine(root, lines, "query");
                    AddNamedArgumentLine(root, lines, "limit");
                    AddNamedArgumentLine(root, lines, "includeSnippets");
                    AddNamedArgumentLine(root, lines, "force");
                    break;

                case "agent_delegate":
                    AddNamedArgumentLine(root, lines, "agent");
                    AddNamedArgumentLine(root, lines, "task");
                    AddNamedArgumentLine(root, lines, "context");
                    break;

                case "agent_orchestrate":
                    AddArrayArgumentSummary(root, lines, "tasks");
                    break;

                case "repo_memory":
                    AddNamedArgumentLine(root, lines, "action");
                    AddNamedArgumentLine(root, lines, "document");
                    AddContentArgumentPreview(root, lines, "content", "content");
                    break;

                default:
                    AddGenericArgumentPreviewLines(root, lines);
                    break;
            }

            if (lines.Count == 0)
            {
                AddGenericArgumentPreviewLines(root, lines);
            }

            return lines.Count == 0
                ? ["arguments: {}"]
                : lines;
        }
        catch (JsonException)
        {
            return [$"arguments: {Truncate(toolCall.ArgumentsJson, 180)}"];
        }
    }

    private static void AddNamedArgumentLine(
        JsonElement root,
        List<string> lines,
        string propertyName)
    {
        if (!TryGetJsonProperty(root, propertyName, out JsonElement property))
        {
            return;
        }

        lines.Add($"{propertyName}: {FormatJsonValuePreview(property)}");
    }

    private static void AddContentArgumentPreview(
        JsonElement root,
        List<string> lines,
        string propertyName,
        string label)
    {
        if (!TryGetJsonProperty(root, propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return;
        }

        string content = property.GetString() ?? string.Empty;
        string[] previewLines = SplitPreviewLines(content);
        lines.Add($"{label}: {content.Length} chars");

        if (previewLines.Length == 0)
        {
            lines.Add("   (empty)");
            return;
        }

        int displayedLineCount = Math.Min(MaxToolPreviewLines, previewLines.Length);
        for (int index = 0; index < displayedLineCount; index++)
        {
            lines.Add(
                $"{(index + 1).ToString(CultureInfo.InvariantCulture).PadLeft(4)} {Truncate(previewLines[index], 180)}");
        }

        if (previewLines.Length > displayedLineCount)
        {
            lines.Add($"... +{previewLines.Length - displayedLineCount} lines");
        }
    }

    private static void AddArrayArgumentSummary(
        JsonElement root,
        List<string> lines,
        string propertyName)
    {
        if (!TryGetJsonProperty(root, propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        lines.Add($"{propertyName}: {property.GetArrayLength()} item(s)");

        int index = 0;
        foreach (JsonElement item in property.EnumerateArray())
        {
            if (index >= Math.Min(MaxToolPreviewLines, property.GetArrayLength()))
            {
                break;
            }

            lines.Add($"   {index + 1}: {FormatJsonValuePreview(item)}");
            index++;
        }

        if (property.GetArrayLength() > index)
        {
            lines.Add($"... +{property.GetArrayLength() - index} items");
        }
    }

    private static void AddGenericArgumentPreviewLines(JsonElement root, List<string> lines)
    {
        int addedCount = 0;
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (addedCount >= MaxSavedCallPreviewLines)
            {
                break;
            }

            lines.Add($"{property.Name}: {FormatJsonValuePreview(property.Value)}");
            addedCount++;
        }

        int remainingCount = root.EnumerateObject().Count() - addedCount;
        if (remainingCount > 0)
        {
            lines.Add($"... +{remainingCount} arguments");
        }
    }

    private static string FormatJsonValuePreview(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => Truncate(value.GetString() ?? string.Empty, 180),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            JsonValueKind.Array => FormatJsonArrayPreview(value),
            JsonValueKind.Object => FormatJsonObjectPreview(value),
            _ => Truncate(value.ToString(), 180)
        };
    }

    private static string FormatJsonArrayPreview(JsonElement array)
    {
        int count = array.GetArrayLength();
        JsonElement? firstItem = null;
        foreach (JsonElement item in array.EnumerateArray())
        {
            firstItem = item;
            break;
        }

        string suffix = firstItem is null
            ? string.Empty
            : $"; first: {FormatJsonValuePreview(firstItem.Value)}";
        return $"[{count} item(s){suffix}]";
    }

    private static string FormatJsonObjectPreview(JsonElement value)
    {
        string[] properties = value
            .EnumerateObject()
            .Where(static property => property.Value.ValueKind is
                JsonValueKind.String or
                JsonValueKind.Number or
                JsonValueKind.True or
                JsonValueKind.False or
                JsonValueKind.Null)
            .Take(3)
            .Select(static property => $"{property.Name}: {FormatJsonValuePreview(property.Value)}")
            .ToArray();

        return properties.Length == 0
            ? "{...}"
            : "{ " + string.Join(", ", properties) + " }";
    }

    private static string BuildToolResultMessage(ToolInvocationResult invocationResult)
    {
        if (TryBuildShellCommandResultMessage(invocationResult, out string shellMessage))
        {
            return shellMessage;
        }

        if (TryBuildApplyPatchResultMessage(invocationResult, out string patchMessage))
        {
            return patchMessage;
        }

        if (TryBuildFileReadResultMessage(invocationResult, out string fileReadMessage))
        {
            return fileReadMessage;
        }

        if (TryBuildDirectoryListResultMessage(invocationResult, out string directoryListMessage))
        {
            return directoryListMessage;
        }

        if (TryBuildTextSearchResultMessage(invocationResult, out string textSearchMessage))
        {
            return textSearchMessage;
        }

        if (TryBuildSearchFilesResultMessage(invocationResult, out string searchFilesMessage))
        {
            return searchFilesMessage;
        }

        if (TryBuildWebSearchResultMessage(invocationResult, out string webSearchMessage))
        {
            return webSearchMessage;
        }

        ToolRenderPayload? renderPayload = invocationResult.Result.RenderPayload;
        if (renderPayload is not null)
        {
            string prefix = invocationResult.Result.IsSuccess
                ? string.Empty
                : "Tool issue: ";

            return $"{prefix}{renderPayload.Title}{Environment.NewLine}{Environment.NewLine}{renderPayload.Text}";
        }

        string title = invocationResult.Result.IsSuccess
            ? $"Tool complete: {invocationResult.ToolName}"
            : $"Tool issue: {invocationResult.ToolName}";

        return $"{title}{Environment.NewLine}{Environment.NewLine}{invocationResult.Result.Message}";
    }

    private static bool TryBuildShellCommandResultMessage(
        ToolInvocationResult invocationResult,
        out string message)
    {
        message = string.Empty;

        if (!string.Equals(invocationResult.ToolName, "shell_command", StringComparison.Ordinal) ||
            !TryParseShellCommandResult(invocationResult.Result.JsonResult, out ShellCommandDisplayResult result))
        {
            return false;
        }

        StringBuilder builder = new();
        builder.Append(Bullet).Append(" Ran ").Append(SuspiciousUnicodeText.RenderVisible(result.Command));

        if (result.ExitCode != 0)
        {
            builder.Append(" (exit ").Append(result.ExitCode).Append(')');
        }

        string output = !string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardError
            : result.StandardOutput;
        string outputLabel = !string.IsNullOrWhiteSpace(result.StandardError)
            ? "stderr"
            : "stdout";

        if (string.IsNullOrWhiteSpace(output))
        {
            builder.AppendLine();
            builder.Append("  - exit code: ").Append(result.ExitCode);
            message = builder.ToString();
            return true;
        }

        string[] lines = NormalizePreviewLines(output);
        int displayedLineCount = GetDisplayedCount(lines.Length, MaxShellPreviewLines);

        builder.AppendLine();
        builder.Append("  - ").Append(outputLabel).Append(':');

        for (int index = 0; index < displayedLineCount; index++)
        {
            builder.AppendLine();
            builder.Append("    ").Append(Truncate(lines[index], 180));
        }

        if (lines.Length > displayedLineCount)
        {
            builder.AppendLine();
            builder.Append("    ... +").Append(lines.Length - displayedLineCount).Append(" lines");
        }

        message = builder.ToString();
        return true;
    }

    private static bool TryBuildApplyPatchResultMessage(
        ToolInvocationResult invocationResult,
        out string message)
    {
        message = string.Empty;

        if (!string.Equals(invocationResult.ToolName, "apply_patch", StringComparison.Ordinal) ||
            !TryParseApplyPatchResult(invocationResult.Result.JsonResult, out IReadOnlyList<FileEditDisplayResult> files))
        {
            return false;
        }

        message = BuildFileEditMessage(files);
        return true;
    }

    private static bool TryBuildFileReadResultMessage(
        ToolInvocationResult invocationResult,
        out string message)
    {
        message = string.Empty;

        if (!string.Equals(invocationResult.ToolName, "file_read", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(invocationResult.Result.JsonResult);
            JsonElement root = document.RootElement;
            if (!TryGetJsonString(root, "Path", out string path))
            {
                return false;
            }

            TryGetJsonString(root, "Content", out string content, trim: false);
            TryGetJsonInt32(root, "CharacterCount", out int characterCount);

            string[] lines = SplitPreviewLines(content);
            bool showFullContent = ToolOutputDisplay.ShowFullToolOutput;
            int displayedLineCount = GetDisplayedCount(lines.Length, MaxToolPreviewLines);
            StringBuilder builder = new();
            builder
                .Append(Bullet)
                .Append(" Read ")
                .Append(path)
                .Append(" (")
                .Append(characterCount)
                .Append(" chars)");

            if (lines.Length == 0)
            {
                builder.AppendLine().Append("  - empty file");
                message = builder.ToString();
                return true;
            }

            builder.AppendLine().Append(showFullContent ? "  - content:" : "  - preview:");
            for (int index = 0; index < displayedLineCount; index++)
            {
                builder
                    .AppendLine()
                    .Append("      ")
                    .Append((index + 1).ToString(CultureInfo.InvariantCulture).PadLeft(4))
                    .Append(' ')
                    .Append(Truncate(lines[index], 180));
            }

            if (lines.Length > displayedLineCount)
            {
                builder
                    .AppendLine()
                    .Append("    ... +")
                    .Append(lines.Length - displayedLineCount)
                    .Append(" lines");
            }

            message = builder.ToString();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryBuildDirectoryListResultMessage(
        ToolInvocationResult invocationResult,
        out string message)
    {
        message = string.Empty;

        if (!string.Equals(invocationResult.ToolName, "directory_list", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(invocationResult.Result.JsonResult);
            JsonElement root = document.RootElement;
            if (!TryGetJsonString(root, "Path", out string path) ||
                !TryGetJsonProperty(root, "Entries", out JsonElement entriesElement) ||
                entriesElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            List<DirectoryEntryDisplayResult> entries = [];
            foreach (JsonElement entryElement in entriesElement.EnumerateArray())
            {
                TryGetJsonString(entryElement, "Path", out string entryPath);
                TryGetJsonString(entryElement, "EntryType", out string entryType);

                if (!string.IsNullOrWhiteSpace(entryPath))
                {
                    entries.Add(new DirectoryEntryDisplayResult(
                        entryPath,
                        string.IsNullOrWhiteSpace(entryType) ? "entry" : entryType));
                }
            }

            StringBuilder builder = new();
            builder
                .Append(Bullet)
                .Append(" Listed ")
                .Append(path)
                .Append(" (")
                .Append(entries.Count)
                .Append(entries.Count == 1 ? " entry)" : " entries)");

            if (entries.Count == 0)
            {
                builder.AppendLine().Append("  - empty");
                message = builder.ToString();
                return true;
            }

            int displayedEntryCount = GetDisplayedCount(entries.Count, MaxToolPreviewLines);
            for (int index = 0; index < displayedEntryCount; index++)
            {
                DirectoryEntryDisplayResult entry = entries[index];
                builder
                    .AppendLine()
                    .Append("  - ")
                    .Append(entry.EntryType)
                    .Append(": ")
                    .Append(entry.Path);
            }

            if (entries.Count > displayedEntryCount)
            {
                builder
                    .AppendLine()
                    .Append("    ... +")
                    .Append(entries.Count - displayedEntryCount)
                    .Append(" entries");
            }

            message = builder.ToString();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryBuildTextSearchResultMessage(
        ToolInvocationResult invocationResult,
        out string message)
    {
        message = string.Empty;

        if (!string.Equals(invocationResult.ToolName, "text_search", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(invocationResult.Result.JsonResult);
            JsonElement root = document.RootElement;
            if (!TryGetJsonString(root, "Query", out string query) ||
                !TryGetJsonProperty(root, "Matches", out JsonElement matchesElement) ||
                matchesElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            TryGetJsonString(root, "Path", out string path);
            List<TextSearchMatchDisplayResult> matches = [];
            foreach (JsonElement matchElement in matchesElement.EnumerateArray())
            {
                TryGetJsonString(matchElement, "Path", out string matchPath);
                TryGetJsonInt32(matchElement, "LineNumber", out int lineNumber);
                TryGetJsonString(matchElement, "LineText", out string lineText, trim: false);

                matches.Add(new TextSearchMatchDisplayResult(
                    matchPath,
                    lineNumber,
                    lineText));
            }

            StringBuilder builder = new();
            builder
                .Append(Bullet)
                .Append(" Searched ")
                .Append(string.IsNullOrWhiteSpace(path) ? "." : path)
                .Append(" for \"")
                .Append(query)
                .Append("\" (")
                .Append(matches.Count)
                .Append(matches.Count == 1 ? " match)" : " matches)");

            if (matches.Count == 0)
            {
                builder.AppendLine().Append("  - no matches");
                message = builder.ToString();
                return true;
            }

            int displayedMatchCount = GetDisplayedCount(matches.Count, MaxToolPreviewLines);
            for (int index = 0; index < displayedMatchCount; index++)
            {
                TextSearchMatchDisplayResult match = matches[index];
                builder
                    .AppendLine()
                    .Append("  - ")
                    .Append(match.Path)
                    .Append(':')
                    .Append(match.LineNumber)
                    .Append(' ')
                    .Append(Truncate(match.LineText, 180));
            }

            if (matches.Count > displayedMatchCount)
            {
                builder
                    .AppendLine()
                    .Append("    ... +")
                    .Append(matches.Count - displayedMatchCount)
                    .Append(" matches");
            }

            message = builder.ToString();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryBuildSearchFilesResultMessage(
        ToolInvocationResult invocationResult,
        out string message)
    {
        message = string.Empty;

        if (!string.Equals(invocationResult.ToolName, "search_files", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(invocationResult.Result.JsonResult);
            JsonElement root = document.RootElement;
            if (!TryGetJsonString(root, "Query", out string query) ||
                !TryGetJsonProperty(root, "Matches", out JsonElement matchesElement) ||
                matchesElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            TryGetJsonString(root, "Path", out string path);
            TryGetJsonString(root, "Glob", out string glob);
            TryGetJsonBoolean(root, "Fuzzy", out bool fuzzy);
            TryGetJsonString(root, "Mode", out string mode);
            TryGetJsonBoolean(root, "WholeWord", out bool wholeWord);
            TryGetJsonBoolean(root, "CaseSensitive", out bool caseSensitive);
            TryGetJsonBoolean(root, "IncludeHidden", out bool includeHidden);
            TryGetJsonBoolean(root, "IncludeGenerated", out bool includeGenerated);
            TryGetJsonBoolean(root, "IncludeIgnored", out bool includeIgnored);
            TryGetJsonBoolean(root, "HasMore", out bool hasMore);
            TryGetJsonInt32(root, "Limit", out int limit);
            TryGetJsonInt32(root, "Offset", out int offset);
            TryGetJsonInt32(root, "TotalMatchCount", out int totalMatchCount);
            TryGetJsonString(root, "NextCursor", out string nextCursor);
            string effectiveMode = !string.IsNullOrWhiteSpace(mode)
                ? mode
                : fuzzy
                    ? "fuzzy"
                    : "substring";
            string searchLabel = string.IsNullOrWhiteSpace(query)
                ? $"{effectiveMode} search"
                : $"\"{query}\"";
            List<SearchFileMatchDisplayResult> matches = [];
            foreach (JsonElement matchElement in matchesElement.EnumerateArray())
            {
                if (matchElement.ValueKind == JsonValueKind.String)
                {
                    string? match = matchElement.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(match))
                    {
                        matches.Add(new SearchFileMatchDisplayResult(match, null, string.Empty));
                    }
                }
                else if (matchElement.ValueKind == JsonValueKind.Object &&
                         TryGetJsonString(matchElement, "Path", out string matchPath))
                {
                    int? score = TryGetJsonInt32(matchElement, "Score", out int parsedScore)
                        ? parsedScore
                        : null;
                    TryGetJsonString(matchElement, "MatchKind", out string matchKind);
                    matches.Add(new SearchFileMatchDisplayResult(matchPath, score, matchKind));
                    }
                }

            StringBuilder builder = new();
            builder
                .Append(Bullet)
                .Append(" Found ")
                .Append(matches.Count)
                .Append(matches.Count == 1 ? " file" : " files")
                .Append(" for ")
                .Append(searchLabel)
                .Append(" in ")
                .Append(string.IsNullOrWhiteSpace(path) ? "." : path);

            builder
                .Append(" (limit ")
                .Append(limit > 0 ? limit : 200)
                .Append(", offset ")
                .Append(Math.Max(0, offset))
                .Append(", mode ")
                .Append(effectiveMode)
                .Append(", caseSensitive ")
                .Append(caseSensitive ? "true" : "false")
                .Append(", wholeWord ")
                .Append(wholeWord ? "true" : "false");

            if (!string.IsNullOrWhiteSpace(glob))
            {
                builder
                    .Append(", glob ")
                    .Append(glob);
            }

            if (includeHidden)
            {
                builder.Append(", includeHidden true");
            }

            if (includeGenerated)
            {
                builder.Append(", includeGenerated true");
            }

            if (includeIgnored)
            {
                builder.Append(", includeIgnored true");
            }

            builder
                .Append(", hasMore ")
                .Append(hasMore ? "true" : "false");

            builder.Append(')');

            if (matches.Count == 0)
            {
                builder.AppendLine().Append("  - no matching files");
                message = builder.ToString();
                return true;
            }

            int displayedMatchCount = GetDisplayedCount(matches.Count, MaxToolPreviewLines);
            for (int index = 0; index < displayedMatchCount; index++)
            {
                SearchFileMatchDisplayResult match = matches[index];
                builder
                    .AppendLine()
                    .Append("  - ")
                    .Append(match.Path);

                if (match.Score.HasValue || !string.IsNullOrWhiteSpace(match.MatchKind))
                {
                    builder.Append(" (");
                    if (match.Score.HasValue)
                    {
                        builder.Append("score ").Append(match.Score.Value);
                    }

                    if (!string.IsNullOrWhiteSpace(match.MatchKind))
                    {
                        if (match.Score.HasValue)
                        {
                            builder.Append(", ");
                        }

                        builder.Append(match.MatchKind);
                    }

                    builder.Append(')');
                }
            }

            if (matches.Count > displayedMatchCount)
            {
                builder
                    .AppendLine()
                    .Append("    ... +")
                    .Append(matches.Count - displayedMatchCount)
                    .Append(" files");
            }

            if (hasMore && !string.IsNullOrWhiteSpace(nextCursor))
            {
                builder
                    .AppendLine()
                    .Append("  - nextCursor: ")
                    .Append(nextCursor);
            }

            if (totalMatchCount > 0)
            {
                builder
                    .AppendLine()
                    .Append("  - total matches: ")
                    .Append(totalMatchCount);
            }

            message = builder.ToString();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryBuildWebSearchResultMessage(
        ToolInvocationResult invocationResult,
        out string message)
    {
        message = string.Empty;

        if (!string.Equals(invocationResult.ToolName, "web_search", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(invocationResult.Result.JsonResult);
            JsonElement root = document.RootElement;
            List<string> lines = [];

            AddWebSearchLines(root, "SearchQuery", "search", lines);
            AddWebSearchWarningLines(root, lines);

            int searchCount = GetJsonArrayCount(root, "SearchQuery");

            StringBuilder builder = new();
            builder
                .Append(Bullet)
                .Append(" web_search completed (")
                .Append(searchCount)
                .Append(searchCount == 1 ? " search)" : " searches)");

            if (lines.Count == 0)
            {
                builder.AppendLine().Append("  - no preview output");
                message = builder.ToString();
                return true;
            }

            int displayedLineCount = GetDisplayedCount(lines.Count, MaxWebPreviewLines);
            for (int index = 0; index < displayedLineCount; index++)
            {
                builder.AppendLine().Append(lines[index]);
            }

            if (lines.Count > displayedLineCount)
            {
                builder
                    .AppendLine()
                    .Append("    ... +")
                    .Append(lines.Count - displayedLineCount)
                    .Append(" lines");
            }

            message = builder.ToString();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void AddWebSearchLines(
        JsonElement root,
        string sectionName,
        string label,
        List<string> lines)
    {
        if (!TryGetJsonProperty(root, sectionName, out JsonElement section) ||
            section.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement item in section.EnumerateArray())
        {
            TryGetJsonString(item, "Query", out string query);
            int resultCount = GetJsonArrayCount(item, "Results");
            lines.Add($"  - {label} \"{query}\": {resultCount} {(resultCount == 1 ? "result" : "results")}");

            if (TryGetJsonProperty(item, "Results", out JsonElement results) &&
                results.ValueKind == JsonValueKind.Array &&
                results.GetArrayLength() > 0)
            {
                JsonElement first = results.EnumerateArray().First();
                string title = GetFirstJsonString(first, "Title", "Url");
                string url = GetFirstJsonString(first, "Url");
                string summary = string.Join(
                    " - ",
                    new[] { title, url }.Where(static value => !string.IsNullOrWhiteSpace(value)));

                if (!string.IsNullOrWhiteSpace(summary))
                {
                    lines.Add($"    {Truncate(summary, 180)}");
                }
            }

            AddWarningLine(item, lines);
        }
    }

    private static void AddWebSearchWarningLines(JsonElement root, List<string> lines)
    {
        if (!TryGetJsonProperty(root, "Warnings", out JsonElement warnings) ||
            warnings.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement warningElement in warnings.EnumerateArray())
        {
            if (warningElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            string? warning = warningElement.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(warning))
            {
                lines.Add($"  - warning: {Truncate(warning, 180)}");
            }
        }
    }

    private static void AddWarningLine(JsonElement item, List<string> lines)
    {
        if (TryGetJsonString(item, "Warning", out string warning) &&
            !string.IsNullOrWhiteSpace(warning))
        {
            lines.Add($"    warning: {Truncate(warning, 180)}");
        }
    }

    private static void FlushFileEditBatch(
        List<string> messages,
        List<FileEditDisplayResult> fileEditBatch)
    {
        if (fileEditBatch.Count == 0)
        {
            return;
        }

        messages.Add(BuildFileEditMessage(fileEditBatch));
        fileEditBatch.Clear();
    }

    private static string BuildFileEditMessage(IReadOnlyList<FileEditDisplayResult> edits)
    {
        if (edits.Count == 0)
        {
            return $"{Bullet} Edited 0 files (+0 -0)";
        }

        int totalAddedLineCount = edits.Sum(static edit => edit.AddedLineCount);
        int totalRemovedLineCount = edits.Sum(static edit => edit.RemovedLineCount);
        StringBuilder builder = new();

        builder
            .Append(Bullet)
            .Append(" Edited ")
            .Append(edits.Count)
            .Append(edits.Count == 1 ? " file" : " files")
            .Append(" (+")
            .Append(totalAddedLineCount)
            .Append(" -")
            .Append(totalRemovedLineCount)
            .Append(')');

        foreach (FileEditDisplayResult edit in edits)
        {
            builder
                .AppendLine()
                .Append("  - ")
                .Append(edit.DisplayPath)
                .Append(" (+")
                .Append(edit.AddedLineCount)
                .Append(" -")
                .Append(edit.RemovedLineCount)
                .Append(')');

            int displayedLineCount = GetDisplayedCount(edit.PreviewLines.Count, MaxToolPreviewLines);
            for (int index = 0; index < displayedLineCount; index++)
            {
                FilePreviewDisplayLine previewLine = edit.PreviewLines[index];
                builder
                    .AppendLine()
                    .Append("      ")
                    .Append(previewLine.LineNumber.ToString(CultureInfo.InvariantCulture).PadLeft(4))
                    .Append(' ')
                    .Append(GetPreviewLineIndicator(previewLine.Kind))
                    .Append(previewLine.Text);
            }

            int remainingLineCount = edit.RemainingPreviewLineCount +
                Math.Max(0, edit.PreviewLines.Count - displayedLineCount);
            if (remainingLineCount > 0)
            {
                builder
                    .AppendLine()
                    .Append("    ... +")
                    .Append(remainingLineCount)
                    .Append(" lines");
            }
        }

        return builder.ToString();
    }

    private static bool CanGroupFileEdit(
        ToolInvocationResult invocationResult,
        out FileEditDisplayResult edit)
    {
        edit = default;

        return invocationResult.Result.IsSuccess &&
            (string.Equals(invocationResult.ToolName, "file_write", StringComparison.Ordinal) ||
             string.Equals(invocationResult.ToolName, "file_delete", StringComparison.Ordinal)) &&
            TryParseFileWriteResult(invocationResult.Result.JsonResult, out edit);
    }

    private static bool IsSuccessfulPlanUpdate(ToolInvocationResult invocationResult)
    {
        return invocationResult.Result.IsSuccess &&
            string.Equals(invocationResult.ToolName, "update_plan", StringComparison.Ordinal);
    }

    private static bool TryParseShellCommandResult(
        string json,
        out ShellCommandDisplayResult result)
    {
        result = default;

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (!TryGetJsonString(root, "Command", out string command))
            {
                return false;
            }

            TryGetJsonString(root, "WorkingDirectory", out string workingDirectory);
            TryGetJsonInt32(root, "ExitCode", out int exitCode);
            TryGetJsonString(root, "StandardOutput", out string standardOutput);
            TryGetJsonString(root, "StandardError", out string standardError);

            result = new ShellCommandDisplayResult(
                command,
                workingDirectory,
                exitCode,
                standardOutput,
                standardError);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseFileWriteResult(
        string json,
        out FileEditDisplayResult result)
    {
        result = default;

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return TryReadFileEdit(document.RootElement, out result);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseApplyPatchResult(
        string json,
        out IReadOnlyList<FileEditDisplayResult> files)
    {
        files = [];

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (!TryGetJsonProperty(document.RootElement, "Files", out JsonElement filesElement) ||
                filesElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            List<FileEditDisplayResult> parsedFiles = [];
            foreach (JsonElement fileElement in filesElement.EnumerateArray())
            {
                if (TryReadFileEdit(fileElement, out FileEditDisplayResult file))
                {
                    parsedFiles.Add(file);
                }
            }

            files = parsedFiles;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadFileEdit(
        JsonElement element,
        out FileEditDisplayResult result)
    {
        result = default;

        if (!TryGetJsonString(element, "Path", out string path))
        {
            return false;
        }

        TryGetJsonString(element, "PreviousPath", out string previousPath);
        TryGetJsonInt32(element, "AddedLineCount", out int addedLineCount);
        TryGetJsonInt32(element, "RemovedLineCount", out int removedLineCount);
        TryGetJsonInt32(element, "RemainingPreviewLineCount", out int remainingPreviewLineCount);

        List<FilePreviewDisplayLine> previewLines = [];
        if (TryGetJsonProperty(element, "PreviewLines", out JsonElement previewLinesElement) &&
            previewLinesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement previewLineElement in previewLinesElement.EnumerateArray())
            {
                TryGetJsonInt32(previewLineElement, "LineNumber", out int lineNumber);
                TryGetJsonString(previewLineElement, "Kind", out string kind);
                TryGetJsonString(previewLineElement, "Text", out string text);

                previewLines.Add(new FilePreviewDisplayLine(
                    lineNumber,
                    kind,
                    text));
            }
        }

        string displayPath = string.IsNullOrWhiteSpace(previousPath)
            ? path
            : $"{previousPath} -> {path}";

        result = new FileEditDisplayResult(
            displayPath,
            addedLineCount,
            removedLineCount,
            previewLines,
            remainingPreviewLineCount);
        return true;
    }

    private static string[] NormalizePreviewLines(string text)
    {
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.None)
            .Select(static line => line.TrimEnd())
            .SkipWhile(static line => string.IsNullOrWhiteSpace(line))
            .Reverse()
            .SkipWhile(static line => string.IsNullOrWhiteSpace(line))
            .Reverse()
            .ToArray();
    }

    private static string[] SplitPreviewLines(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.None)
            .Select(static line => line.TrimEnd())
            .ToArray();
    }

    private static int GetDisplayedCount(int totalCount, int compactMaxCount)
    {
        return ToolOutputDisplay.ShowFullToolOutput
            ? totalCount
            : Math.Min(compactMaxCount, totalCount);
    }

    private static char GetPreviewLineIndicator(string kind)
    {
        return kind.Equals("add", StringComparison.OrdinalIgnoreCase)
            ? '+'
            : kind.Equals("remove", StringComparison.OrdinalIgnoreCase)
                ? '-'
                : ' ';
    }

    private static bool TryGetArgumentString(
        string argumentsJson,
        string propertyName,
        out string value)
    {
        value = string.Empty;

        try
        {
            using JsonDocument document = JsonDocument.Parse(argumentsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(propertyName, out JsonElement property) ||
                property.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            value = property.GetString()?.Trim() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetFirstArrayObjectString(
        string argumentsJson,
        string arrayPropertyName,
        string itemPropertyName,
        out string value)
    {
        value = string.Empty;

        try
        {
            using JsonDocument document = JsonDocument.Parse(argumentsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(arrayPropertyName, out JsonElement array) ||
                array.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (JsonElement item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !item.TryGetProperty(itemPropertyName, out JsonElement property) ||
                    property.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                value = property.GetString()?.Trim() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(value);
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetArgumentArrayCount(
        string argumentsJson,
        string propertyName,
        out int count)
    {
        count = 0;

        try
        {
            using JsonDocument document = JsonDocument.Parse(argumentsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(propertyName, out JsonElement property) ||
                property.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            count = property.GetArrayLength();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetJsonString(
        JsonElement element,
        string propertyName,
        out string value,
        bool trim = true)
    {
        value = string.Empty;

        if (!TryGetJsonProperty(element, propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        if (trim)
        {
            value = value.Trim();
        }

        return true;
    }

    private static bool TryGetJsonInt32(
        JsonElement element,
        string propertyName,
        out int value)
    {
        value = 0;

        if (!TryGetJsonProperty(element, propertyName, out JsonElement property))
        {
            return false;
        }

        return property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out value);
    }

    private static bool TryGetJsonBoolean(
        JsonElement element,
        string propertyName,
        out bool value)
    {
        value = false;

        if (!TryGetJsonProperty(element, propertyName, out JsonElement property))
        {
            return false;
        }

        if (property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = property.GetBoolean();
        return true;
    }

    private static bool TryGetJsonProperty(
        JsonElement element,
        string propertyName,
        out JsonElement property)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            property = default;
            return false;
        }

        if (element.TryGetProperty(propertyName, out property))
        {
            return true;
        }

        foreach (JsonProperty candidate in element.EnumerateObject())
        {
            if (string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                property = candidate.Value;
                return true;
            }
        }

        property = default;
        return false;
    }

    private static int GetJsonArrayCount(
        JsonElement element,
        string propertyName)
    {
        return TryGetJsonProperty(element, propertyName, out JsonElement property) &&
            property.ValueKind == JsonValueKind.Array
                ? property.GetArrayLength()
                : 0;
    }

    private static string GetFirstJsonString(
        JsonElement element,
        params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            if (TryGetJsonString(element, propertyName, out string value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static string Truncate(string value, int maxLength)
    {
        string normalized = SuspiciousUnicodeText.RenderVisible(value).Trim();
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..Math.Max(0, maxLength - 3)] + "...";
    }

    private readonly record struct FileEditDisplayResult(
        string DisplayPath,
        int AddedLineCount,
        int RemovedLineCount,
        IReadOnlyList<FilePreviewDisplayLine> PreviewLines,
        int RemainingPreviewLineCount);

    private readonly record struct FilePreviewDisplayLine(
        int LineNumber,
        string Kind,
        string Text);

    private readonly record struct DirectoryEntryDisplayResult(
        string Path,
        string EntryType);

    private readonly record struct TextSearchMatchDisplayResult(
        string Path,
        int LineNumber,
        string LineText);

    private readonly record struct SearchFileMatchDisplayResult(
        string Path,
        int? Score,
        string MatchKind);

    private readonly record struct ShellCommandDisplayResult(
        string Command,
        string WorkingDirectory,
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
