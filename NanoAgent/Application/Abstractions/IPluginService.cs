using NanoAgent.Application.Models;

namespace NanoAgent.Application.Abstractions;

public interface IPluginService
{
    Task<PluginMarketplaceAddResult> AddMarketplaceAsync(
        string workspacePath,
        string repository,
        string? alias,
        string? reference,
        CancellationToken cancellationToken);

    Task<PluginInstallResult> InstallAsync(
        string workspacePath,
        string pluginId,
        string marketplaceAlias,
        bool force,
        CancellationToken cancellationToken);

    Task<PluginMarketplaceRemoveResult> RemoveMarketplaceAsync(
        string workspacePath,
        string alias,
        CancellationToken cancellationToken);

    Task<PluginBrowseResult> BrowseMarketplaceAsync(
        string workspacePath,
        string marketplaceAlias,
        CancellationToken cancellationToken);

    Task<PluginListResult> ListAsync(
        string workspacePath,
        CancellationToken cancellationToken);

    Task<PluginUninstallResult> UninstallAsync(
        string workspacePath,
        string pluginId,
        CancellationToken cancellationToken);
}
