using FluentAssertions;
using NanoAgent.Application.Backend;
using NanoAgent.Application.Models;
using NanoAgent.Application.UI;
using NanoAgent.CLI;
using System.Text.Json;

namespace NanoAgent.Tests.CLI;

public sealed class AcpServerTests
{
    [Fact]
    public async Task RunAsync_Should_HandleInitializeSessionAndPrompt()
    {
        string cwd = Directory.GetCurrentDirectory();
        FakeBackend backend = new();
        string input = string.Join(
            Environment.NewLine,
            """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":1}}
            """,
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"session/new\",\"params\":{\"cwd\":" +
                JsonSerializer.Serialize(cwd) +
                ",\"mcpServers\":[]}}",
            """
            {"jsonrpc":"2.0","id":3,"method":"session/prompt","params":{"sessionId":"sess-test","prompt":[{"type":"text","text":"Hello"},{"type":"resource","resource":{"uri":"file:///notes.txt","mimeType":"text/plain","text":"context"}}]}}
            """);

        using StringReader reader = new(input);
        using StringWriter output = new();
        using StringWriter error = new();
        AcpServer sut = new(
            reader,
            output,
            error,
            backendArgs: [],
            providerAuthKey: null,
            _ => backend);

        await sut.RunAsync(CancellationToken.None);

        IReadOnlyList<JsonElement> messages = ParseJsonLines(output.ToString());

        JsonElement initialize = FindResponse(messages, 1);
        initialize.GetProperty("result")
            .GetProperty("agentInfo")
            .GetProperty("name")
            .GetString()
            .Should()
            .Be("nanoagent");

        JsonElement sessionNew = FindResponse(messages, 2);
        sessionNew.GetProperty("result")
            .GetProperty("sessionId")
            .GetString()
            .Should()
            .Be("sess-test");

        messages.Should().Contain(message => IsSessionUpdate(message, "plan"));
        messages.Should().Contain(message => IsSessionUpdate(message, "tool_call"));
        messages.Should().Contain(message => IsSessionUpdate(message, "tool_call_update"));
        messages.Should().Contain(message =>
            IsSessionUpdate(message, "agent_message_chunk") &&
            message.GetProperty("params").GetProperty("update").GetProperty("content").GetProperty("text").GetString() == "Done.");

        JsonElement prompt = FindResponse(messages, 3);
        prompt.GetProperty("result")
            .GetProperty("stopReason")
            .GetString()
            .Should()
            .Be("end_turn");

        backend.LastInput.Should().Contain("Hello");
        backend.LastInput.Should().Contain("Resource: file:///notes.txt");
    }

    [Fact]
    public void Parse_Should_Not_ReadRedirectedStdin_WhenAcpModeIsSelected()
    {
        bool readCalled = false;

        CliInvocation invocation = CliInvocation.Parse(
            ["--acp", "--profile", "review"],
            stdinRedirected: true,
            () =>
            {
                readCalled = true;
                return "protocol input";
            });

        invocation.Mode.Should().Be(CliMode.Acp);
        invocation.BackendArgs.Should().Equal("--profile", "review");
        readCalled.Should().BeFalse();
    }

    private static JsonElement FindResponse(IReadOnlyList<JsonElement> messages, int id)
    {
        return messages.Single(message =>
            message.TryGetProperty("id", out JsonElement responseId) &&
            responseId.ValueKind == JsonValueKind.Number &&
            responseId.GetInt32() == id);
    }

    private static bool IsSessionUpdate(JsonElement message, string updateKind)
    {
        return message.TryGetProperty("method", out JsonElement method) &&
            method.GetString() == "session/update" &&
            message.TryGetProperty("params", out JsonElement parameters) &&
            parameters.TryGetProperty("update", out JsonElement update) &&
            update.TryGetProperty("sessionUpdate", out JsonElement sessionUpdate) &&
            sessionUpdate.GetString() == updateKind;
    }

    private static IReadOnlyList<JsonElement> ParseJsonLines(string output)
    {
        List<JsonElement> messages = [];
        foreach (string line in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            using JsonDocument document = JsonDocument.Parse(line);
            messages.Add(document.RootElement.Clone());
        }

        return messages;
    }

    private sealed class FakeBackend : INanoAgentBackend
    {
        public string LastInput { get; private set; } = string.Empty;

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public Task<BackendSessionInfo> InitializeAsync(
            IUiBridge uiBridge,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new BackendSessionInfo(
                "sess-test",
                "nanoai --section sess-test",
                "OpenAI",
                "gpt-test",
                ActiveModelContextWindowTokens: null,
                ["gpt-test"],
                "off",
                "build",
                "Untitled section",
                IsResumedSection: false,
                ConversationHistory: []));
        }

        public Task<BackendCommandResult> RunCommandAsync(
            string commandText,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ConversationTurnResult> RunTurnAsync(
            string input,
            IUiBridge uiBridge,
            CancellationToken cancellationToken)
        {
            return RunTurnAsync(input, [], uiBridge, cancellationToken);
        }

        public Task<ConversationTurnResult> RunTurnAsync(
            string input,
            IReadOnlyList<ConversationAttachment> attachments,
            IUiBridge uiBridge,
            CancellationToken cancellationToken)
        {
            LastInput = input;
            uiBridge.ShowExecutionPlan(new ExecutionPlanProgress(["Inspect context"], 0));

            ConversationToolCall toolCall = new("call-1", "file_read", """{"path":"README.md"}""");
            uiBridge.ShowToolCalls([toolCall]);
            uiBridge.ShowToolResults(new ToolExecutionBatchResult(
                [
                    new ToolInvocationResult(
                        "call-1",
                        "file_read",
                        ToolResult.Success("Read complete.", "{}"))
                ]));

            return Task.FromResult(ConversationTurnResult.AssistantMessage("Done."));
        }

        public Task<BackendCommandResult> SelectModelAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
