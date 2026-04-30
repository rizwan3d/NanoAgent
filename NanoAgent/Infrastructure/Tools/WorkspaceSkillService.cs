using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Utilities;
using System.Text;

namespace NanoAgent.Infrastructure.Tools;

internal sealed class WorkspaceSkillService : ISkillService
{
    private const int MaxDescriptionCharacters = 1_000;
    private const int MaxInstructionCharacters = 24_000;
    private const string SkillFileName = "SKILL.md";
    private const string SkillsDirectoryName = "skills";
    private const string WorkspaceDirectoryName = ".nanoagent";

    public Task<IReadOnlyList<WorkspaceSkillDescriptor>> ListAsync(
        ReplSessionContext session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(LoadSkillIndex(session.WorkspacePath));
    }

    public Task<string?> CreateRoutingPromptAsync(
        ReplSessionContext session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<WorkspaceSkillDescriptor> skills = LoadSkillIndex(session.WorkspacePath);
        if (skills.Count == 0)
        {
            return Task.FromResult<string?>(null);
        }

        StringBuilder builder = new();
        builder.AppendLine("Workspace skills:");
        builder.AppendLine("Use the following skill names and descriptions only as routing signals. The skill body instructions are not loaded yet. When a skill is relevant, call `skill_load` with its name before following that skill.");

        foreach (WorkspaceSkillDescriptor skill in skills)
        {
            builder.AppendLine();
            builder.Append("<workspace_skill name=\"");
            builder.Append(EscapeAttribute(skill.Name));
            builder.Append("\" path=\"");
            builder.Append(EscapeAttribute(skill.Path));
            builder.AppendLine("\">");
            builder.AppendLine(SecretRedactor.Redact(skill.Description));
            builder.AppendLine("</workspace_skill>");
        }

        return Task.FromResult<string?>(builder.ToString().Trim());
    }

    public async Task<WorkspaceSkillLoadResult?> LoadAsync(
        ReplSessionContext session,
        string name,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();

        string? normalizedName = NormalizeSkillName(name);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return null;
        }

        string workspaceRoot = GetWorkspaceRoot(session.WorkspacePath);
        SkillFile? skillFile = FindSkillFiles(workspaceRoot)
            .FirstOrDefault(skill =>
                string.Equals(skill.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
        if (skillFile is null)
        {
            return null;
        }

        string content;
        try
        {
            content = await File.ReadAllTextAsync(skillFile.FullPath, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        ParsedSkillDocument document = ParseDocument(content);
        string instructions = NormalizeContent(
            SecretRedactor.Redact(document.Body),
            MaxInstructionCharacters,
            out bool wasTruncated);

        return new WorkspaceSkillLoadResult(
            skillFile.Name,
            skillFile.Description,
            skillFile.RelativePath,
            instructions,
            instructions.Length,
            wasTruncated);
    }

    private static IReadOnlyList<WorkspaceSkillDescriptor> LoadSkillIndex(string workspacePath)
    {
        string workspaceRoot = GetWorkspaceRoot(workspacePath);
        return FindSkillFiles(workspaceRoot)
            .Select(static skill => new WorkspaceSkillDescriptor(
                skill.Name,
                skill.Description,
                skill.RelativePath))
            .ToArray();
    }

    private static IReadOnlyList<SkillFile> FindSkillFiles(string workspaceRoot)
    {
        string skillsDirectory = Path.Combine(
            workspaceRoot,
            WorkspaceDirectoryName,
            SkillsDirectoryName);
        if (!Directory.Exists(skillsDirectory))
        {
            return [];
        }

        List<string> candidateFiles = [];
        try
        {
            candidateFiles.AddRange(Directory
                .EnumerateFiles(skillsDirectory, "*.md", SearchOption.TopDirectoryOnly)
                .Where(static file => !string.Equals(
                    Path.GetFileName(file),
                    SkillFileName,
                    StringComparison.OrdinalIgnoreCase)));

            foreach (string skillDirectory in Directory.EnumerateDirectories(
                         skillsDirectory,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                string skillFile = Path.Combine(skillDirectory, SkillFileName);
                if (File.Exists(skillFile))
                {
                    candidateFiles.Add(skillFile);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        List<SkillFile> skills = [];
        HashSet<string> seenNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (string candidateFile in candidateFiles.Order(StringComparer.OrdinalIgnoreCase))
        {
            if (TryLoadSkillFile(workspaceRoot, candidateFile, out SkillFile? skillFile) &&
                skillFile is not null &&
                seenNames.Add(skillFile.Name))
            {
                skills.Add(skillFile);
            }
        }

        return skills;
    }

    private static bool TryLoadSkillFile(
        string workspaceRoot,
        string fullPath,
        out SkillFile? skillFile)
    {
        skillFile = null;

        string content;
        try
        {
            content = File.ReadAllText(fullPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        ParsedSkillDocument document = ParseDocument(content);
        string? fallbackName = string.Equals(
            Path.GetFileName(fullPath),
            SkillFileName,
            StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileName(Path.GetDirectoryName(fullPath))
            : Path.GetFileNameWithoutExtension(fullPath);
        if (string.IsNullOrWhiteSpace(fallbackName))
        {
            return false;
        }

        string? skillName = NormalizeSkillName(GetFirstValue(document.Metadata, "name") ?? fallbackName);
        if (string.IsNullOrWhiteSpace(skillName))
        {
            return false;
        }

        string description = GetFirstValue(document.Metadata, "description")
            ?? $"Workspace skill '{skillName}'.";
        description = NormalizeContent(
            description,
            MaxDescriptionCharacters,
            out _);
        if (string.IsNullOrWhiteSpace(description))
        {
            return false;
        }

        skillFile = new SkillFile(
            skillName,
            description,
            WorkspacePath.ToRelativePath(workspaceRoot, fullPath),
            fullPath);
        return true;
    }

    private static ParsedSkillDocument ParseDocument(string content)
    {
        string normalizedContent = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        string[] lines = normalizedContent.Split('\n');
        Dictionary<string, List<string>> metadata = new(StringComparer.OrdinalIgnoreCase);
        int bodyStartIndex = 0;

        if (lines.Length > 0 && IsFence(lines[0]))
        {
            int closingFenceIndex = FindClosingFenceIndex(lines);
            if (closingFenceIndex > 0)
            {
                ParseMetadata(lines.AsSpan(1, closingFenceIndex - 1), metadata);
                bodyStartIndex = closingFenceIndex + 1;
            }
        }

        string body = string.Join('\n', lines.Skip(bodyStartIndex)).Trim();
        return new ParsedSkillDocument(metadata, body);
    }

    private static int FindClosingFenceIndex(string[] lines)
    {
        for (int index = 1; index < lines.Length; index++)
        {
            if (IsFence(lines[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static void ParseMetadata(
        ReadOnlySpan<string> lines,
        Dictionary<string, List<string>> metadata)
    {
        string? activeListKey = null;

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(activeListKey))
            {
                AddMetadataValue(metadata, activeListKey, line[2..]);
                continue;
            }

            int separatorIndex = line.IndexOf(':', StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                activeListKey = null;
                continue;
            }

            string key = line[..separatorIndex].Trim();
            string value = line[(separatorIndex + 1)..].Trim();
            activeListKey = key;

            if (value.Length > 0)
            {
                AddMetadataValue(metadata, key, value);
            }
            else if (!metadata.ContainsKey(key))
            {
                metadata[key] = [];
            }
        }
    }

    private static void AddMetadataValue(
        Dictionary<string, List<string>> metadata,
        string key,
        string value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (!metadata.TryGetValue(key.Trim(), out List<string>? values))
        {
            values = [];
            metadata[key.Trim()] = values;
        }

        string normalizedValue = Unquote(value.Trim());
        if (normalizedValue.Length > 0)
        {
            values.Add(normalizedValue);
        }
    }

    private static string NormalizeContent(
        string content,
        int maxCharacters,
        out bool wasTruncated)
    {
        string normalized = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

        if (normalized.Length <= maxCharacters)
        {
            wasTruncated = false;
            return normalized;
        }

        wasTruncated = true;
        return normalized[..maxCharacters].TrimEnd();
    }

    private static string? NormalizeSkillName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim().ToLowerInvariant().Replace('_', '-');
        char[] buffer = new char[normalized.Length];
        int length = 0;
        bool previousWasSeparator = false;

        foreach (char character in normalized)
        {
            if (char.IsLetterOrDigit(character) || character is '.' or '-')
            {
                buffer[length++] = character;
                previousWasSeparator = character is '.' or '-';
                continue;
            }

            if (char.IsWhiteSpace(character) && !previousWasSeparator && length > 0)
            {
                buffer[length++] = '-';
                previousWasSeparator = true;
            }
        }

        return new string(buffer, 0, length).Trim('.', '-');
    }

    private static string? GetFirstValue(
        Dictionary<string, List<string>> metadata,
        string key)
    {
        return metadata.TryGetValue(key, out List<string>? values)
            ? values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))
            : null;
    }

    private static string GetWorkspaceRoot(string workspacePath)
    {
        return Path.GetFullPath(workspacePath);
    }

    private static bool IsFence(string line)
    {
        return string.Equals(line.Trim(), "---", StringComparison.Ordinal);
    }

    private static string Unquote(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '"' && trimmed[^1] == '"') ||
             (trimmed[0] == '\'' && trimmed[^1] == '\'')))
        {
            return trimmed[1..^1].Trim();
        }

        return trimmed;
    }

    private static string EscapeAttribute(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }

    private sealed record ParsedSkillDocument(
        Dictionary<string, List<string>> Metadata,
        string Body);

    private sealed record SkillFile(
        string Name,
        string Description,
        string RelativePath,
        string FullPath);
}
