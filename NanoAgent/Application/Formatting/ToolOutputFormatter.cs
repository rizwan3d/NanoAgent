using System.Globalization;
using System.Text;
using System.Text.Json;
using NanoAgent.Application.Models;
using NanoAgent.Application.Utilities;

namespace NanoAgent.Application.Formatting;

public interface IToolOutputFormatter
{
    string DescribeCall(ConversationToolCall toolCall);

    IReadOnlyList<string> FormatResults(ToolExecutionBatchResult toolExecutionResult);
}

public sealed class ToolOutputFormatter : IToolOutputFormatter
{
    private const int MaxShellPreviewLines = 5;
    private const int MaxToolPreviewLines = 8;
    private const int MaxWebPreviewLines = 12;
    private const string Bullet = "\u2022";

    public string DescribeCall(ConversationToolCall toolCall)
    {
        string name = toolCall.Name.Trim();

        string description = name switch
        {
            "shell_command" when TryGetArgumentString(toolCall.ArgumentsJson, "command", out string command) =>
                $"command: {Truncate(command, 120)}",
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
            "web_run" => DescribeWebRunCall(toolCall.ArgumentsJson),
            _ => name
        };

        return SecretRedactor.Redact(description);
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

    private static string DescribeWebRunCall(string argumentsJson)
    {
        if (TryGetFirstArrayObjectString(argumentsJson, "search_query", "q", out string query))
        {
            return $"web search: \"{query}\"";
        }

        if (TryGetFirstArrayObjectString(argumentsJson, "image_query", "q", out string imageQuery))
        {
            return $"image search: \"{imageQuery}\"";
        }

        if (TryGetFirstArrayObjectString(argumentsJson, "open", "ref_id", out string refId))
        {
            return $"web open: {refId}";
        }

        if (TryGetFirstArrayObjectString(argumentsJson, "find", "pattern", out string pattern))
        {
            return $"web find: \"{pattern}\"";
        }

        return "web_run";
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

        if (TryBuildWebRunResultMessage(invocationResult, out string webRunMessage))
        {
            return webRunMessage;
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
        builder.Append(Bullet).Append(" Ran ").Append(result.Command);

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
        int displayedLineCount = Math.Min(MaxShellPreviewLines, lines.Length);

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
            int displayedLineCount = Math.Min(MaxToolPreviewLines, lines.Length);
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

            builder.AppendLine().Append("  - preview:");
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

            int displayedEntryCount = Math.Min(MaxToolPreviewLines, entries.Count);
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

            int displayedMatchCount = Math.Min(MaxToolPreviewLines, matches.Count);
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
            List<string> matches = [];
            foreach (JsonElement matchElement in matchesElement.EnumerateArray())
            {
                if (matchElement.ValueKind == JsonValueKind.String)
                {
                    string? match = matchElement.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(match))
                    {
                        matches.Add(match);
                    }
                }
            }

            StringBuilder builder = new();
            builder
                .Append(Bullet)
                .Append(" Found ")
                .Append(matches.Count)
                .Append(matches.Count == 1 ? " file" : " files")
                .Append(" for \"")
                .Append(query)
                .Append("\" in ")
                .Append(string.IsNullOrWhiteSpace(path) ? "." : path);

            if (matches.Count == 0)
            {
                builder.AppendLine().Append("  - no matching files");
                message = builder.ToString();
                return true;
            }

            int displayedMatchCount = Math.Min(MaxToolPreviewLines, matches.Count);
            for (int index = 0; index < displayedMatchCount; index++)
            {
                builder
                    .AppendLine()
                    .Append("  - ")
                    .Append(matches[index]);
            }

            if (matches.Count > displayedMatchCount)
            {
                builder
                    .AppendLine()
                    .Append("    ... +")
                    .Append(matches.Count - displayedMatchCount)
                    .Append(" files");
            }

            message = builder.ToString();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryBuildWebRunResultMessage(
        ToolInvocationResult invocationResult,
        out string message)
    {
        message = string.Empty;

        if (!string.Equals(invocationResult.ToolName, "web_run", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(invocationResult.Result.JsonResult);
            JsonElement root = document.RootElement;
            List<string> lines = [];

            AddWebRunSearchLines(root, "SearchQuery", "search", lines);
            AddWebRunSearchLines(root, "ImageQuery", "image search", lines);
            AddWebRunOpenLines(root, lines);
            AddWebRunFindLines(root, lines);
            AddWebRunSimpleArrayLines(root, "Screenshot", "screenshot", lines);
            AddWebRunSimpleArrayLines(root, "Finance", "finance", lines);
            AddWebRunSimpleArrayLines(root, "Weather", "weather", lines);
            AddWebRunSimpleArrayLines(root, "Sports", "sports", lines);
            AddWebRunSimpleArrayLines(root, "Time", "time", lines);
            AddWebRunWarningLines(root, lines);

            int operationCount =
                GetJsonArrayCount(root, "SearchQuery") +
                GetJsonArrayCount(root, "ImageQuery") +
                GetJsonArrayCount(root, "Open") +
                GetJsonArrayCount(root, "Find") +
                GetJsonArrayCount(root, "Screenshot") +
                GetJsonArrayCount(root, "Finance") +
                GetJsonArrayCount(root, "Weather") +
                GetJsonArrayCount(root, "Sports") +
                GetJsonArrayCount(root, "Time");

            StringBuilder builder = new();
            builder
                .Append(Bullet)
                .Append(" web_run completed (")
                .Append(operationCount)
                .Append(operationCount == 1 ? " operation)" : " operations)");

            if (lines.Count == 0)
            {
                builder.AppendLine().Append("  - no preview output");
                message = builder.ToString();
                return true;
            }

            int displayedLineCount = Math.Min(MaxWebPreviewLines, lines.Count);
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

    private static void AddWebRunSearchLines(
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
                string title = GetFirstJsonString(first, "Title", "Url", "ImageUrl");
                string refId = GetFirstJsonString(first, "RefId");
                string url = GetFirstJsonString(first, "DisplayUrl", "Url", "ImageUrl", "SourcePageUrl");
                string prefix = string.IsNullOrWhiteSpace(refId) ? string.Empty : $"{refId}: ";
                string summary = string.Join(
                    " - ",
                    new[] { title, url }.Where(static value => !string.IsNullOrWhiteSpace(value)));

                if (!string.IsNullOrWhiteSpace(summary))
                {
                    lines.Add($"    {prefix}{Truncate(summary, 180)}");
                }
            }

            AddWarningLine(item, lines);
        }
    }

    private static void AddWebRunOpenLines(JsonElement root, List<string> lines)
    {
        if (!TryGetJsonProperty(root, "Open", out JsonElement section) ||
            section.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement item in section.EnumerateArray())
        {
            string refId = GetFirstJsonString(item, "RequestedRefId");
            string title = GetFirstJsonString(item, "Title", "ResolvedUrl");
            TryGetJsonInt32(item, "StartLine", out int startLine);
            TryGetJsonInt32(item, "EndLine", out int endLine);
            lines.Add($"  - open {refId}: {Truncate(title, 160)} (lines {startLine}-{endLine})");

            if (TryGetJsonString(item, "Text", out string text, trim: false))
            {
                string[] preview = NormalizePreviewLines(text);
                for (int index = 0; index < Math.Min(2, preview.Length); index++)
                {
                    lines.Add($"    {Truncate(preview[index], 180)}");
                }
            }

            AddWarningLine(item, lines);
        }
    }

    private static void AddWebRunFindLines(JsonElement root, List<string> lines)
    {
        if (!TryGetJsonProperty(root, "Find", out JsonElement section) ||
            section.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement item in section.EnumerateArray())
        {
            string refId = GetFirstJsonString(item, "RequestedRefId");
            string pattern = GetFirstJsonString(item, "Pattern");
            int matchCount = GetJsonArrayCount(item, "Matches");
            lines.Add($"  - find \"{pattern}\" in {refId}: {matchCount} {(matchCount == 1 ? "match" : "matches")}");

            if (TryGetJsonProperty(item, "Matches", out JsonElement matches) &&
                matches.ValueKind == JsonValueKind.Array &&
                matches.GetArrayLength() > 0)
            {
                JsonElement first = matches.EnumerateArray().First();
                TryGetJsonInt32(first, "LineNumber", out int lineNumber);
                string lineText = GetFirstJsonString(first, "LineText");
                lines.Add($"    {lineNumber}: {Truncate(lineText, 180)}");
            }

            AddWarningLine(item, lines);
        }
    }

    private static void AddWebRunSimpleArrayLines(
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
            string name = GetFirstJsonString(
                item,
                "Ticker",
                "Location",
                "UtcOffset",
                "League",
                "RequestedRefId",
                "ResolvedUrl",
                "Function");
            string detail = BuildSimpleWebRunDetail(item);

            lines.Add(string.IsNullOrWhiteSpace(detail)
                ? $"  - {label} {name}".TrimEnd()
                : $"  - {label} {name}: {detail}".TrimEnd());

            AddWarningLine(item, lines);
        }
    }

    private static string BuildSimpleWebRunDetail(JsonElement item)
    {
        string[] candidates =
        [
            GetJsonScalarString(item, "Price"),
            GetFirstJsonString(item, "Currency"),
            GetFirstJsonString(item, "MarketState"),
            GetFirstJsonString(item, "Condition"),
            GetFirstJsonString(item, "TemperatureC"),
            GetFirstJsonString(item, "DisplayTime"),
            GetFirstJsonString(item, "ContentType")
        ];

        string summary = string.Join(
            " ",
            candidates.Where(static value => !string.IsNullOrWhiteSpace(value)));

        if (!string.IsNullOrWhiteSpace(summary))
        {
            return Truncate(summary, 120);
        }

        int entryCount = GetJsonArrayCount(item, "Entries");
        if (entryCount > 0)
        {
            return $"{entryCount} {(entryCount == 1 ? "entry" : "entries")}";
        }

        string byteCount = GetJsonScalarString(item, "ByteCount");
        return string.IsNullOrWhiteSpace(byteCount) ? string.Empty : $"{byteCount} bytes";
    }

    private static void AddWebRunWarningLines(JsonElement root, List<string> lines)
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

            int displayedLineCount = Math.Min(MaxToolPreviewLines, edit.PreviewLines.Count);
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

    private static string GetJsonScalarString(
        JsonElement element,
        string propertyName)
    {
        if (!TryGetJsonProperty(element, propertyName, out JsonElement property))
        {
            return string.Empty;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString()?.Trim() ?? string.Empty,
            JsonValueKind.Number => property.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }

    private static string Truncate(string value, int maxLength)
    {
        string normalized = value.Trim();
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

    private readonly record struct ShellCommandDisplayResult(
        string Command,
        string WorkingDirectory,
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
