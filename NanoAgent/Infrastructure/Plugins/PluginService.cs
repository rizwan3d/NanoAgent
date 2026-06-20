using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Utilities;
using System.Net;
using System.Text;
using System.Text.Json;

namespace NanoAgent.Infrastructure.Plugins;

internal sealed class PluginService : IPluginService
{
    private const string DefaultMarketplaceType = "github";
    private const string DefaultRef = "main";
    private const string ManifestFileName = "nanoagent-plugin.json";
    private const string MarketplaceIndexFileName = "nanoagent-marketplace.json";
    private const string PluginsDirectoryRelativePath = ".nanoagent/plugins";
    private const string InstalledFileName = "installed.json";
    private const string MarketplacesFileName = "marketplaces.json";
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly string[] AllowedTargetPrefixes =
    [
        ".nanoagent/skills/",
        ".nanoagent/commands/",
        ".nanoagent/plugins/",
        ".nanoagent/memory/"
    ];

    private readonly HttpClient _httpClient;

    public PluginService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<PluginMarketplaceAddResult> AddMarketplaceAsync(
        string workspacePath,
        string repository,
        string? alias,
        string? reference,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string workspaceRoot = NormalizeWorkspaceRoot(workspacePath);
        (string owner, string repo) = ParseRepository(repository);
        string normalizedAlias = NormalizeAlias(alias) ?? repo;
        string normalizedRef = NormalizeOptional(reference) ?? DefaultRef;

        PluginMarketplaceConfig config = await LoadMarketplaceConfigAsync(workspaceRoot, cancellationToken);
        Dictionary<string, PluginMarketplaceEntry> marketplaces = NormalizeMarketplaces(config.Marketplaces);
        PluginMarketplaceEntry entry = new()
        {
            Type = DefaultMarketplaceType,
            Repository = $"{owner}/{repo}",
            Ref = normalizedRef
        };

        marketplaces[normalizedAlias] = entry;
        await SaveMarketplaceConfigAsync(
            workspaceRoot,
            new PluginMarketplaceConfig
            {
                Marketplaces = marketplaces
            },
            cancellationToken);

        return new PluginMarketplaceAddResult(normalizedAlias, entry);
    }

    public async Task<PluginInstallResult> InstallAsync(
        string workspacePath,
        string pluginId,
        string marketplaceAlias,
        bool force,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string workspaceRoot = NormalizeWorkspaceRoot(workspacePath);
        string normalizedPluginId = NormalizePluginId(pluginId);
        string normalizedMarketplaceAlias = NormalizeAlias(marketplaceAlias)
            ?? throw new InvalidOperationException("Marketplace alias cannot be empty.");

        PluginMarketplaceConfig config = await LoadMarketplaceConfigAsync(workspaceRoot, cancellationToken);
        Dictionary<string, PluginMarketplaceEntry> marketplaces = NormalizeMarketplaces(config.Marketplaces);
        if (!marketplaces.TryGetValue(normalizedMarketplaceAlias, out PluginMarketplaceEntry? marketplace))
        {
            throw new InvalidOperationException(
                $"Plugin marketplace '{normalizedMarketplaceAlias}' is not configured.");
        }

        PluginManifest? manifest = await TryLoadManifestAsync(marketplace, cancellationToken);
        IReadOnlyList<PluginManifestFile> manifestFiles = manifest is null
            ? await BuildConventionFallbackAsync(normalizedPluginId, marketplace, cancellationToken)
            : ValidateManifest(normalizedPluginId, manifest);

        if (manifestFiles.Count == 0)
        {
            throw new InvalidOperationException(
                $"Plugin '{normalizedPluginId}' did not expose any installable files.");
        }

        List<PlannedPluginFile> plannedFiles = [];
        foreach (PluginManifestFile manifestFile in manifestFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string normalizedSource = NormalizeManifestSource(manifestFile.Source);
            string normalizedTarget = NormalizeManifestTarget(workspaceRoot, manifestFile.Target);
            string fullTargetPath = WorkspacePath.Resolve(workspaceRoot, normalizedTarget);

            if (Directory.Exists(fullTargetPath))
            {
                throw new InvalidOperationException(
                    $"Plugin target '{normalizedTarget}' is a directory, not a file.");
            }

            if (!force && File.Exists(fullTargetPath))
            {
                throw new InvalidOperationException(
                    $"Plugin target '{normalizedTarget}' already exists. Re-run with --force to overwrite it.");
            }

            string content = await FetchRequiredTextFileAsync(
                marketplace,
                normalizedSource,
                cancellationToken);
            plannedFiles.Add(new PlannedPluginFile(normalizedSource, normalizedTarget, fullTargetPath, content));
        }

        foreach (PlannedPluginFile plannedFile in plannedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? parentDirectory = Path.GetDirectoryName(plannedFile.FullTargetPath);
            if (!string.IsNullOrWhiteSpace(parentDirectory))
            {
                Directory.CreateDirectory(parentDirectory);
            }

            await File.WriteAllTextAsync(
                plannedFile.FullTargetPath,
                plannedFile.Content,
                Utf8NoBom,
                cancellationToken);
        }

        InstalledPluginLock lockFile = await LoadInstalledPluginLockAsync(workspaceRoot, cancellationToken);
        Dictionary<string, InstalledPluginEntry> plugins = NormalizeInstalledPlugins(lockFile.Plugins);
        InstalledPluginEntry installedEntry = new()
        {
            PluginId = normalizedPluginId,
            MarketplaceAlias = normalizedMarketplaceAlias,
            SourceType = NormalizeOptional(marketplace.Type) ?? DefaultMarketplaceType,
            Repository = marketplace.Repository,
            Ref = NormalizeOptional(marketplace.Ref) ?? DefaultRef,
            Files = plannedFiles
                .Select(static file => file.TargetRelativePath)
                .ToList()
        };

        plugins[normalizedPluginId] = installedEntry;
        await SaveInstalledPluginLockAsync(
            workspaceRoot,
            new InstalledPluginLock
            {
                Plugins = plugins
            },
            cancellationToken);

        return new PluginInstallResult(installedEntry, manifest is not null);
    }

    public async Task<PluginMarketplaceRemoveResult> RemoveMarketplaceAsync(
        string workspacePath,
        string alias,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string workspaceRoot = NormalizeWorkspaceRoot(workspacePath);
        string normalizedAlias = NormalizeAlias(alias)
            ?? throw new InvalidOperationException("Marketplace alias cannot be empty.");

        PluginMarketplaceConfig config = await LoadMarketplaceConfigAsync(workspaceRoot, cancellationToken);
        Dictionary<string, PluginMarketplaceEntry> marketplaces = NormalizeMarketplaces(config.Marketplaces);
        if (!marketplaces.TryGetValue(normalizedAlias, out PluginMarketplaceEntry? removed))
        {
            throw new InvalidOperationException(
                $"Plugin marketplace '{normalizedAlias}' is not configured.");
        }

        marketplaces.Remove(normalizedAlias);
        await SaveMarketplaceConfigAsync(
            workspaceRoot,
            new PluginMarketplaceConfig
            {
                Marketplaces = marketplaces
            },
            cancellationToken);

        return new PluginMarketplaceRemoveResult(normalizedAlias, removed);
    }

    public async Task<PluginBrowseResult> BrowseMarketplaceAsync(
        string workspacePath,
        string marketplaceAlias,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string workspaceRoot = NormalizeWorkspaceRoot(workspacePath);
        string normalizedAlias = NormalizeAlias(marketplaceAlias)
            ?? throw new InvalidOperationException("Marketplace alias cannot be empty.");

        PluginMarketplaceConfig config = await LoadMarketplaceConfigAsync(workspaceRoot, cancellationToken);
        Dictionary<string, PluginMarketplaceEntry> marketplaces = NormalizeMarketplaces(config.Marketplaces);
        if (!marketplaces.TryGetValue(normalizedAlias, out PluginMarketplaceEntry? marketplace))
        {
            throw new InvalidOperationException(
                $"Plugin marketplace '{normalizedAlias}' is not configured.");
        }

        string? indexJson = await TryFetchTextFileAsync(
            marketplace,
            MarketplaceIndexFileName,
            cancellationToken);
        if (indexJson is null)
        {
            throw new InvalidOperationException(
                $"Marketplace '{normalizedAlias}' does not publish a {MarketplaceIndexFileName} index.");
        }

        PluginMarketplaceIndex? index;
        try
        {
            index = JsonSerializer.Deserialize(
                indexJson,
                PluginJsonContext.Default.PluginMarketplaceIndex);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Marketplace index '{MarketplaceIndexFileName}' is not valid JSON.",
                exception);
        }

        IReadOnlyList<PluginIndexEntry> entries = (index?.Plugins ?? [])
            .Where(static entry => entry is not null && !string.IsNullOrWhiteSpace(entry.Id))
            .Select(static entry => new PluginIndexEntry
            {
                Id = entry.Id.Trim(),
                Name = NormalizeOptional(entry.Name),
                Description = NormalizeOptional(entry.Description)
            })
            .ToArray();

        return new PluginBrowseResult(normalizedAlias, marketplace, entries);
    }

    public async Task<PluginListResult> ListAsync(
        string workspacePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string workspaceRoot = NormalizeWorkspaceRoot(workspacePath);
        PluginMarketplaceConfig config = await LoadMarketplaceConfigAsync(workspaceRoot, cancellationToken);
        InstalledPluginLock lockFile = await LoadInstalledPluginLockAsync(workspaceRoot, cancellationToken);

        return new PluginListResult(
            new PluginMarketplaceConfig
            {
                Marketplaces = NormalizeMarketplaces(config.Marketplaces)
            },
            new InstalledPluginLock
            {
                Plugins = NormalizeInstalledPlugins(lockFile.Plugins)
            });
    }

    public async Task<PluginUninstallResult> UninstallAsync(
        string workspacePath,
        string pluginId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string workspaceRoot = NormalizeWorkspaceRoot(workspacePath);
        string normalizedPluginId = NormalizePluginId(pluginId);

        InstalledPluginLock lockFile = await LoadInstalledPluginLockAsync(workspaceRoot, cancellationToken);
        Dictionary<string, InstalledPluginEntry> plugins = NormalizeInstalledPlugins(lockFile.Plugins);
        if (!plugins.TryGetValue(normalizedPluginId, out InstalledPluginEntry? installedPlugin))
        {
            throw new InvalidOperationException($"Plugin '{normalizedPluginId}' is not installed.");
        }

        List<string> removedFiles = [];
        foreach (string relativePath in installedPlugin.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string normalizedTarget = NormalizeManifestTarget(workspaceRoot, relativePath);
            string fullPath = WorkspacePath.Resolve(workspaceRoot, normalizedTarget);
            if (!File.Exists(fullPath))
            {
                continue;
            }

            File.Delete(fullPath);
            removedFiles.Add(normalizedTarget);
            TryDeleteEmptyPluginOwnedDirectories(workspaceRoot, normalizedTarget);
        }

        plugins.Remove(normalizedPluginId);
        await SaveInstalledPluginLockAsync(
            workspaceRoot,
            new InstalledPluginLock
            {
                Plugins = plugins
            },
            cancellationToken);

        return new PluginUninstallResult(normalizedPluginId, removedFiles);
    }

    private async Task<PluginMarketplaceConfig> LoadMarketplaceConfigAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        string filePath = GetMarketplaceConfigPath(workspaceRoot);
        if (!File.Exists(filePath))
        {
            return new PluginMarketplaceConfig();
        }

        string json = await File.ReadAllTextAsync(filePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new PluginMarketplaceConfig();
        }

        try
        {
            return JsonSerializer.Deserialize(
                       json,
                       PluginJsonContext.Default.PluginMarketplaceConfig)
                   ?? new PluginMarketplaceConfig();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"{WorkspacePath.ToRelativePath(workspaceRoot, filePath)} is not valid JSON.",
                exception);
        }
    }

    private async Task SaveMarketplaceConfigAsync(
        string workspaceRoot,
        PluginMarketplaceConfig config,
        CancellationToken cancellationToken)
    {
        string filePath = GetMarketplaceConfigPath(workspaceRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        string json = JsonSerializer.Serialize(
            config,
            PluginJsonContext.Default.PluginMarketplaceConfig);
        await File.WriteAllTextAsync(
            filePath,
            json + Environment.NewLine,
            Utf8NoBom,
            cancellationToken);
    }

    private async Task<InstalledPluginLock> LoadInstalledPluginLockAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        string filePath = GetInstalledLockPath(workspaceRoot);
        if (!File.Exists(filePath))
        {
            return new InstalledPluginLock();
        }

        string json = await File.ReadAllTextAsync(filePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new InstalledPluginLock();
        }

        try
        {
            return JsonSerializer.Deserialize(
                       json,
                       PluginJsonContext.Default.InstalledPluginLock)
                   ?? new InstalledPluginLock();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"{WorkspacePath.ToRelativePath(workspaceRoot, filePath)} is not valid JSON.",
                exception);
        }
    }

    private async Task SaveInstalledPluginLockAsync(
        string workspaceRoot,
        InstalledPluginLock lockFile,
        CancellationToken cancellationToken)
    {
        string filePath = GetInstalledLockPath(workspaceRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        string json = JsonSerializer.Serialize(
            lockFile,
            PluginJsonContext.Default.InstalledPluginLock);
        await File.WriteAllTextAsync(
            filePath,
            json + Environment.NewLine,
            Utf8NoBom,
            cancellationToken);
    }

    private async Task<PluginManifest?> TryLoadManifestAsync(
        PluginMarketplaceEntry marketplace,
        CancellationToken cancellationToken)
    {
        string? manifestJson = await TryFetchTextFileAsync(
            marketplace,
            ManifestFileName,
            cancellationToken);
        if (manifestJson is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(
                manifestJson,
                PluginJsonContext.Default.PluginManifest);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Plugin manifest '{ManifestFileName}' is not valid JSON.",
                exception);
        }
    }

    private async Task<IReadOnlyList<PluginManifestFile>> BuildConventionFallbackAsync(
        string pluginId,
        PluginMarketplaceEntry marketplace,
        CancellationToken cancellationToken)
    {
        List<PluginManifestFile> files = [];

        string skillSource = $"skills/{pluginId}/SKILL.md";
        if (await TryFetchTextFileAsync(marketplace, skillSource, cancellationToken) is not null)
        {
            files.Add(new PluginManifestFile
            {
                Source = skillSource,
                Target = $".nanoagent/skills/{pluginId}/SKILL.md",
                Kind = "skill"
            });
        }

        const string agentsSource = "AGENTS.md";
        if (await TryFetchTextFileAsync(marketplace, agentsSource, cancellationToken) is not null)
        {
            files.Add(new PluginManifestFile
            {
                Source = agentsSource,
                Target = $".nanoagent/plugins/{pluginId}/AGENTS.md",
                Kind = "instructions"
            });
        }

        return files;
    }

    private static IReadOnlyList<PluginManifestFile> ValidateManifest(
        string requestedPluginId,
        PluginManifest manifest)
    {
        if (!string.IsNullOrWhiteSpace(manifest.Id) &&
            !string.Equals(manifest.Id.Trim(), requestedPluginId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Plugin manifest id '{manifest.Id.Trim()}' does not match requested plugin id '{requestedPluginId}'.");
        }

        return (manifest.Files ?? [])
            .Where(static file => file is not null)
            .ToArray();
    }

    private async Task<string> FetchRequiredTextFileAsync(
        PluginMarketplaceEntry marketplace,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        return await TryFetchTextFileAsync(marketplace, sourcePath, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Plugin source file '{sourcePath}' was not found in {marketplace.Repository}@{NormalizeOptional(marketplace.Ref) ?? DefaultRef}.");
    }

    private async Task<string?> TryFetchTextFileAsync(
        PluginMarketplaceEntry marketplace,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        string url = BuildRawGitHubUrl(
            marketplace.Repository,
            NormalizeOptional(marketplace.Ref) ?? DefaultRef,
            sourcePath);

        using HttpRequestMessage request = new(HttpMethod.Get, url);
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"GitHub returned HTTP {(int)response.StatusCode} while fetching '{sourcePath}' from {marketplace.Repository}@{NormalizeOptional(marketplace.Ref) ?? DefaultRef}.");
        }

        return body;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException(
                $"Unable to fetch plugin content from '{request.RequestUri}'.",
                exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                $"Timed out while fetching plugin content from '{request.RequestUri}'.",
                exception);
        }
    }

    private static string BuildRawGitHubUrl(
        string repository,
        string reference,
        string sourcePath)
    {
        (string owner, string repo) = ParseRepository(repository);
        string encodedRef = Uri.EscapeDataString(reference.Trim());
        string encodedPath = string.Join(
            "/",
            sourcePath
                .Replace("\\", "/", StringComparison.Ordinal)
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));

        return $"https://raw.githubusercontent.com/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/{encodedRef}/{encodedPath}";
    }

    private static (string Owner, string Repo) ParseRepository(string repository)
    {
        string normalized = NormalizeOptional(repository)
            ?? throw new InvalidOperationException("Repository cannot be empty.");
        string[] parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            parts.Any(static part => part.Contains("\\", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "Repository must be in '<owner>/<repo>' format.");
        }

        return (parts[0], parts[1]);
    }

    private static string NormalizeWorkspaceRoot(string workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            throw new InvalidOperationException("Workspace path cannot be empty.");
        }

        return Path.GetFullPath(workspacePath.Trim());
    }

    private static string NormalizePluginId(string pluginId)
    {
        string normalized = NormalizeOptional(pluginId)
            ?? throw new InvalidOperationException("Plugin id cannot be empty.");

        return IsSimpleIdentifier(normalized)
            ? normalized
            : throw new InvalidOperationException(
                $"Plugin id '{pluginId}' is invalid. Use letters, digits, '.', '-', or '_'.");
    }

    private static string? NormalizeAlias(string? alias)
    {
        string? normalized = NormalizeOptional(alias);
        if (normalized is null)
        {
            return null;
        }

        return IsSimpleIdentifier(normalized)
            ? normalized
            : throw new InvalidOperationException(
                $"Marketplace alias '{alias}' is invalid. Use letters, digits, '.', '-', or '_'.");
    }

    private static string NormalizeManifestSource(string source)
    {
        string normalized = NormalizeRelativePath(source);
        if (normalized.StartsWith(".git/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Plugin sources cannot target .git paths.");
        }

        return normalized;
    }

    private static string NormalizeManifestTarget(
        string workspaceRoot,
        string target)
    {
        string normalized = NormalizeRelativePath(target);
        if (!AllowedTargetPrefixes.Any(prefix => normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Plugin target '{target}' is not allowed. Targets must stay under .nanoagent/skills, .nanoagent/commands, .nanoagent/plugins, or .nanoagent/memory.");
        }

        if (normalized.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/.git/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Plugin target '{target}' is not allowed.");
        }

        string fullPath = WorkspacePath.Resolve(workspaceRoot, normalized);
        if (!WorkspacePath.IsSamePathOrDescendant(workspaceRoot, fullPath))
        {
            throw new InvalidOperationException(
                $"Plugin target '{target}' escapes the workspace.");
        }

        return WorkspacePath.ToRelativePath(workspaceRoot, fullPath);
    }

    private static string NormalizeRelativePath(string path)
    {
        string normalized = NormalizeOptional(path)
            ?? throw new InvalidOperationException("Plugin file paths cannot be empty.");
        normalized = normalized.Replace("\\", "/", StringComparison.Ordinal);

        if (normalized.StartsWith("/", StringComparison.Ordinal) ||
            Path.IsPathRooted(normalized))
        {
            throw new InvalidOperationException(
                $"Plugin path '{path}' must be relative.");
        }

        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0 ||
            segments.Any(static segment => segment == "." || segment == ".."))
        {
            throw new InvalidOperationException(
                $"Plugin path '{path}' is not allowed.");
        }

        return string.Join("/", segments);
    }

    private static bool IsSimpleIdentifier(string value)
    {
        return value.All(static character =>
            char.IsLetterOrDigit(character) ||
            character is '.' or '-' or '_');
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static string GetPluginDirectoryPath(string workspaceRoot)
    {
        return WorkspacePath.Resolve(workspaceRoot, PluginsDirectoryRelativePath);
    }

    private static string GetMarketplaceConfigPath(string workspaceRoot)
    {
        return Path.Combine(GetPluginDirectoryPath(workspaceRoot), MarketplacesFileName);
    }

    private static string GetInstalledLockPath(string workspaceRoot)
    {
        return Path.Combine(GetPluginDirectoryPath(workspaceRoot), InstalledFileName);
    }

    private static Dictionary<string, PluginMarketplaceEntry> NormalizeMarketplaces(
        IDictionary<string, PluginMarketplaceEntry>? marketplaces)
    {
        Dictionary<string, PluginMarketplaceEntry> normalized = new(StringComparer.OrdinalIgnoreCase);
        if (marketplaces is null)
        {
            return normalized;
        }

        foreach ((string key, PluginMarketplaceEntry value) in marketplaces)
        {
            string? alias = NormalizeAlias(key);
            if (alias is null || value is null || string.IsNullOrWhiteSpace(value.Repository))
            {
                continue;
            }

            normalized[alias] = new PluginMarketplaceEntry
            {
                Type = NormalizeOptional(value.Type) ?? DefaultMarketplaceType,
                Repository = value.Repository.Trim(),
                Ref = NormalizeOptional(value.Ref) ?? DefaultRef
            };
        }

        return normalized;
    }

    private static Dictionary<string, InstalledPluginEntry> NormalizeInstalledPlugins(
        IDictionary<string, InstalledPluginEntry>? plugins)
    {
        Dictionary<string, InstalledPluginEntry> normalized = new(StringComparer.OrdinalIgnoreCase);
        if (plugins is null)
        {
            return normalized;
        }

        foreach ((string key, InstalledPluginEntry value) in plugins)
        {
            string pluginId;
            try
            {
                pluginId = NormalizePluginId(key);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            if (value is null)
            {
                continue;
            }

            normalized[pluginId] = new InstalledPluginEntry
            {
                PluginId = pluginId,
                MarketplaceAlias = NormalizeAlias(value.MarketplaceAlias) ?? string.Empty,
                SourceType = NormalizeOptional(value.SourceType) ?? DefaultMarketplaceType,
                Repository = NormalizeOptional(value.Repository) ?? string.Empty,
                Ref = NormalizeOptional(value.Ref) ?? DefaultRef,
                Files = (value.Files ?? new List<string>())
                    .Where(static path => !string.IsNullOrWhiteSpace(path))
                    .Select(static path => path.Replace("\\", "/", StringComparison.Ordinal))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
        }

        return normalized;
    }

    private static void TryDeleteEmptyPluginOwnedDirectories(
        string workspaceRoot,
        string relativePath)
    {
        string normalizedRelativePath = relativePath.Replace("\\", "/", StringComparison.Ordinal);
        string? sharedRoot = AllowedTargetPrefixes
            .FirstOrDefault(prefix => normalizedRelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (sharedRoot is null)
        {
            return;
        }

        string stopDirectory = WorkspacePath.Resolve(
            workspaceRoot,
            sharedRoot.TrimEnd('/'));
        string? currentDirectory = Path.GetDirectoryName(
            WorkspacePath.Resolve(workspaceRoot, normalizedRelativePath));

        while (!string.IsNullOrWhiteSpace(currentDirectory) &&
               WorkspacePath.IsSamePathOrDescendant(stopDirectory, currentDirectory) &&
               !WorkspacePath.PathEquals(currentDirectory, stopDirectory))
        {
            try
            {
                if (Directory.Exists(currentDirectory) &&
                    !Directory.EnumerateFileSystemEntries(currentDirectory).Any())
                {
                    Directory.Delete(currentDirectory);
                    currentDirectory = Path.GetDirectoryName(currentDirectory);
                    continue;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            break;
        }
    }

    private sealed record PlannedPluginFile(
        string SourcePath,
        string TargetRelativePath,
        string FullTargetPath,
        string Content);
}
