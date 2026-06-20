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
