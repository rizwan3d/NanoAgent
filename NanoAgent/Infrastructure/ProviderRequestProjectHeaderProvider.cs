using NanoAgent.Application.Abstractions;

namespace NanoAgent.Infrastructure;

internal sealed class ProviderRequestProjectHeaderProvider
{
    internal const string DefaultRequestTitle = "NanoAgent";
    internal const string ProjectNameEnvironmentVariableName = "NANOAGENT_PROJECT_NAME";
    internal const string TitleEnvironmentVariableName = "NANOAGENT_TITLE";
    private const string GitMetadataDirectoryName = ".git";
    private const string FallbackProjectName = "workspace";

    private readonly IWorkspaceRootProvider _workspaceRootProvider;

    public ProviderRequestProjectHeaderProvider(IWorkspaceRootProvider workspaceRootProvider)
    {
        _workspaceRootProvider = workspaceRootProvider;
    }

    public string GetProjectName()
    {
        return GetConfiguredProjectName() ?? ResolveProjectName(_workspaceRootProvider.GetWorkspaceRoot());
    }

    public static string? GetConfiguredProjectName()
    {
        return NormalizeProjectName(Environment.GetEnvironmentVariable(ProjectNameEnvironmentVariableName)) ??
            NormalizeProjectName(Environment.GetEnvironmentVariable(TitleEnvironmentVariableName));
    }

    public static string? GetConfiguredTitle()
    {
        return NormalizeProjectName(Environment.GetEnvironmentVariable(TitleEnvironmentVariableName));
    }

    internal static string ResolveProjectName(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return FallbackProjectName;
        }

        string fullPath = Path.GetFullPath(workspaceRoot);
        DirectoryInfo? repositoryRoot = FindRepositoryRoot(fullPath);
        if (repositoryRoot is not null)
        {
            return GetDirectoryName(repositoryRoot.FullName);
        }

        return GetDirectoryName(fullPath);
    }

    private static DirectoryInfo? FindRepositoryRoot(string workspaceRoot)
    {
        for (DirectoryInfo? current = new DirectoryInfo(workspaceRoot); current is not null; current = current.Parent)
        {
            string gitPath = Path.Combine(current.FullName, GitMetadataDirectoryName);
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                return current;
            }
        }

        return null;
    }

    private static string GetDirectoryName(string path)
    {
        string trimmedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string name = Path.GetFileName(trimmedPath);
        return string.IsNullOrWhiteSpace(name)
            ? FallbackProjectName
            : name;
    }

    private static string? NormalizeProjectName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = new(
            value
                .Trim()
                .Where(static character => !char.IsControl(character))
                .ToArray());

        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : normalized;
    }
}
