using FluentAssertions;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using NanoAgent.Infrastructure.Storage;
using System.Text.Json;

namespace NanoAgent.Tests.Infrastructure.Storage;

public sealed class JsonSessionEventLogServiceTests
{
    [Fact]
    public async Task RecordMethods_Should_AppendJsonlEventsImmediately()
    {
        string rootPath = Path.Combine(
            Path.GetTempPath(),
            "nanoagent-session-events-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);

        try
        {
            JsonSessionEventLogService sut = new(
                new TestUserDataPathProvider(rootPath));
            ReplSessionContext session = new(
                new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
                "gpt-5-mini",
                ["gpt-5-mini"],
                workspacePath: rootPath);

            await sut.RecordUserInputAsync(session, "inspect repo", CancellationToken.None);
            await sut.RecordToolCallRequestedAsync(
                session,
                new ConversationToolCall("call_1", "shell_command", "{\"command\":\"pwd\"}"),
                CancellationToken.None);
            await sut.RecordAssistantOutputAsync(session, "Done.", CancellationToken.None);

            string storagePath = sut.GetStoragePath(session.SectionId);
            File.Exists(storagePath).Should().BeTrue();

            string[] lines = await File.ReadAllLinesAsync(storagePath);
            lines.Should().HaveCount(3);

            using JsonDocument first = JsonDocument.Parse(lines[0]);
            using JsonDocument second = JsonDocument.Parse(lines[1]);
            using JsonDocument third = JsonDocument.Parse(lines[2]);

            first.RootElement.GetProperty("eventType").GetString().Should().Be("user_input");
            first.RootElement.GetProperty("text").GetString().Should().Be("inspect repo");

            second.RootElement.GetProperty("eventType").GetString().Should().Be("assistant_tool_call_request");
            second.RootElement.GetProperty("toolName").GetString().Should().Be("shell_command");
            second.RootElement.GetProperty("toolArgumentsJson").GetString().Should().Be("{\"command\":\"pwd\"}");

            third.RootElement.GetProperty("eventType").GetString().Should().Be("assistant_output");
            third.RootElement.GetProperty("text").GetString().Should().Be("Done.");
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    private sealed class TestUserDataPathProvider : IUserDataPathProvider
    {
        private readonly string _rootPath;

        public TestUserDataPathProvider(string rootPath)
        {
            _rootPath = rootPath;
        }

        public string GetConfigurationFilePath()
        {
            return Path.Combine(_rootPath, "agent-profile.json");
        }

        public string GetLogsDirectoryPath()
        {
            return Path.Combine(_rootPath, "logs");
        }

        public string GetMcpConfigurationFilePath()
        {
            return GetConfigurationFilePath();
        }

        public string GetSessionsDirectoryPath()
        {
            return Path.Combine(_rootPath, "sessions");
        }
    }
}
