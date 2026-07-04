using Microsoft.Extensions.DependencyInjection;
using NanoAgent.Application.Commands;
using NanoAgent.Application.Models;
using NanoAgent.Tests.Application.Tools;
using System.Reflection;

namespace NanoAgent.Tests.Application.Commands;

public sealed class ReplCommandCatalogTests
{
    [Fact]
    public void AddReplCommands_registers_handlers_for_every_catalog_entry()
    {
        ServiceCollection services = new();

        services.AddReplCommands();

        int registeredHandlerCount = services
            .Where(static descriptor => descriptor.ServiceType == typeof(IReplCommandHandler))
            .Count();
        int catalogHandlerCount = typeof(ReplCommandCatalog)
            .GetField("Registrations", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null) is Array registrations
                ? registrations.Length
                : 0;

        Assert.Equal(catalogHandlerCount, registeredHandlerCount);
        Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(ResumeCommandHandler));

        string[] catalogNames = ReplCommandCatalog.All
            .Select(static metadata => metadata.CommandName)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.Contains("resume", catalogNames, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HelpCommand_uses_catalog_entries()
    {
        HelpCommandHandler handler = new();
        ReplCommandResult result = await handler.ExecuteAsync(
            new ReplCommandContext(
                "help",
                string.Empty,
                [],
                "/help",
                TestSessionFactory.Create()),
            CancellationToken.None);

        Assert.NotNull(result.Message);
        Assert.Contains(
            "/plugin [marketplace add <owner/repo> [--ref <ref>] [--alias <alias>]|marketplace remove <alias>|browse <marketplaceAlias>|install <pluginId>@<marketplaceAlias> [--force]|list|uninstall <pluginId>] - Manage data-only plugin marketplaces and installs.",
            result.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "/index [update|status|rebuild|list] [limit] - Update, rebuild, inspect, or list the local codebase index.",
            result.Message,
            StringComparison.Ordinal);
    }
}
