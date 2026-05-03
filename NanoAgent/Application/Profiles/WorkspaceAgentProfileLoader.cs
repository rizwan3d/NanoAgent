using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;

namespace NanoAgent.Application.Profiles;

internal static class WorkspaceAgentProfileLoader
{
    private const string AgentsDirectoryName = "agents";
    private const string WorkspaceDirectoryName = ".nanoagent";

    private static readonly IReadOnlySet<string> PrimaryEditingTools = new HashSet<string>(
        [
            AgentToolNames.AgentDelegate,
            AgentToolNames.AgentOrchestrate,
            AgentToolNames.ApplyPatch,
            AgentToolNames.CodebaseIndex,
            AgentToolNames.CodeIntelligence,
            AgentToolNames.DirectoryList,
            AgentToolNames.FileDelete,
            AgentToolNames.FileRead,
            AgentToolNames.FileWrite,
            AgentToolNames.HeadlessBrowser,
            AgentToolNames.LessonMemory,
            AgentToolNames.PlanningMode,
            AgentToolNames.RepoMemory,
            AgentToolNames.SearchFiles,
            AgentToolNames.ShellCommand,
            AgentToolNames.SkillLoad,
            AgentToolNames.TextSearch,
            AgentToolNames.UpdatePlan,
            AgentToolNames.WebRun
        ],
        StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> PrimaryReadOnlyTools = new HashSet<string>(
        [
            AgentToolNames.AgentDelegate,
            AgentToolNames.AgentOrchestrate,
            AgentToolNames.CodebaseIndex,
            AgentToolNames.CodeIntelligence,
            AgentToolNames.DirectoryList,
            AgentToolNames.FileRead,
            AgentToolNames.HeadlessBrowser,
            AgentToolNames.LessonMemory,
            AgentToolNames.PlanningMode,
            AgentToolNames.RepoMemory,
            AgentToolNames.SearchFiles,
            AgentToolNames.ShellCommand,
            AgentToolNames.SkillLoad,
            AgentToolNames.TextSearch,
            AgentToolNames.UpdatePlan,
            AgentToolNames.WebRun
        ],
        StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> SubagentEditingTools = new HashSet<string>(
        [
            AgentToolNames.ApplyPatch,
            AgentToolNames.CodebaseIndex,
            AgentToolNames.CodeIntelligence,
            AgentToolNames.DirectoryList,
            AgentToolNames.FileDelete,
            AgentToolNames.FileRead,
            AgentToolNames.FileWrite,
            AgentToolNames.HeadlessBrowser,
            AgentToolNames.LessonMemory,
            AgentToolNames.PlanningMode,
            AgentToolNames.RepoMemory,
            AgentToolNames.SearchFiles,
            AgentToolNames.ShellCommand,
            AgentToolNames.SkillLoad,
            AgentToolNames.TextSearch,
            AgentToolNames.WebRun
        ],
        StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> SubagentReadOnlyTools = new HashSet<string>(
        [
            AgentToolNames.DirectoryList,
            AgentToolNames.CodebaseIndex,
            AgentToolNames.CodeIntelligence,
            AgentToolNames.FileRead,
            AgentToolNames.HeadlessBrowser,
            AgentToolNames.LessonMemory,
            AgentToolNames.RepoMemory,
            AgentToolNames.SearchFiles,
            AgentToolNames.ShellCommand,
            AgentToolNames.SkillLoad,
            AgentToolNames.TextSearch,
            AgentToolNames.WebRun
        ],
        StringComparer.Ordinal);

    public static IReadOnlyList<IAgentProfile> Load(string? workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return [];
        }

        string agentsDirectory;
        try
        {
            agentsDirectory = Path.Combine(
                Path.GetFullPath(workspaceRoot.Trim()),
                WorkspaceDirectoryName,
                AgentsDirectoryName);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return [];
        }

        if (!Directory.Exists(agentsDirectory))
        {
            return [];
        }

        string[] profileFiles;
        try
        {
            profileFiles = Directory
                .EnumerateFiles(agentsDirectory, "*.md", SearchOption.TopDirectoryOnly)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        List<IAgentProfile> profiles = [];
        foreach (string profileFile in profileFiles)
        {
            if (TryLoadProfile(profileFile, out IAgentProfile? profile) &&
                profile is not null)
            {
                profiles.Add(profile);
            }
        }

        return profiles;
    }

    private static bool TryLoadProfile(
        string profileFile,
        out IAgentProfile? profile)
    {
        profile = null;

        string content;
        try
        {
            content = File.ReadAllText(profileFile);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        ParsedAgentProfileDocument document = ParseDocument(content);
        string fallbackName = Path.GetFileNameWithoutExtension(profileFile);
        string? profileName = NormalizeProfileName(GetFirstValue(document.Metadata, "name") ?? fallbackName);
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return false;
        }

        AgentProfileMode mode = ParseMode(GetFirstValue(document.Metadata, "mode"));
        AgentProfileEditMode editMode = ParseEditMode(document.Metadata);
        AgentProfileShellMode shellMode = ParseShellMode(document.Metadata, editMode);
        string description = GetFirstValue(document.Metadata, "description")
            ?? $"Workspace custom {mode.ToString().ToLowerInvariant()} agent '{profileName}'.";
        string behaviorIntent = GetFirstValue(document.Metadata, "permissionDescription")
            ?? GetFirstValue(document.Metadata, "behaviorIntent")
            ?? CreateDefaultBehaviorIntent(editMode, shellMode);
        IReadOnlySet<string> enabledTools = ParseEnabledTools(document.Metadata, mode, editMode);

        profile = new BuiltInAgentProfile(
            profileName,
            mode,
            description,
            document.Body,
            enabledTools,
            new AgentProfilePermissionOverlay(
                editMode,
                shellMode,
                behaviorIntent));
        return true;
    }

    private static ParsedAgentProfileDocument ParseDocument(string content)
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
        return new ParsedAgentProfileDocument(metadata, body);
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

    private static AgentProfileMode ParseMode(string? value)
    {
        string normalized = NormalizeOption(value);
        return normalized switch
        {
            "primary" => AgentProfileMode.Primary,
            _ => AgentProfileMode.Subagent
        };
    }

    private static AgentProfileEditMode ParseEditMode(Dictionary<string, List<string>> metadata)
    {
        string normalized = NormalizeOption(
            GetFirstValue(metadata, "editMode") ??
            GetFirstValue(metadata, "edit_mode") ??
            GetFirstValue(metadata, "edits"));

        return normalized switch
        {
            "allowedits" or "allow" or "allowed" or "edit" or "editing" or "write" or "writes" => AgentProfileEditMode.AllowEdits,
            _ => AgentProfileEditMode.ReadOnly
        };
    }

    private static AgentProfileShellMode ParseShellMode(
        Dictionary<string, List<string>> metadata,
        AgentProfileEditMode editMode)
    {
        string normalized = NormalizeOption(
            GetFirstValue(metadata, "shellMode") ??
            GetFirstValue(metadata, "shell_mode") ??
            GetFirstValue(metadata, "shell"));

        return normalized switch
        {
            "default" or "full" or "toolchain" or "mutating" => AgentProfileShellMode.Default,
            "safe" or "safeinspection" or "safeinspectiononly" or "readonly" => AgentProfileShellMode.SafeInspectionOnly,
            _ => editMode == AgentProfileEditMode.AllowEdits
                ? AgentProfileShellMode.Default
                : AgentProfileShellMode.SafeInspectionOnly
        };
    }

    private static IReadOnlySet<string> ParseEnabledTools(
        Dictionary<string, List<string>> metadata,
        AgentProfileMode mode,
        AgentProfileEditMode editMode)
    {
        if (!metadata.TryGetValue("tools", out List<string>? configuredTools) ||
            configuredTools.Count == 0)
        {
            return GetDefaultToolSet(mode, editMode);
        }

        HashSet<string> tools = new(StringComparer.Ordinal);
        foreach (string configuredTool in configuredTools.SelectMany(SplitToolValue))
        {
            tools.Add(configuredTool);
        }

        return tools.Count == 0
            ? GetDefaultToolSet(mode, editMode)
            : tools;
    }

    private static IReadOnlySet<string> GetDefaultToolSet(
        AgentProfileMode mode,
        AgentProfileEditMode editMode)
    {
        if (mode == AgentProfileMode.Primary)
        {
            return editMode == AgentProfileEditMode.AllowEdits
                ? PrimaryEditingTools
                : PrimaryReadOnlyTools;
        }

        return editMode == AgentProfileEditMode.AllowEdits
            ? SubagentEditingTools
            : SubagentReadOnlyTools;
    }

    private static IEnumerable<string> SplitToolValue(string value)
    {
        string normalizedValue = value.Trim();
        if (normalizedValue.StartsWith('[') && normalizedValue.EndsWith(']'))
        {
            normalizedValue = normalizedValue[1..^1];
        }

        foreach (string part in normalizedValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string normalizedPart = Unquote(part);
            if (!string.IsNullOrWhiteSpace(normalizedPart))
            {
                yield return normalizedPart;
            }
        }
    }

    private static string? NormalizeProfileName(string? value)
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

    private static string CreateDefaultBehaviorIntent(
        AgentProfileEditMode editMode,
        AgentProfileShellMode shellMode)
    {
        string editText = editMode == AgentProfileEditMode.AllowEdits
            ? "allows edits"
            : "stays read-only";
        string shellText = shellMode == AgentProfileShellMode.Default
            ? "normal shell execution"
            : "safe shell inspection only";

        return $"Workspace custom agent that {editText} with {shellText}.";
    }

    private static string? GetFirstValue(
        Dictionary<string, List<string>> metadata,
        string key)
    {
        return metadata.TryGetValue(key, out List<string>? values)
            ? values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))
            : null;
    }

    private static bool IsFence(string line)
    {
        return string.Equals(line.Trim(), "---", StringComparison.Ordinal);
    }

    private static string NormalizeOption(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value
                .Trim()
                .ToLowerInvariant()
                .Where(static character => char.IsLetterOrDigit(character))
                .ToArray());
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

    private sealed record ParsedAgentProfileDocument(
        Dictionary<string, List<string>> Metadata,
        string Body);
}
