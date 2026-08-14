using FluentAssertions;
using StemCode.Application.Abstractions;
using StemCode.Application.Commands;
using StemCode.Application.Models;
using StemCode.Domain.Models;

namespace StemCode.Tests.Application.Commands;

public sealed class AutoCommitCommandHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_Should_SaveDisabledAutoCommitToWorkspaceProfile()
    {
        CapturingWorkspaceSettingsWriter workspaceSettingsWriter = new();
        AutoCommitCommandHandler sut = new(workspaceSettingsWriter);
        string workspacePath = Path.Combine(
            Path.GetTempPath(),
            "stemcode-autocommit-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);
        ReplSessionContext session = CreateSession(workspacePath);

        try
        {
            ReplCommandResult result = await sut.ExecuteAsync(
                new ReplCommandContext(
                    "autocommit",
                    "off",
                    ["off"],
                    "/autocommit off",
                    session),
                CancellationToken.None);

            result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
            result.Message.Should().Contain(".stemcode/agent-profile.json");
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
    public async Task ExecuteAsync_Should_RejectUnknownArgument()
    {
        AutoCommitCommandHandler sut = new(new CapturingWorkspaceSettingsWriter());

        ReplCommandResult result = await sut.ExecuteAsync(
            new ReplCommandContext(
                "autocommit",
                "later",
                ["later"],
                "/autocommit later",
                CreateSession(Path.GetTempPath())),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Error);
        result.Message.Should().Be("Usage: /autocommit [on|off|status]");
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
            return Task.CompletedTask;
        }

        public Task SaveAutoCommitEnabledAsync(
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
