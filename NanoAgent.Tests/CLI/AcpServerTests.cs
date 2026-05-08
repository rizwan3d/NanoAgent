using FluentAssertions;
using NanoAgent.Application.Backend;
using NanoAgent.Application.Models;
using NanoAgent.Application.UI;
using NanoAgent.CLI;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

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

        messages.Should().Contain(message =>
            IsSessionUpdate(message, "session_info_update") &&
            message.GetProperty("params").GetProperty("update").GetProperty("modelId").GetString() == "gpt-test");
        messages.Should().Contain(message =>
            IsSessionUpdate(message, "session_info_update") &&
            message.GetProperty("params")
                .GetProperty("update")
                .GetProperty("availableAgentProfiles")
                .EnumerateArray()
                .Any(profile => profile.GetProperty("name").GetString() == "custom-review"));
        messages.Should().Contain(message => IsSessionUpdate(message, "plan"));
        messages.Should().Contain(message => IsSessionUpdate(message, "tool_call"));
        messages.Should().Contain(message => IsSessionUpdate(message, "tool_call_update"));
        JsonElement toolUpdate = messages.Single(message => IsSessionUpdate(message, "tool_call_update"));
        toolUpdate
            .GetProperty("params")
            .GetProperty("update")
            .GetProperty("content")[0]
            .GetProperty("content")
            .GetProperty("text")
            .GetString()
            .Should()
            .Contain("\u2022 Ran dotnet test");
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
    public async Task RunAsync_Should_ScopeInitializeAndSessionMcpServersToBackend()
    {
        string cwd = Directory.GetCurrentDirectory();
        FakeBackend backend = new();
        IReadOnlyList<BackendMcpServerConfiguration>? capturedMcpServers = null;
        string input = string.Join(
            Environment.NewLine,
            """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":1,"mcpServers":[{"name":"init-server","command":"node","args":["init.js"],"env":[{"name":"INIT_TOKEN","value":"secret"}]}]}}
            """,
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"session/new\",\"params\":{\"cwd\":" +
                JsonSerializer.Serialize(cwd) +
                """
                ,"mcpServers":[{"type":"http","name":"editor-server","url":"http://127.0.0.1:9876/mcp","headers":[{"name":"X-Editor","value":"nano"}]}]}}
                """,
            """
            {"jsonrpc":"2.0","id":3,"method":"session/prompt","params":{"sessionId":"sess-test","prompt":[{"type":"text","text":"/mcp"}]}}
            """);

        using StringReader reader = new(input);
        using StringWriter output = new();
        using StringWriter error = new();
        AcpServer sut = new(
            reader,
            output,
            error,
            backendArgs: ["--profile", "review"],
            providerAuthKey: null,
            (args, mcpServers) =>
            {
                args.Should().Contain("--profile");
                capturedMcpServers = mcpServers;
                return backend;
            });

        await sut.RunAsync(CancellationToken.None);

        capturedMcpServers.Should().NotBeNull();
        capturedMcpServers!.Select(static server => server.Name)
            .Should()
            .Equal("init-server", "editor-server");

        BackendMcpServerConfiguration initServer = capturedMcpServers[0];
        initServer.Command.Should().Be("node");
        initServer.Args.Should().Equal("init.js");
        initServer.Env.Should().Contain("INIT_TOKEN", "secret");
        initServer.Source.Should().Be("ACP initialize");

        BackendMcpServerConfiguration editorServer = capturedMcpServers[1];
        editorServer.Url.Should().Be("http://127.0.0.1:9876/mcp");
        editorServer.Command.Should().BeNull();
        editorServer.IsAssigned(nameof(BackendMcpServerConfiguration.Command)).Should().BeTrue();
        editorServer.HttpHeaders.Should().Contain("X-Editor", "nano");
        editorServer.Source.Should().Be("ACP session");
        backend.LastCommand.Should().Be("/mcp");
        ParseJsonLines(output.ToString()).Should().Contain(message =>
            IsSessionUpdate(message, "agent_message_chunk") &&
            message.GetProperty("params").GetProperty("update").GetProperty("content").GetProperty("text").GetString() == "command:/mcp");
    }

    [Fact]
    public async Task RunAsync_Should_AllowMultipleActiveSessions()
    {
        string cwd = Directory.GetCurrentDirectory();
        List<FakeBackend> backends = [];
        string input = string.Join(
            Environment.NewLine,
            """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":1}}
            """,
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"session/new\",\"params\":{\"cwd\":" +
                JsonSerializer.Serialize(cwd) +
                "}}",
            "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"session/new\",\"params\":{\"cwd\":" +
                JsonSerializer.Serialize(cwd) +
                "}}",
            """
            {"jsonrpc":"2.0","id":4,"method":"session/prompt","params":{"sessionId":"sess-1","prompt":[{"type":"text","text":"First"}]}}
            """,
            """
            {"jsonrpc":"2.0","id":5,"method":"session/prompt","params":{"sessionId":"sess-2","prompt":[{"type":"text","text":"Second"}]}}
            """,
            """
            {"jsonrpc":"2.0","id":6,"method":"session/close","params":{"sessionId":"sess-1"}}
            """,
            """
            {"jsonrpc":"2.0","id":7,"method":"session/prompt","params":{"sessionId":"sess-2","prompt":[{"type":"text","text":"Still active"}]}}
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
            _ =>
            {
                FakeBackend backend = new($"sess-{backends.Count + 1}");
                backends.Add(backend);
                return backend;
            });

        await sut.RunAsync(CancellationToken.None);

        IReadOnlyList<JsonElement> messages = ParseJsonLines(output.ToString());
        FindResponse(messages, 2).GetProperty("result").GetProperty("sessionId").GetString().Should().Be("sess-1");
        FindResponse(messages, 3).GetProperty("result").GetProperty("sessionId").GetString().Should().Be("sess-2");
        FindResponse(messages, 4).GetProperty("result").GetProperty("stopReason").GetString().Should().Be("end_turn");
        FindResponse(messages, 5).GetProperty("result").GetProperty("stopReason").GetString().Should().Be("end_turn");
        FindResponse(messages, 6).GetProperty("result").ValueKind.Should().Be(JsonValueKind.Object);
        FindResponse(messages, 7).GetProperty("result").GetProperty("stopReason").GetString().Should().Be("end_turn");

        backends.Should().HaveCount(2);
        backends[0].Inputs.Should().Equal("First");
        backends[1].Inputs.Should().Equal("Second", "Still active");
        backends[0].DisposeCount.Should().Be(1);
        backends[1].DisposeCount.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_Should_HandleOnboardingPromptsBeforeSessionExists()
    {
        string cwd = Directory.GetCurrentDirectory();
        PromptingBackend backend = new();
        ScriptedAcpTransport transport = new();
        using StringWriter error = new();
        AcpServer sut = new(
            transport.Input,
            transport.Output,
            error,
            backendArgs: [],
            providerAuthKey: null,
            _ => backend);

        Task runTask = sut.RunAsync(CancellationToken.None);

        await transport.SendAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":1}}""");
        await transport.SendAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"session/new\",\"params\":{\"cwd\":" +
            JsonSerializer.Serialize(cwd) +
            "}}");
        await transport.WaitForSessionNewAsync();
        transport.CompleteInput();
        await runTask;

        IReadOnlyList<JsonElement> messages = transport.Messages;

        messages.Should().Contain(message =>
            IsMethod(message, "session/request_permission") &&
            message.GetProperty("params").GetProperty("toolCall").GetProperty("title").GetString() == "Choose provider");
        messages.Should().Contain(message =>
            IsMethod(message, "session/request_text") &&
            message.GetProperty("params").GetProperty("label").GetString() == "API key" &&
            message.GetProperty("params").GetProperty("isSecret").GetBoolean());
        IndexOfMessage(messages, message =>
                IsMethod(message, "session/request_permission") &&
                message.GetProperty("params").GetProperty("toolCall").GetProperty("title").GetString() == "Choose provider")
            .Should()
            .BeLessThan(IndexOfMessage(messages, message =>
                IsMethod(message, "session/request_text") &&
                message.GetProperty("params").GetProperty("label").GetString() == "API key"));

        FindResponse(messages, 2).GetProperty("result").GetProperty("sessionId").GetString().Should().Be("sess-prompt");
        backend.SelectedProvider.Should().Be("OpenAI");
        backend.ApiKey.Should().Be("sk-test");
    }

    [Fact]
    public async Task RunAsync_Should_IncludePermissionDefaultAndTimeoutMetadata()
    {
        string cwd = Directory.GetCurrentDirectory();
        PermissionMetadataBackend backend = new();
        ScriptedAcpTransport transport = new();
        using StringWriter error = new();
        AcpServer sut = new(
            transport.Input,
            transport.Output,
            error,
            backendArgs: [],
            providerAuthKey: null,
            _ => backend);

        Task runTask = sut.RunAsync(CancellationToken.None);

        await transport.SendAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":1}}""");
        await transport.SendAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"session/new\",\"params\":{\"cwd\":" +
            JsonSerializer.Serialize(cwd) +
            "}}");
        await transport.WaitForSessionNewAsync();
        transport.CompleteInput();
        await runTask;

        JsonElement permissionRequest = transport.Messages.Single(message =>
            IsMethod(message, "session/request_permission") &&
            message.GetProperty("params").GetProperty("toolCall").GetProperty("title").GetString() == "Approve shell command?");
        JsonElement parameters = permissionRequest.GetProperty("params");
        parameters.GetProperty("allowCancellation").GetBoolean().Should().BeTrue();
        parameters.GetProperty("defaultOptionId").GetString().Should().Be("0");
        parameters.GetProperty("autoSelectAfterMilliseconds").GetInt32().Should().Be(10_000);

        JsonElement content = parameters
            .GetProperty("toolCall")
            .GetProperty("content")[0];
        content.GetProperty("type").GetString().Should().Be("content");
        content.GetProperty("content").GetProperty("type").GetString().Should().Be("text");
        content.GetProperty("content").GetProperty("text").GetString().Should().Contain("Tool: shell_command");
        backend.SelectedChoice.Should().Be("allow");
    }

    [Fact]
    public async Task RunAsync_Should_HandleOpenAiCompatibleOnboardingPromptSequenceBeforeSessionExists()
    {
        string cwd = Directory.GetCurrentDirectory();
        CompatiblePromptingBackend backend = new();
        ScriptedAcpTransport transport = new();
        using StringWriter error = new();
        AcpServer sut = new(
            transport.Input,
            transport.Output,
            error,
            backendArgs: [],
            providerAuthKey: null,
            _ => backend);

        Task runTask = sut.RunAsync(CancellationToken.None);

        await transport.SendAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":1}}""");
        await transport.SendAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"session/new\",\"params\":{\"cwd\":" +
            JsonSerializer.Serialize(cwd) +
            "}}");
        await transport.WaitForSessionNewAsync();
        transport.CompleteInput();
        await runTask;

        IReadOnlyList<JsonElement> messages = transport.Messages;
        int providerIndex = IndexOfMessage(messages, message =>
            IsMethod(message, "session/request_permission") &&
            message.GetProperty("params").GetProperty("toolCall").GetProperty("title").GetString() == "Choose provider");
        int baseUrlIndex = IndexOfMessage(messages, message =>
            IsMethod(message, "session/request_text") &&
            message.GetProperty("params").GetProperty("label").GetString() == "Base URL");
        int apiKeyIndex = IndexOfMessage(messages, message =>
            IsMethod(message, "session/request_text") &&
            message.GetProperty("params").GetProperty("label").GetString() == "API key");

        providerIndex.Should().BeLessThan(baseUrlIndex);
        baseUrlIndex.Should().BeLessThan(apiKeyIndex);
        backend.SelectedProvider.Should().Be("OpenAI-compatible provider");
        backend.BaseUrl.Should().Be("https://compatible.example.com/v1");
        backend.ApiKey.Should().Be("sk-test");
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
            responseId.GetInt32() == id &&
            !message.TryGetProperty("method", out _) &&
            (message.TryGetProperty("result", out _) || message.TryGetProperty("error", out _)));
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

    private static bool IsMethod(JsonElement message, string methodName)
    {
        return message.TryGetProperty("method", out JsonElement method) &&
            method.GetString() == methodName;
    }

    private static int IndexOfMessage(
        IReadOnlyList<JsonElement> messages,
        Func<JsonElement, bool> predicate)
    {
        for (int index = 0; index < messages.Count; index++)
        {
            if (predicate(messages[index]))
            {
                return index;
            }
        }

        throw new InvalidOperationException("Expected ACP message was not found.");
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
        private readonly string _sessionId;

        public FakeBackend(string sessionId = "sess-test")
        {
            _sessionId = sessionId;
        }

        public int DisposeCount { get; private set; }

        public List<string> Inputs { get; } = [];

        public string LastCommand { get; private set; } = string.Empty;

        public string LastInput { get; private set; } = string.Empty;

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }

        public Task<BackendSessionInfo> InitializeAsync(
            IUiBridge uiBridge,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(CreateSessionInfo());
        }

        public Task<BackendCommandResult> RunCommandAsync(
            string commandText,
            CancellationToken cancellationToken)
        {
            LastCommand = commandText;
            return Task.FromResult(new BackendCommandResult(
                ReplCommandResult.Continue("command:" + commandText),
                CreateSessionInfo()));
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
            Inputs.Add(input);
            uiBridge.ShowExecutionPlan(new ExecutionPlanProgress(["Inspect context"], 0));

            ConversationToolCall toolCall = new("call-1", "shell_command", """{"command":"dotnet test"}""");
            uiBridge.ShowToolCalls([toolCall]);
            uiBridge.ShowToolResults(new ToolExecutionBatchResult(
                [
                    new ToolInvocationResult(
                        "call-1",
                        "shell_command",
                        ToolResult.Success(
                            "Command complete.",
                            """
                            {
                              "Command": "dotnet test",
                              "WorkingDirectory": ".",
                              "ExitCode": 0,
                              "StandardOutput": "Passed!\n",
                              "StandardError": ""
                            }
                            """))
                ]));

            return Task.FromResult(ConversationTurnResult.AssistantMessage("Done."));
        }

        public Task<BackendCommandResult> SelectModelAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        private BackendSessionInfo CreateSessionInfo()
        {
            return new BackendSessionInfo(
                _sessionId,
                $"nanoai --section {_sessionId}",
                "OpenAI",
                "gpt-test",
                ActiveModelContextWindowTokens: null,
                ["gpt-test"],
                "off",
                "build",
                "Untitled section",
                IsResumedSection: false,
                ConversationHistory: [])
            {
                AvailableAgentProfiles =
                [
                    new BackendAgentProfileInfo("build", "primary", "Default build profile"),
                    new BackendAgentProfileInfo("custom-review", "primary", "Workspace custom review profile")
                ]
            };
        }
    }

    private sealed class PromptingBackend : INanoAgentBackend
    {
        public string ApiKey { get; private set; } = string.Empty;

        public string SelectedProvider { get; private set; } = string.Empty;

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public async Task<BackendSessionInfo> InitializeAsync(
            IUiBridge uiBridge,
            CancellationToken cancellationToken)
        {
            SelectedProvider = await uiBridge.RequestSelectionAsync(
                new SelectionPromptRequest<string>(
                    "Choose provider",
                    [new SelectionPromptOption<string>("OpenAI", "OpenAI")],
                    "Pick a provider."),
                cancellationToken);

            ApiKey = await uiBridge.RequestTextAsync(
                new TextPromptRequest(
                    "API key",
                    "Paste the API key."),
                isSecret: true,
                cancellationToken);

            return new BackendSessionInfo(
                "sess-prompt",
                "nanoai --section sess-prompt",
                SelectedProvider,
                "gpt-test",
                ActiveModelContextWindowTokens: null,
                ["gpt-test"],
                "off",
                "build",
                "Prompted session",
                IsResumedSection: false,
                ConversationHistory: []);
        }

        public Task<BackendCommandResult> RunCommandAsync(
            string commandText,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<BackendCommandResult> SelectModelAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ConversationTurnResult> RunTurnAsync(
            string input,
            IUiBridge uiBridge,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ConversationTurnResult> RunTurnAsync(
            string input,
            IReadOnlyList<ConversationAttachment> attachments,
            IUiBridge uiBridge,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class PermissionMetadataBackend : INanoAgentBackend
    {
        public string SelectedChoice { get; private set; } = string.Empty;

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public async Task<BackendSessionInfo> InitializeAsync(
            IUiBridge uiBridge,
            CancellationToken cancellationToken)
        {
            SelectedChoice = await uiBridge.RequestSelectionAsync(
                new SelectionPromptRequest<string>(
                    "Approve shell command?",
                    [
                        new SelectionPromptOption<string>("Allow once", "allow"),
                        new SelectionPromptOption<string>("Deny once", "deny")
                    ],
                    "Tool: shell_command\nCommand: dotnet test",
                    DefaultIndex: 0,
                    AllowCancellation: true,
                    AutoSelectAfter: TimeSpan.FromSeconds(10)),
                cancellationToken);

            return new BackendSessionInfo(
                "sess-permission",
                "nanoai --section sess-permission",
                "OpenAI",
                "gpt-test",
                ActiveModelContextWindowTokens: null,
                ["gpt-test"],
                "off",
                "build",
                "Permission session",
                IsResumedSection: false,
                ConversationHistory: []);
        }

        public Task<BackendCommandResult> RunCommandAsync(
            string commandText,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<BackendCommandResult> SelectModelAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ConversationTurnResult> RunTurnAsync(
            string input,
            IUiBridge uiBridge,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ConversationTurnResult> RunTurnAsync(
            string input,
            IReadOnlyList<ConversationAttachment> attachments,
            IUiBridge uiBridge,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class CompatiblePromptingBackend : INanoAgentBackend
    {
        public string ApiKey { get; private set; } = string.Empty;

        public string BaseUrl { get; private set; } = string.Empty;

        public string SelectedProvider { get; private set; } = string.Empty;

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public async Task<BackendSessionInfo> InitializeAsync(
            IUiBridge uiBridge,
            CancellationToken cancellationToken)
        {
            SelectedProvider = await uiBridge.RequestSelectionAsync(
                new SelectionPromptRequest<string>(
                    "Choose provider",
                    [new SelectionPromptOption<string>("OpenAI-compatible provider", "OpenAI-compatible provider")],
                    "Pick a provider."),
                cancellationToken);

            BaseUrl = await uiBridge.RequestTextAsync(
                new TextPromptRequest(
                    "Base URL",
                    "Enter the OpenAI-compatible base URL."),
                isSecret: false,
                cancellationToken);

            ApiKey = await uiBridge.RequestTextAsync(
                new TextPromptRequest(
                    "API key",
                    "Paste the API key."),
                isSecret: true,
                cancellationToken);

            return new BackendSessionInfo(
                "sess-compatible",
                "nanoai --section sess-compatible",
                SelectedProvider,
                "gpt-test",
                ActiveModelContextWindowTokens: null,
                ["gpt-test"],
                "off",
                "build",
                "Compatible session",
                IsResumedSection: false,
                ConversationHistory: []);
        }

        public Task<BackendCommandResult> RunCommandAsync(
            string commandText,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<BackendCommandResult> SelectModelAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ConversationTurnResult> RunTurnAsync(
            string input,
            IUiBridge uiBridge,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ConversationTurnResult> RunTurnAsync(
            string input,
            IReadOnlyList<ConversationAttachment> attachments,
            IUiBridge uiBridge,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class ScriptedAcpTransport
    {
        private readonly Channel<string?> _input = Channel.CreateUnbounded<string?>();
        private readonly TaskCompletionSource _sessionNewCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<JsonElement> _messages = [];

        public ScriptedAcpTransport()
        {
            Input = new ChannelTextReader(_input.Reader);
            Output = new ScriptedTextWriter(this);
        }

        public TextReader Input { get; }

        public IReadOnlyList<JsonElement> Messages => _messages;

        public TextWriter Output { get; }

        public void CompleteInput()
        {
            _input.Writer.TryComplete();
        }

        public ValueTask SendAsync(string line)
        {
            return _input.Writer.WriteAsync(line);
        }

        public Task WaitForSessionNewAsync()
        {
            return _sessionNewCompletion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        private void HandleOutputLine(string line)
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement message = document.RootElement.Clone();
            _messages.Add(message);

            if (message.TryGetProperty("method", out JsonElement method))
            {
                string? methodName = method.GetString();
                if (methodName == "session/request_permission")
                {
                    Respond(
                        message,
                        """{"outcome":{"outcome":"selected","optionId":"0"}}""");
                }
                else if (methodName == "session/request_text")
                {
                    string label = message
                        .GetProperty("params")
                        .GetProperty("label")
                        .GetString() ?? string.Empty;
                    string value = label == "Base URL"
                        ? "https://compatible.example.com/v1"
                        : "sk-test";
                    Respond(
                        message,
                        """{"outcome":{"outcome":"submitted","value":""" +
                        JsonSerializer.Serialize(value) +
                        "}}");
                }
            }

            if (message.TryGetProperty("id", out JsonElement id) &&
                id.ValueKind == JsonValueKind.Number &&
                id.GetInt32() == 2 &&
                message.TryGetProperty("result", out JsonElement result) &&
                result.TryGetProperty("sessionId", out _))
            {
                _sessionNewCompletion.TrySetResult();
            }
        }

        private void Respond(JsonElement request, string resultJson)
        {
            JsonElement id = request.GetProperty("id");
            string response =
                "{\"jsonrpc\":\"2.0\",\"id\":" +
                id.GetRawText() +
                ",\"result\":" +
                resultJson +
                "}";

            _input.Writer.TryWrite(response);
        }

        private sealed class ChannelTextReader : TextReader
        {
            private readonly ChannelReader<string?> _reader;

            public ChannelTextReader(ChannelReader<string?> reader)
            {
                _reader = reader;
            }

            public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
            {
                try
                {
                    return await _reader.ReadAsync(cancellationToken);
                }
                catch (ChannelClosedException)
                {
                    return null;
                }
            }
        }

        private sealed class ScriptedTextWriter : TextWriter
        {
            private readonly ScriptedAcpTransport _transport;

            public ScriptedTextWriter(ScriptedAcpTransport transport)
            {
                _transport = transport;
            }

            public override Encoding Encoding => Encoding.UTF8;

            public override Task WriteLineAsync(string? value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    _transport.HandleOutputLine(value);
                }

                return Task.CompletedTask;
            }
        }
    }
}
