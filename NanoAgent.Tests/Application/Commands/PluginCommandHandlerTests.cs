using FluentAssertions;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Commands;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;

namespace NanoAgent.Tests.Application.Commands;

public sealed class PluginCommandHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_Should_ParseInstallSpecAndForceFlag()
    {
        CapturingPluginService pluginService = new();
        PluginCommandHandler sut = new(pluginService);

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext("install ponytail@ponytail --force"),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
        pluginService.LastInstallRequest.Should().NotBeNull();
        pluginService.LastInstallRequest!.PluginId.Should().Be("ponytail");
        pluginService.LastInstallRequest.MarketplaceAlias.Should().Be("ponytail");
        pluginService.LastInstallRequest.Force.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_Should_RemoveMarketplaceByAlias()
    {
        CapturingPluginService pluginService = new();
        PluginCommandHandler sut = new(pluginService);

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext("marketplace remove ponytail"),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
        pluginService.LastRemovedAlias.Should().Be("ponytail");
    }

    [Fact]
    public async Task ExecuteAsync_Should_BrowseMarketplaceByAlias()
    {
        CapturingPluginService pluginService = new();
        PluginCommandHandler sut = new(pluginService);

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext("browse ponytail"),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
        pluginService.LastBrowsedAlias.Should().Be("ponytail");
    }

    private static ReplCommandContext CreateContext(string argumentText)
    {
        ReplSessionContext session = new(
            new AgentProviderProfile(ProviderKind.OpenAi, BaseUrl: null),
            "gpt-4.1",
            ["gpt-4.1"]);
        string[] arguments = argumentText.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new ReplCommandContext(
            "plugin",
            argumentText,
            arguments,
            "/plugin " + argumentText,
            session);
    }

    private sealed class CapturingPluginService : IPluginService
    {
        public InstallRequest? LastInstallRequest { get; private set; }

        public string? LastRemovedAlias { get; private set; }

        public string? LastBrowsedAlias { get; private set; }

        public Task<PluginMarketplaceAddResult> AddMarketplaceAsync(
            string workspacePath,
            string repository,
            string? alias,
            string? reference,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<PluginInstallResult> InstallAsync(
            string workspacePath,
            string pluginId,
            string marketplaceAlias,
            bool force,
            CancellationToken cancellationToken)
        {
            LastInstallRequest = new InstallRequest(workspacePath, pluginId, marketplaceAlias, force);
            return Task.FromResult(new PluginInstallResult(
                new InstalledPluginEntry
                {
                    PluginId = pluginId,
                    MarketplaceAlias = marketplaceAlias,
                    Repository = "DietrichGebert/ponytail",
                    Ref = "main",
                    Files =
                    [
                        ".nanoagent/skills/ponytail/SKILL.md",
                        ".nanoagent/plugins/ponytail/AGENTS.md"
                    ]
                },
                UsedManifest: false));
        }

        public Task<PluginMarketplaceRemoveResult> RemoveMarketplaceAsync(
            string workspacePath,
            string alias,
            CancellationToken cancellationToken)
        {
            LastRemovedAlias = alias;
            return Task.FromResult(new PluginMarketplaceRemoveResult(
                alias,
                new PluginMarketplaceEntry
                {
                    Repository = "DietrichGebert/ponytail",
                    Ref = "main"
                }));
        }

        public Task<PluginBrowseResult> BrowseMarketplaceAsync(
            string workspacePath,
            string marketplaceAlias,
            CancellationToken cancellationToken)
        {
            LastBrowsedAlias = marketplaceAlias;
            return Task.FromResult(new PluginBrowseResult(
                marketplaceAlias,
                new PluginMarketplaceEntry
                {
                    Repository = "DietrichGebert/ponytail",
                    Ref = "main"
                },
                [
                    new PluginIndexEntry { Id = "ponytail", Name = "Ponytail" }
                ]));
        }

        public Task<PluginListResult> ListAsync(
            string workspacePath,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<PluginUninstallResult> UninstallAsync(
            string workspacePath,
            string pluginId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed record InstallRequest(
        string WorkspacePath,
        string PluginId,
        string MarketplaceAlias,
        bool Force);
}
