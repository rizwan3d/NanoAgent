using FluentAssertions;
using NanoAgent.Application.Backend;
using NanoAgent.Application.Models;
using NanoAgent.CLI;
using System.Text.Json;

namespace NanoAgent.Tests.CLI;

public sealed class CliJsonOutputWriterTests
{
    [Fact]
    public void FormatCommand_Should_WriteStructuredCommandResult()
    {
        BackendCommandResult result = new(
            ReplCommandResult.Continue("done"),
            CreateSessionInfo());

        using JsonDocument document = JsonDocument.Parse(CliJsonOutputWriter.FormatCommand(result));
        JsonElement root = document.RootElement;

        root.GetProperty("status").GetString().Should().Be("ok");
        root.GetProperty("type").GetString().Should().Be("command");
        root.GetProperty("message").GetString().Should().Be("done");
        root.GetProperty("session").GetProperty("id").GetString().Should().Be("sess-test");
        root.GetProperty("session").GetProperty("model").GetString().Should().Be("gpt-test");
    }

    [Fact]
    public void FormatTurn_Should_WriteStructuredTurnResult()
    {
        ConversationTurnResult result = new(
            ConversationTurnResultKind.AssistantMessage,
            "hello",
            toolExecutionResult: null,
            new ConversationTurnMetrics(
                TimeSpan.FromMilliseconds(25),
                estimatedOutputTokens: 7,
                estimatedInputTokens: 3,
                cachedInputTokens: 1,
                providerRetryCount: 2,
                toolRoundCount: 4));

        using JsonDocument document = JsonDocument.Parse(CliJsonOutputWriter.FormatTurn(result, CreateSessionInfo()));
        JsonElement root = document.RootElement;

        root.GetProperty("status").GetString().Should().Be("ok");
        root.GetProperty("type").GetString().Should().Be("turn");
        root.GetProperty("response").GetString().Should().Be("hello");
        root.GetProperty("metrics").GetProperty("estimatedTotalTokens").GetInt32().Should().Be(10);
        root.GetProperty("metrics").GetProperty("toolRoundCount").GetInt32().Should().Be(4);
    }

    [Fact]
    public void FormatError_Should_WriteStructuredError()
    {
        using JsonDocument document = JsonDocument.Parse(CliJsonOutputWriter.FormatError("cancelled", "Cancelled."));

        document.RootElement.GetProperty("status").GetString().Should().Be("error");
        document.RootElement.GetProperty("errorCode").GetString().Should().Be("cancelled");
        document.RootElement.GetProperty("message").GetString().Should().Be("Cancelled.");
    }

    private static BackendSessionInfo CreateSessionInfo()
    {
        return new BackendSessionInfo(
            SessionId: "sess-test",
            SectionResumeCommand: "nanoai --session sess-test",
            ProviderName: "OpenAI",
            ModelId: "gpt-test",
            ActiveModelContextWindowTokens: 128000,
            AvailableModelIds: ["gpt-test"],
            ThinkingMode: "off",
            ReasoningEffort: null,
            ShowThinking: false,
            AgentProfileName: "build",
            SectionTitle: "Untitled section",
            IsResumedSection: false,
            ConversationHistory: []);
    }
}
