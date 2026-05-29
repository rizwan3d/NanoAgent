using System.Text.Json;

namespace NanoAgent.Infrastructure.CodeIntelligence;

internal sealed class LanguageServerProfileConfiguration
{
    public string Key { get; }

    public string? Command { get; set; }

    public List<string> Args { get; } = [];

    public bool? Enabled { get; set; }

    public List<string> FileExtensions { get; } = [];

    public JsonElement? InitializationOptions { get; set; }

    public string? InstallHint { get; set; }

    public string? Language { get; set; }

    public string? LanguageId { get; set; }

    public string? Name { get; set; }

    public int? Priority { get; set; }

    public string? SourcePath { get; set; }

    public LanguageServerProfileConfiguration(string key)
    {
        Key = key;
    }

    public void ResolveRelativePaths(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(Command) ||
            Path.IsPathRooted(Command) ||
            string.IsNullOrWhiteSpace(SourcePath))
        {
            return;
        }

        string baseDirectory = Path.GetDirectoryName(SourcePath)
            ?? workspaceRoot;
        if (baseDirectory.EndsWith(".nanoagent", StringComparison.OrdinalIgnoreCase))
        {
            baseDirectory = workspaceRoot;
        }

        Command = Path.GetFullPath(Path.Combine(baseDirectory, Command));
    }

    public void Merge(LanguageServerProfileConfiguration other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (other.Command is not null)
        {
            Command = other.Command;
        }

        if (other.Args.Count > 0)
        {
            Args.Clear();
            Args.AddRange(other.Args);
        }

        if (other.Enabled.HasValue)
        {
            Enabled = other.Enabled;
        }

        if (other.FileExtensions.Count > 0)
        {
            FileExtensions.Clear();
            FileExtensions.AddRange(other.FileExtensions);
        }

        if (other.InitializationOptions.HasValue)
        {
            InitializationOptions = other.InitializationOptions.Value.Clone();
        }

        if (other.InstallHint is not null)
        {
            InstallHint = other.InstallHint;
        }

        if (other.Language is not null)
        {
            Language = other.Language;
        }

        if (other.LanguageId is not null)
        {
            LanguageId = other.LanguageId;
        }

        if (other.Name is not null)
        {
            Name = other.Name;
        }

        if (other.Priority.HasValue)
        {
            Priority = other.Priority;
        }

        if (other.SourcePath is not null)
        {
            SourcePath = other.SourcePath;
        }
    }
}
