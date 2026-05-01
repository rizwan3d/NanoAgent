using NanoAgent.Application.Models;
using System.Text;

namespace NanoAgent.CLI;

public static partial class Program
{
    private const int MaxAttachmentBytes = 20 * 1024 * 1024;

    private static bool TryAttachFilesFromDroppedOrPastedText(
        AppState state,
        string text)
    {
        string[] paths = ParsePastedFilePaths(text);
        if (paths.Length == 0)
        {
            return false;
        }

        List<string> resolvedPaths = [];
        foreach (string path in paths)
        {
            if (!TryResolveInputFilePath(state, path, out string? fullPath) ||
                !File.Exists(fullPath))
            {
                return false;
            }

            resolvedPaths.Add(fullPath);
        }

        AttachFiles(state, resolvedPaths, "pasted");
        return true;
    }

    private static void AttachFiles(
        AppState state,
        IEnumerable<string> paths,
        string source)
    {
        int attachedCount = 0;
        List<string> skipped = [];

        foreach (string path in paths)
        {
            if (!TryCreateAttachmentFromFile(path, out ConversationAttachment? attachment, out string? error) ||
                attachment is null)
            {
                if (!string.IsNullOrWhiteSpace(error))
                {
                    skipped.Add(error);
                }

                continue;
            }

            AddAttachment(state, attachment);
            attachedCount++;
        }

        if (attachedCount > 0)
        {
            ResetSlashCommandSuggestions(state);
        }

        foreach (string message in skipped.Take(3))
        {
            state.AddSystemMessage(message);
        }

        if (skipped.Count > 3)
        {
            state.AddSystemMessage(
                $"Skipped {skipped.Count - 3} more {source} attachment(s).");
        }
    }

    private static void AddAttachment(
        AppState state,
        ConversationAttachment attachment)
    {
        bool alreadyAttached = state.InputAttachments.Any(existing =>
            string.Equals(existing.Name, attachment.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.ContentBase64, attachment.ContentBase64, StringComparison.Ordinal));

        if (!alreadyAttached)
        {
            state.InputAttachments.Add(attachment);
        }
    }

    private static bool TryCreateAttachmentFromFile(
        string path,
        out ConversationAttachment? attachment,
        out string? error)
    {
        attachment = null;
        error = null;

        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            error = $"Attachment not found: {path}";
            return false;
        }

        FileInfo file = new(fullPath);
        if (file.Length == 0)
        {
            error = $"Skipped attachment '{file.Name}' because the file has no content.";
            return false;
        }

        if (file.Length > MaxAttachmentBytes)
        {
            error = $"Skipped attachment '{file.Name}' because it is larger than {FormatBytes(MaxAttachmentBytes)}.";
            return false;
        }

        if (IsImageFile(fullPath))
        {
            byte[] bytes = File.ReadAllBytes(fullPath);
            attachment = new ConversationAttachment(
                file.Name,
                GetMediaType(fullPath),
                Convert.ToBase64String(bytes));
            return true;
        }

        byte[] contentBytes = File.ReadAllBytes(fullPath);
        if (TryDecodeTextAttachment(fullPath, contentBytes, out string? text))
        {
            attachment = new ConversationAttachment(
                file.Name,
                GetMediaType(fullPath),
                Convert.ToBase64String(contentBytes),
                text);
            return true;
        }

        attachment = new ConversationAttachment(
            file.Name,
            GetMediaType(fullPath),
            Convert.ToBase64String(contentBytes));
        return true;
    }

    private static string[] ParsePastedFilePaths(string text)
    {
        string normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return [];
        }

        List<string> tokens = [];
        StringBuilder token = new();
        char? quote = null;

        foreach (char character in normalized)
        {
            if (quote is char activeQuote)
            {
                if (character == activeQuote)
                {
                    quote = null;
                }
                else
                {
                    token.Append(character);
                }

                continue;
            }

            if (character is '"' or '\'')
            {
                quote = character;
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                AddToken(tokens, token);
                continue;
            }

            token.Append(character);
        }

        AddToken(tokens, token);
        return tokens.ToArray();
    }

    private static void AddToken(
        List<string> tokens,
        StringBuilder token)
    {
        if (token.Length == 0)
        {
            return;
        }

        tokens.Add(token.ToString().Trim());
        token.Clear();
    }

    private static bool TryResolveInputFilePath(
        AppState state,
        string path,
        out string? fullPath)
    {
        fullPath = null;
        string normalized = path.Trim();
        if (Uri.TryCreate(normalized, UriKind.Absolute, out Uri? uri) && uri.IsFile)
        {
            normalized = uri.LocalPath;
        }

        fullPath = Path.GetFullPath(
            Path.IsPathRooted(normalized)
                ? normalized
                : Path.Combine(state.RootDirectory, normalized));
        return true;
    }

    private static bool TryDecodeTextAttachment(
        string path,
        byte[] bytes,
        out string? text)
    {
        text = null;
        if (bytes.Length == 0)
        {
            text = string.Empty;
            return true;
        }

        if (!IsTextExtension(path) && IsLikelyBinaryContent(bytes))
        {
            return false;
        }

        try
        {
            text = new UTF8Encoding(false, true).GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            if (!IsTextExtension(path))
            {
                return false;
            }

            text = Encoding.UTF8.GetString(bytes);
            return true;
        }
    }

    private static bool IsLikelyBinaryContent(byte[] bytes)
    {
        int sampleLength = Math.Min(bytes.Length, 4096);
        int zeroCount = 0;

        for (int index = 0; index < sampleLength; index++)
        {
            if (bytes[index] == 0)
            {
                zeroCount++;
            }
        }

        return zeroCount > 0;
    }

    private static bool IsLikelyBinaryFile(string path)
    {
        return !IsImageFile(path) && !IsTextExtension(path);
    }

    private static bool IsImageFile(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" => true,
            _ => false
        };
    }

    private static bool IsTextExtension(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".txt" or ".md" or ".markdown" or ".json" or ".jsonl" or ".xml" or ".yaml" or ".yml" or
            ".csv" or ".tsv" or ".log" or ".ini" or ".toml" or ".props" or ".targets" or ".csproj" or
            ".sln" or ".slnx" or ".cs" or ".fs" or ".vb" or ".js" or ".jsx" or ".ts" or ".tsx" or
            ".css" or ".scss" or ".html" or ".htm" or ".svg" or ".py" or ".go" or ".rs" or ".java" or ".c" or
            ".h" or ".cpp" or ".hpp" or ".sql" or ".sh" or ".ps1" or ".bat" or ".cmd" => true,
            _ => false
        };
    }

    private static string GetMediaType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            ".txt" or ".log" => "text/plain",
            ".md" or ".markdown" => "text/markdown",
            ".json" or ".jsonl" => "application/json",
            ".xml" or ".csproj" or ".props" or ".targets" => "application/xml",
            ".yaml" or ".yml" => "application/yaml",
            ".csv" => "text/csv",
            ".html" or ".htm" => "text/html",
            ".css" or ".scss" => "text/css",
            ".js" or ".jsx" or ".ts" or ".tsx" => "text/javascript",
            _ when IsTextExtension(path) => "text/plain",
            _ => "application/octet-stream"
        };
    }

    private static string FormatBytes(int bytes)
    {
        return bytes >= 1024 * 1024
            ? $"{bytes / (1024 * 1024)} MB"
            : $"{bytes / 1024} KB";
    }

}
