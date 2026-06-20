namespace NanoAgent.Application.Models;

public sealed class PluginMarketplaceConfig
{
    public Dictionary<string, PluginMarketplaceEntry> Marketplaces { get; init; } = [];
}

public sealed class PluginMarketplaceEntry
{
    public string Type { get; init; } = "github";

    public string Repository { get; init; } = string.Empty;

    public string Ref { get; init; } = "main";
}

public sealed class PluginManifest
{
    public string Id { get; init; } = string.Empty;

    public string? Name { get; init; }

    public string? Version { get; init; }

    public string? Description { get; init; }

    public string? License { get; init; }

    public List<PluginManifestFile> Files { get; init; } = [];
}

public sealed class PluginManifestFile
{
    public string Source { get; init; } = string.Empty;

    public string Target { get; init; } = string.Empty;

    public string? Kind { get; init; }
}

public sealed class InstalledPluginLock
{
    public Dictionary<string, InstalledPluginEntry> Plugins { get; init; } = [];
}

public sealed class InstalledPluginEntry
{
    public string PluginId { get; init; } = string.Empty;

    public string MarketplaceAlias { get; init; } = string.Empty;

    public string SourceType { get; init; } = "github";

    public string Repository { get; init; } = string.Empty;

    public string Ref { get; init; } = "main";

    public List<string> Files { get; init; } = [];
}

public sealed class PluginMarketplaceIndex
{
    public List<PluginIndexEntry> Plugins { get; init; } = [];
}

public sealed class PluginIndexEntry
{
    public string Id { get; init; } = string.Empty;

    public string? Name { get; init; }

    public string? Description { get; init; }
}

public sealed record PluginMarketplaceAddResult(
    string Alias,
    PluginMarketplaceEntry Entry);

public sealed record PluginMarketplaceRemoveResult(
    string Alias,
    PluginMarketplaceEntry Removed);

public sealed record PluginBrowseResult(
    string Alias,
    PluginMarketplaceEntry Marketplace,
    IReadOnlyList<PluginIndexEntry> Plugins);

public sealed record PluginInstallResult(
    InstalledPluginEntry InstalledPlugin,
    bool UsedManifest);

public sealed record PluginListResult(
    PluginMarketplaceConfig MarketplaceConfig,
    InstalledPluginLock InstalledPlugins);

public sealed record PluginUninstallResult(
    string PluginId,
    IReadOnlyList<string> RemovedFiles);
