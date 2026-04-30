using FluentAssertions;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Serialization;
using NanoAgent.Domain.Models;
using NanoAgent.Infrastructure.Storage;
using System.Text.Json;

namespace NanoAgent.Tests.Infrastructure.Storage;

public sealed class WorkspaceToolAuditLogServiceTests : IDisposable
{
    private readonly string _workspacePath = Path.Combine(
        Path.GetTempPath(),
        "NanoAgent.ToolAudit",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RecordAsync_Should_NotCreateAuditLog_When_DisabledByDefault()
    {
        WorkspaceToolAuditLogService sut = new(
            new FixedWorkspaceRootProvider(_workspacePath));

        await sut.RecordAsync(
            CreateToolCall("""{ "path": "README.md" }"""),
            CreateInvocationResult("Read file 'README.md'."),
            CreateSession(),
            ConversationExecutionPhase.Execution,
            DateTimeOffset.UtcNow.AddMilliseconds(-5),
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        File.Exists(sut.GetStoragePath()).Should().BeFalse();
    }

    [Fact]
    public async Task RecordAsync_Should_AppendRedactedJsonLine_When_Enabled()
    {
        WorkspaceToolAuditLogService sut = new(
            new FixedWorkspaceRootProvider(_workspacePath),
            new ToolAuditSettings
            {
                Enabled = true,
                MaxArgumentsChars = 200,
                MaxResultChars = 200,
                RedactSecrets = true
            });

        DateTimeOffset startedAt = new(2026, 4, 25, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset completedAt = startedAt.AddMilliseconds(42);
        await sut.RecordAsync(
            CreateToolCall("""{ "path": "README.md", "token": "sk-abcdefghijklmnopqrstuvwxyz123456" }"""),
            CreateInvocationResult("Failed with token=sk-abcdefghijklmnopqrstuvwxyz123456"),
            CreateSession(),
            ConversationExecutionPhase.Execution,
            startedAt,
            completedAt,
            CancellationToken.None);

        string[] lines = await File.ReadAllLinesAsync(sut.GetStoragePath());
        lines.Should().ContainSingle();

        ToolAuditRecord? record = JsonSerializer.Deserialize(
            lines[0],
            ToolAuditLogJsonContext.Default.ToolAuditRecord);

        record.Should().NotBeNull();
        record!.TimestampUtc.Should().Be(completedAt);
        record.ToolCallId.Should().Be("call_1");
        record.ToolName.Should().Be("file_read");
        record.Status.Should().Be("ExecutionError");
        record.DurationMilliseconds.Should().Be(42);
        record.ArgumentsJson.Should().Contain("<redacted>");
        record.ArgumentsJson.Should().NotContain("sk-abcdefghijklmnopqrstuvwxyz");
        record.ResultMessage.Should().Contain("<redacted>");
        record.ResultJson.Should().Contain("<redacted>");
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspacePath))
        {
            Directory.Delete(_workspacePath, recursive: true);
        }
    }

    private static ConversationToolCall CreateToolCall(string argumentsJson)
    {
        return new ConversationToolCall(
            "call_1",
            "file_read",
            argumentsJson);
    }

    private static ToolInvocationResult CreateInvocationResult(string message)
    {
        return new ToolInvocationResult(
            "call_1",
            "file_read",
            ToolResultFactory.ExecutionError(
                "tool_execution_failed",
                message));
    }

    private static ReplSessionContext CreateSession()
    {
        return new ReplSessionContext(
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "gpt-5-mini",
            ["gpt-5-mini"]);
    }

    private sealed class FixedWorkspaceRootProvider : IWorkspaceRootProvider
    {
        private readonly string _workspacePath;

        public FixedWorkspaceRootProvider(string workspacePath)
        {
            _workspacePath = workspacePath;
        }

        public string GetWorkspaceRoot()
        {
            return _workspacePath;
        }
    }
}
