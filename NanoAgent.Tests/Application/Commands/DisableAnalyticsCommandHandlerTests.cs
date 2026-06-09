using FluentAssertions;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Commands;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;

namespace NanoAgent.Tests.Application.Commands;

public sealed class DisableAnalyticsCommandHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_Should_SaveDisabledTelemetryToWorkspaceProfile()
    {
        CapturingWorkspaceSettingsWriter workspaceSettingsWriter = new();
        DisableAnalyticsCommandHandler sut = new(workspaceSettingsWriter);
        string workspacePath = Path.Combine(
            Path.GetTempPath(),
            "nanoagent-disable-analytics-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);
        ReplSessionContext session = CreateSession(workspacePath);

        try
        {
            ReplCommandResult result = await sut.ExecuteAsync(
                new ReplCommandContext(
                    "disableanalytics",
                    string.Empty,
                    [],
                    "/disableanalytics",
                    session),
                CancellationToken.None);

            result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
            result.Message.Should().Contain(".nanoagent/agent-profile.json");
            workspaceSettingsWriter.WorkspacePath.Should().Be(workspacePath);
            workspaceSettingsWriter.Enabled.Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(workspacePath))
            {
                Directory.Delete(workspacePath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_Should_RejectArguments()
    {
        DisableAnalyticsCommandHandler sut = new(new CapturingWorkspaceSettingsWriter());

        ReplCommandResult result = await sut.ExecuteAsync(
            new ReplCommandContext(
                "disableanalytics",
                "now",
                ["now"],
                "/disableanalytics now",
                CreateSession(Path.GetTempPath())),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Error);
        result.Message.Should().Be("Usage: /disableanalytics");
    }

    private static ReplSessionContext CreateSession(string workspacePath)
    {
        return new ReplSessionContext(
            new AgentProviderProfile(ProviderKind.OpenAi, null),
            "model-a",
            ["model-a"],
            workspacePath: workspacePath);
    }

    private sealed class CapturingWorkspaceSettingsWriter : IWorkspaceSettingsWriter
    {
        public bool? Enabled { get; private set; }

        public string? WorkspacePath { get; private set; }

        public Task SavePermissionSettingsAsync(
            string workspacePath,
            PermissionSettings settings,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task SaveMemorySettingsAsync(
            string workspacePath,
            MemorySettings settings,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task SaveTelemetryEnabledAsync(
            string workspacePath,
            bool enabled,
            CancellationToken cancellationToken)
        {
            WorkspacePath = workspacePath;
            Enabled = enabled;
            return Task.CompletedTask;
        }
    }
}
