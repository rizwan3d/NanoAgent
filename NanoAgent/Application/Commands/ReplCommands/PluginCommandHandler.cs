using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using System.Text;

namespace NanoAgent.Application.Commands;

internal sealed class PluginCommandHandler : IReplCommandHandler
{
    private readonly IPluginService _pluginService;

    public PluginCommandHandler(IPluginService pluginService)
    {
        _pluginService = pluginService;
    }

    public string CommandName => "plugin";

    public string Description => "Manage data-only plugin marketplaces and installs.";

    public string Usage => "/plugin [marketplace add <owner/repo> [--ref <ref>] [--alias <alias>]|marketplace remove <alias>|browse <marketplaceAlias>|install <pluginId>@<marketplaceAlias> [--force]|list|uninstall <pluginId>]";

    public async Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (context.Arguments.Count == 0)
        {
            return ReplCommandResult.Continue(BuildHelpText());
        }

        try
        {
            return context.Arguments[0].Trim().ToLowerInvariant() switch
            {
                "help" or "-h" or "--help" => ReplCommandResult.Continue(BuildHelpText()),
                "marketplace" => await ExecuteMarketplaceAsync(context, cancellationToken),
                "browse" => await ExecuteBrowseAsync(context, cancellationToken),
                "install" => await ExecuteInstallAsync(context, cancellationToken),
                "list" => await ExecuteListAsync(context, cancellationToken),
                "uninstall" or "remove" => await ExecuteUninstallAsync(context, cancellationToken),
                _ => ReplCommandResult.Continue(
                    $"Unknown plugin action '{context.Arguments[0]}'.\n{BuildHelpText()}",
                    ReplFeedbackKind.Error)
            };
        }
        catch (InvalidOperationException exception)
        {
            return ReplCommandResult.Continue(exception.Message, ReplFeedbackKind.Error);
        }
    }

    private async Task<ReplCommandResult> ExecuteMarketplaceAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        if (context.Arguments.Count < 2)
        {
            return ReplCommandResult.Continue(
                "Usage: /plugin marketplace <add <owner>/<repo> [--ref <ref>] [--alias <alias>]|remove <alias>>",
                ReplFeedbackKind.Error);
        }

        return context.Arguments[1].Trim().ToLowerInvariant() switch
        {
            "add" => await ExecuteMarketplaceAddAsync(context, cancellationToken),
            "remove" or "rm" => await ExecuteMarketplaceRemoveAsync(context, cancellationToken),
            _ => ReplCommandResult.Continue(
                "Usage: /plugin marketplace <add <owner>/<repo> [--ref <ref>] [--alias <alias>]|remove <alias>>",
                ReplFeedbackKind.Error)
        };
    }

    private async Task<ReplCommandResult> ExecuteMarketplaceAddAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        if (!TryParseOptions(
                context.Arguments.Skip(2),
                valueOptions: ["--ref", "--alias"],
                flagOptions: [],
                out List<string> positionals,
                out Dictionary<string, string> options,
                out string? error))
        {
            return ReplCommandResult.Continue(error, ReplFeedbackKind.Error);
        }

        if (positionals.Count != 1)
        {
            return ReplCommandResult.Continue(
                "Usage: /plugin marketplace add <owner>/<repo> [--ref <ref>] [--alias <alias>]",
                ReplFeedbackKind.Error);
        }

        PluginMarketplaceAddResult result = await _pluginService.AddMarketplaceAsync(
            context.Session.WorkspacePath,
            positionals[0],
            options.GetValueOrDefault("--alias"),
            options.GetValueOrDefault("--ref"),
            cancellationToken);

        return ReplCommandResult.Continue(
            $"Added plugin marketplace '{result.Alias}' -> {result.Entry.Repository}@{result.Entry.Ref}.",
            ReplFeedbackKind.Info);
    }

    private async Task<ReplCommandResult> ExecuteMarketplaceRemoveAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        if (context.Arguments.Count != 3)
        {
            return ReplCommandResult.Continue(
                "Usage: /plugin marketplace remove <alias>",
                ReplFeedbackKind.Error);
        }

        PluginMarketplaceRemoveResult result = await _pluginService.RemoveMarketplaceAsync(
            context.Session.WorkspacePath,
            context.Arguments[2],
            cancellationToken);

        return ReplCommandResult.Continue(
            $"Removed plugin marketplace '{result.Alias}' -> {result.Removed.Repository}@{result.Removed.Ref}.",
            ReplFeedbackKind.Info);
    }

    private async Task<ReplCommandResult> ExecuteBrowseAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        if (context.Arguments.Count != 2)
        {
            return ReplCommandResult.Continue(
                "Usage: /plugin browse <marketplaceAlias>",
                ReplFeedbackKind.Error);
        }

        PluginBrowseResult result = await _pluginService.BrowseMarketplaceAsync(
            context.Session.WorkspacePath,
            context.Arguments[1],
            cancellationToken);

        StringBuilder builder = new();
        builder.AppendLine(
            $"Plugins available in '{result.Alias}' ({result.Marketplace.Repository}@{result.Marketplace.Ref}):");
        if (result.Plugins.Count == 0)
        {
            builder.AppendLine("- None published.");
        }
        else
        {
            foreach (PluginIndexEntry plugin in result.Plugins
                         .OrderBy(static entry => entry.Id, StringComparer.OrdinalIgnoreCase))
            {
                builder.AppendLine(plugin.Name is null
                    ? $"- {plugin.Id}"
                    : $"- {plugin.Id} ({plugin.Name})");
                if (plugin.Description is not null)
                {
                    builder.AppendLine($"    {plugin.Description}");
                }
            }
        }

        return ReplCommandResult.Continue(builder.ToString().TrimEnd(), ReplFeedbackKind.Info);
    }

    private async Task<ReplCommandResult> ExecuteInstallAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        if (!TryParseOptions(
                context.Arguments.Skip(1),
                valueOptions: [],
                flagOptions: ["--force"],
                out List<string> positionals,
                out Dictionary<string, string> options,
                out string? error))
        {
            return ReplCommandResult.Continue(error, ReplFeedbackKind.Error);
        }

        if (positionals.Count != 1 ||
            !TryParseInstallSpec(positionals[0], out string? pluginId, out string? marketplaceAlias))
        {
            return ReplCommandResult.Continue(
                "Usage: /plugin install <pluginId>@<marketplaceAlias> [--force]",
                ReplFeedbackKind.Error);
        }

        PluginInstallResult result = await _pluginService.InstallAsync(
            context.Session.WorkspacePath,
            pluginId!,
            marketplaceAlias!,
            options.ContainsKey("--force"),
            cancellationToken);

        StringBuilder builder = new();
        builder.AppendLine(
            $"Installed plugin '{result.InstalledPlugin.PluginId}' from '{result.InstalledPlugin.MarketplaceAlias}' ({result.InstalledPlugin.Repository}@{result.InstalledPlugin.Ref}).");
        builder.AppendLine(result.UsedManifest
            ? "Source: manifest"
            : "Source: convention fallback");
        builder.AppendLine("Files:");
        foreach (string file in result.InstalledPlugin.Files)
        {
            builder.AppendLine("- " + file);
        }

        return ReplCommandResult.Continue(builder.ToString().TrimEnd(), ReplFeedbackKind.Info);
    }

    private async Task<ReplCommandResult> ExecuteListAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        if (context.Arguments.Count != 1)
        {
            return ReplCommandResult.Continue("Usage: /plugin list", ReplFeedbackKind.Error);
        }

        PluginListResult result = await _pluginService.ListAsync(
            context.Session.WorkspacePath,
            cancellationToken);

        StringBuilder builder = new();
        builder.AppendLine("Plugin marketplaces:");
        if (result.MarketplaceConfig.Marketplaces.Count == 0)
        {
            builder.AppendLine("- None configured.");
        }
        else
        {
            foreach ((string alias, PluginMarketplaceEntry entry) in result.MarketplaceConfig.Marketplaces
                         .OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                builder.AppendLine($"- {alias}: {entry.Type} {entry.Repository}@{entry.Ref}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Installed plugins:");
        if (result.InstalledPlugins.Plugins.Count == 0)
        {
            builder.AppendLine("- None installed.");
        }
        else
        {
            foreach ((string pluginId, InstalledPluginEntry entry) in result.InstalledPlugins.Plugins
                         .OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                builder.AppendLine($"- {pluginId}: {entry.Repository}@{entry.Ref} via {entry.MarketplaceAlias}");
                foreach (string file in entry.Files.Order(StringComparer.OrdinalIgnoreCase))
                {
                    builder.AppendLine($"  - {file}");
                }
            }
        }

        return ReplCommandResult.Continue(builder.ToString().TrimEnd(), ReplFeedbackKind.Info);
    }

    private async Task<ReplCommandResult> ExecuteUninstallAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        if (context.Arguments.Count != 2)
        {
            return ReplCommandResult.Continue(
                "Usage: /plugin uninstall <pluginId>",
                ReplFeedbackKind.Error);
        }

        PluginUninstallResult result = await _pluginService.UninstallAsync(
            context.Session.WorkspacePath,
            context.Arguments[1],
            cancellationToken);

        StringBuilder builder = new();
        builder.AppendLine($"Uninstalled plugin '{result.PluginId}'.");
        builder.AppendLine("Removed files:");
        if (result.RemovedFiles.Count == 0)
        {
            builder.AppendLine("- None");
        }
        else
        {
            foreach (string file in result.RemovedFiles)
            {
                builder.AppendLine("- " + file);
            }
        }

        return ReplCommandResult.Continue(builder.ToString().TrimEnd(), ReplFeedbackKind.Info);
    }

    private static bool TryParseInstallSpec(
        string value,
        out string? pluginId,
        out string? marketplaceAlias)
    {
        pluginId = null;
        marketplaceAlias = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        int separatorIndex = value.LastIndexOf('@');
        if (separatorIndex <= 0 || separatorIndex >= value.Length - 1)
        {
            return false;
        }

        pluginId = value[..separatorIndex].Trim();
        marketplaceAlias = value[(separatorIndex + 1)..].Trim();
        return pluginId.Length > 0 && marketplaceAlias.Length > 0;
    }

    private static bool TryParseOptions(
        IEnumerable<string> arguments,
        IReadOnlyCollection<string> valueOptions,
        IReadOnlyCollection<string> flagOptions,
        out List<string> positionals,
        out Dictionary<string, string> options,
        out string? error)
    {
        positionals = [];
        options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        error = null;

        string[] items = arguments.ToArray();
        for (int index = 0; index < items.Length; index++)
        {
            string token = items[index];
            if (valueOptions.Contains(token, StringComparer.OrdinalIgnoreCase))
            {
                int valueIndex = index + 1;
                if (valueIndex >= items.Length || string.IsNullOrWhiteSpace(items[valueIndex]))
                {
                    error = $"Option '{token}' requires a value.";
                    return false;
                }

                options[token] = items[valueIndex].Trim();
                index = valueIndex;
                continue;
            }

            if (flagOptions.Contains(token, StringComparer.OrdinalIgnoreCase))
            {
                options[token] = bool.TrueString;
                continue;
            }

            if (token.StartsWith("--", StringComparison.Ordinal))
            {
                error = $"Unknown option '{token}'.";
                return false;
            }

            positionals.Add(token);
        }

        return true;
    }

    private static string BuildHelpText()
    {
        return string.Join(
            Environment.NewLine,
            [
                "Plugin commands:",
                "/plugin marketplace add <owner>/<repo> [--ref <ref>] [--alias <alias>]",
                "/plugin marketplace remove <alias>",
                "/plugin browse <marketplaceAlias>",
                "/plugin install <pluginId>@<marketplaceAlias> [--force]",
                "/plugin list",
                "/plugin uninstall <pluginId>"
            ]);
    }
}
