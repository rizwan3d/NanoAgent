namespace NanoAgent.CLI;

internal static class FilePathSuggestionProvider
{
    private const string ReadCommandPrefix = "/read ";
    private const string ImportCommandPrefix = "/import ";
    private const string ExportJsonCommandPrefix = "/export json ";
    private const string ExportHtmlCommandPrefix = "/export html ";
    private static readonly char[] DirectorySeparators = ['/', '\\'];

    public static IReadOnlyList<FilePathSuggestion> GetSuggestions(
        string rootDirectory,
        string input,
        int maxCount)
    {
        if (maxCount <= 0 ||
            !TryCreateRequest(rootDirectory, input, out FilePathSuggestionRequest? request) ||
            request is null)
        {
            return [];
        }

        string directoryPart = GetDirectoryPart(request.PathText);
        string namePrefix = GetNamePrefix(request.PathText);
        if (!TryResolveDirectory(request.RootDirectory, directoryPart, out string? searchDirectory) ||
            !Directory.Exists(searchDirectory))
        {
            return [];
        }

        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        List<FilePathSuggestion> suggestions = [];
        foreach (DirectoryInfo directory in EnumerateDirectories(searchDirectory)
            .Where(directory => directory.Name.StartsWith(namePrefix, comparison))
            .OrderBy(directory => directory.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (ShouldSkipPath(request.RootDirectory, directory.FullName))
            {
                continue;
            }

            string displayPath = ToDisplayPath(request.RootDirectory, directory.FullName) + "/";
            suggestions.Add(new FilePathSuggestion(
                request.CommandPrefix + displayPath,
                displayPath,
                "Directory",
                IsDirectory: true));

            if (suggestions.Count >= maxCount)
            {
                return suggestions;
            }
        }

        foreach (FileInfo file in EnumerateFiles(searchDirectory)
            .Where(file => file.Name.StartsWith(namePrefix, comparison))
            .Where(file => !request.JsonOnly || string.Equals(file.Extension, ".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (ShouldSkipPath(request.RootDirectory, file.FullName))
            {
                continue;
            }

            string displayPath = ToDisplayPath(request.RootDirectory, file.FullName);
            suggestions.Add(new FilePathSuggestion(
                request.CommandPrefix + displayPath,
                displayPath,
                request.JsonOnly ? "JSON file" : "File",
                IsDirectory: false));

            if (suggestions.Count >= maxCount)
            {
                return suggestions;
            }
        }

        return suggestions;
    }

    private static bool TryCreateRequest(
        string rootDirectory,
        string input,
        out FilePathSuggestionRequest? request)
    {
        request = null;
        if (string.IsNullOrWhiteSpace(rootDirectory) ||
            string.IsNullOrWhiteSpace(input) ||
            input.Contains('\n', StringComparison.Ordinal))
        {
            return false;
        }

        string fullRoot = Path.GetFullPath(rootDirectory);
        if (TryCreateRequest(input, ReadCommandPrefix, fullRoot, jsonOnly: false, out request) ||
            TryCreateRequest(input, ImportCommandPrefix, fullRoot, jsonOnly: true, out request) ||
            TryCreateRequest(input, ExportJsonCommandPrefix, fullRoot, jsonOnly: false, out request) ||
            TryCreateRequest(input, ExportHtmlCommandPrefix, fullRoot, jsonOnly: false, out request))
        {
            return true;
        }

        return false;
    }

    private static bool TryCreateRequest(
        string input,
        string commandPrefix,
        string rootDirectory,
        bool jsonOnly,
        out FilePathSuggestionRequest? request)
    {
        request = null;
        if (!input.StartsWith(commandPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string pathText = input[commandPrefix.Length..];
        if (Path.IsPathRooted(pathText))
        {
            return false;
        }

        request = new FilePathSuggestionRequest(
            rootDirectory,
            commandPrefix,
            pathText,
            jsonOnly);
        return true;
    }

    private static string GetDirectoryPart(string pathText)
    {
        string normalized = NormalizeSeparators(pathText);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        if (normalized.EndsWith(Path.DirectorySeparatorChar))
        {
            return normalized.TrimEnd(Path.DirectorySeparatorChar);
        }

        return Path.GetDirectoryName(normalized) ?? string.Empty;
    }

    private static string GetNamePrefix(string pathText)
    {
        string normalized = NormalizeSeparators(pathText);
        if (normalized.Length == 0 ||
            normalized.EndsWith(Path.DirectorySeparatorChar))
        {
            return string.Empty;
        }

        return Path.GetFileName(normalized);
    }

    private static string NormalizeSeparators(string value)
    {
        string normalized = value.TrimStart();
        foreach (char separator in DirectorySeparators)
        {
            normalized = normalized.Replace(separator, Path.DirectorySeparatorChar);
        }

        return normalized;
    }

    private static bool TryResolveDirectory(
        string rootDirectory,
        string directoryPart,
        out string? fullDirectoryPath)
    {
        fullDirectoryPath = null;
        string fullRoot = Path.GetFullPath(rootDirectory);
        string candidate = Path.GetFullPath(Path.Combine(fullRoot, directoryPart));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!candidate.Equals(fullRoot, comparison) &&
            !candidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison))
        {
            return false;
        }

        fullDirectoryPath = candidate;
        return true;
    }

    private static IEnumerable<DirectoryInfo> EnumerateDirectories(string directory)
    {
        try
        {
            return new DirectoryInfo(directory).EnumerateDirectories();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<FileInfo> EnumerateFiles(string directory)
    {
        try
        {
            return new DirectoryInfo(directory).EnumerateFiles();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool ShouldSkipPath(
        string rootDirectory,
        string path)
    {
        string relativePath = ToDisplayPath(rootDirectory, path);
        string[] segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment =>
                string.Equals(segment, ".git", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return IsPathOrChild(relativePath, ".nanoagent/cache") ||
            IsPathOrChild(relativePath, ".nanoagent/logs") ||
            IsPathOrChild(relativePath, ".nanoagent/sessions");
    }

    private static bool IsPathOrChild(
        string relativePath,
        string skippedPath)
    {
        return string.Equals(relativePath, skippedPath, StringComparison.OrdinalIgnoreCase) ||
            relativePath.StartsWith(skippedPath + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string ToDisplayPath(
        string rootDirectory,
        string path)
    {
        return Path.GetRelativePath(rootDirectory, path)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private sealed record FilePathSuggestionRequest(
        string RootDirectory,
        string CommandPrefix,
        string PathText,
        bool JsonOnly);
}

internal readonly record struct FilePathSuggestion(
    string CompletedInput,
    string DisplayPath,
    string Description,
    bool IsDirectory);
