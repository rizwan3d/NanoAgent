using System.Text.Json;
using System.Net;
using System.Net.Http;
using System.Text;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using NanoAgent.Infrastructure.Conversation;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace NanoAgent.Tests.Infrastructure.Conversation;

public sealed class OpenAiCompatibleConversationProviderClientTests
{
    [Fact]
    public async Task SendAsync_Should_PostChatCompletionsToV1Endpoint_When_CompatibleProviderBaseUrlHasNoPath()
    {
        RecordingHandler handler = new("""
            {
              "id": "resp_1",
              "choices": [
                {
                  "message": {
                    "content": "Hello."
                  }
                }
              ]
            }
            """);
        HttpClient httpClient = new(handler);
        OpenAiCompatibleConversationProviderClient sut = CreateSut(httpClient);

        ConversationProviderPayload payload = await sut.SendAsync(
            new ConversationProviderRequest(
                new AgentProviderProfile(ProviderKind.OpenAiCompatible, "http://127.0.0.1:1234"),
                "test-key",
                "gpt-4.1",
                [
                    ConversationRequestMessage.User("Explain the diff.")
                ],
                "You are helpful.",
                [CreateToolDefinition("file_read")]),
            CancellationToken.None);

        handler.RequestUri.Should().Be(new Uri("http://127.0.0.1:1234/v1/chat/completions"));
        handler.RequestMethod.Should().Be(HttpMethod.Post);
        handler.AuthorizationHeader.Should().Be("Bearer test-key");
        handler.RequestBody.Should().Contain("\"model\":\"gpt-4.1\"");
        handler.RequestBody.Should().Contain("\"role\":\"system\"");
        handler.RequestBody.Should().Contain("\"role\":\"user\"");
        handler.RequestBody.Should().Contain("\"tools\"");
        handler.RequestBody.Should().Contain("\"name\":\"file_read\"");
        payload.ResponseId.Should().Be("req_789");
    }

    [Fact]
    public async Task SendAsync_Should_SerializeAssistantToolCallsAndToolMessages_When_RequestContainsToolHistory()
    {
        RecordingHandler handler = new("""
            {
              "id": "resp_2",
              "choices": [
                {
                  "message": {
                    "content": "Done."
                  }
                }
              ]
            }
            """);
        HttpClient httpClient = new(handler);
        OpenAiCompatibleConversationProviderClient sut = CreateSut(httpClient);

        ConversationProviderPayload payload = await sut.SendAsync(
            new ConversationProviderRequest(
                new AgentProviderProfile(ProviderKind.OpenAiCompatible, "http://127.0.0.1:1234/v1"),
                "test-key",
                "gpt-4.1",
                [
                    ConversationRequestMessage.User("Create the files."),
                    ConversationRequestMessage.AssistantToolCalls([
                        new ConversationToolCall("call_1", "file_write", """{"path":"index.html","content":"...","overwrite":true}""")
                    ]),
                    ConversationRequestMessage.ToolResult("call_1", """{"path":"index.html","written":true}""")
                ],
                "You are helpful.",
                [CreateToolDefinition("file_write")]),
            CancellationToken.None);

        handler.RequestUri.Should().Be(new Uri("http://127.0.0.1:1234/v1/chat/completions"));
        handler.RequestBody.Should().Contain("\"role\":\"assistant\"");
        handler.RequestBody.Should().Contain("\"tool_calls\"");
        handler.RequestBody.Should().Contain("\"id\":\"call_1\"");
        handler.RequestBody.Should().Contain("\"name\":\"file_write\"");
        handler.RequestBody.Should().Contain("\"role\":\"tool\"");
        handler.RequestBody.Should().Contain("\"tool_call_id\":\"call_1\"");
        handler.RequestBody.Should().Contain("\\u0022written\\u0022:true");
        payload.ResponseId.Should().Be("req_789");
    }

    [Fact]
    public async Task SendAsync_Should_PostChatCompletionsToGoogleAiStudioEndpoint_When_GoogleAiStudioProviderIsSelected()
    {
        RecordingHandler handler = new("""
            {
              "id": "resp_4",
              "choices": [
                {
                  "message": {
                    "content": "Hello from Gemini."
                  }
                }
              ]
            }
            """);
        HttpClient httpClient = new(handler);
        OpenAiCompatibleConversationProviderClient sut = CreateSut(httpClient);

        ConversationProviderPayload payload = await sut.SendAsync(
            new ConversationProviderRequest(
                new AgentProviderProfile(ProviderKind.GoogleAiStudio, null),
                "test-key",
                "gemini-2.5-flash",
                [
                    ConversationRequestMessage.User("Say hello.")
                ],
                "You are helpful.",
                []),
            CancellationToken.None);

        handler.RequestUri.Should().Be(new Uri("https://generativelanguage.googleapis.com/v1beta/openai/chat/completions"));
        handler.AuthorizationHeader.Should().Be("Bearer test-key");
        handler.RequestBody.Should().Contain("\"model\":\"gemini-2.5-flash\"");
        payload.ResponseId.Should().Be("req_789");
    }

    [Fact]
    public async Task SendAsync_Should_PostChatCompletionsToAnthropicEndpoint_When_AnthropicProviderIsSelected()
    {
        RecordingHandler handler = new("""
            {
              "id": "resp_5",
              "choices": [
                {
                  "message": {
                    "content": "Hello from Claude."
                  }
                }
              ]
            }
            """, responseIdHeaderName: "request-id");
        HttpClient httpClient = new(handler);
        OpenAiCompatibleConversationProviderClient sut = CreateSut(httpClient);

        ConversationProviderPayload payload = await sut.SendAsync(
            new ConversationProviderRequest(
                new AgentProviderProfile(ProviderKind.Anthropic, null),
                "test-key",
                "claude-sonnet-4-6",
                [
                    ConversationRequestMessage.User("Say hello.")
                ],
                "You are helpful.",
                []),
            CancellationToken.None);

        handler.RequestUri.Should().Be(new Uri("https://api.anthropic.com/v1/chat/completions"));
        handler.AuthorizationHeader.Should().Be("Bearer test-key");
        handler.RequestBody.Should().Contain("\"model\":\"claude-sonnet-4-6\"");
        payload.ProviderKind.Should().Be(ProviderKind.Anthropic);
        payload.ResponseId.Should().Be("req_789");
    }

    [Fact]
    public async Task SendAsync_Should_SerializeProviderReasoningEffort_When_ThinkingIsOn()
    {
        RecordingHandler handler = new("""
            {
              "id": "resp_reasoning",
              "choices": [
                {
                  "message": {
                    "content": "Done."
                  }
                }
              ]
            }
            """);
        HttpClient httpClient = new(handler);
        OpenAiCompatibleConversationProviderClient sut = CreateSut(httpClient);

        await sut.SendAsync(
            new ConversationProviderRequest(
                new AgentProviderProfile(ProviderKind.OpenAi, null),
                "test-key",
                "gpt-5.4",
                [ConversationRequestMessage.User("Think carefully.")],
                "You are helpful.",
                [],
                "on"),
            CancellationToken.None);

        handler.RequestBody.Should().Contain("\"reasoning_effort\":\"high\"");
    }

    [Fact]
    public async Task SendAsync_Should_PreserveStructuredToolFeedbackJson_When_ToolMessagesContainStatusMetadata()
    {
        RecordingHandler handler = new("""
            {
              "id": "resp_3",
              "choices": [
                {
                  "message": {
                    "content": "Adjusted after tool feedback."
                  }
                }
              ]
            }
            """);
        HttpClient httpClient = new(handler);
        OpenAiCompatibleConversationProviderClient sut = CreateSut(httpClient);

        string toolFeedbackJson = """
            {
              "ToolName": "shell_command",
              "Status": "ExecutionError",
              "IsSuccess": false,
              "Message": "The command exited with code 1.",
              "Data": {
                "Code": "exit_code_1",
                "Message": "The command exited with code 1."
              }
            }
            """;

        await sut.SendAsync(
            new ConversationProviderRequest(
                new AgentProviderProfile(ProviderKind.OpenAiCompatible, "http://127.0.0.1:1234/v1"),
                "test-key",
                "gpt-4.1",
                [
                    ConversationRequestMessage.User("Run tests and fix failures."),
                    ConversationRequestMessage.ToolResult("call_2", toolFeedbackJson)
                ],
                "You are helpful.",
                [CreateToolDefinition("shell_command")]),
            CancellationToken.None);

        using JsonDocument requestDocument = JsonDocument.Parse(handler.RequestBody!);
        JsonElement toolMessage = requestDocument.RootElement
            .GetProperty("messages")[2];

        toolMessage.GetProperty("tool_call_id").GetString().Should().Be("call_2");

        using JsonDocument toolContentDocument = JsonDocument.Parse(toolMessage.GetProperty("content").GetString()!);
        JsonElement toolContent = toolContentDocument.RootElement;
        toolContent.GetProperty("ToolName").GetString().Should().Be("shell_command");
        toolContent.GetProperty("Status").GetString().Should().Be("ExecutionError");
        toolContent.GetProperty("IsSuccess").GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task SendAsync_Should_RetryRetryableHttpStatusCodes(int statusCode)
    {
        SequenceHandler handler = new(
            CreateResponse((HttpStatusCode)statusCode, """{ "error": "retry later" }"""),
            CreateResponse(HttpStatusCode.OK, """
                {
                  "id": "resp_retry",
                  "choices": [
                    {
                      "message": {
                        "content": "Recovered."
                      }
                    }
                  ]
                }
                """));
        List<TimeSpan> delays = [];
        HttpClient httpClient = new(handler);
        OpenAiCompatibleConversationProviderClient sut = CreateSut(
            httpClient,
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            },
            () => 0.5d);

        ConversationProviderPayload payload = await sut.SendAsync(
            CreateRequest(),
            CancellationToken.None);

        payload.RawContent.Should().Contain("Recovered.");
        payload.RetryCount.Should().Be(1);
        handler.RequestBodies.Should().HaveCount(2);
        delays.Should().Equal([TimeSpan.FromMilliseconds(125)]);
    }

    [Fact]
    public async Task SendAsync_Should_UseExponentialBackoffWithJitter_When_MultipleRetriesAreNeeded()
    {
        SequenceHandler handler = new(
            CreateResponse(HttpStatusCode.TooManyRequests, """{ "error": "rate limited" }"""),
            CreateResponse(HttpStatusCode.InternalServerError, """{ "error": "temporary" }"""),
            CreateResponse(HttpStatusCode.OK, """
                {
                  "id": "resp_retry",
                  "choices": [
                    {
                      "message": {
                        "content": "Recovered."
                      }
                    }
                  ]
                }
                """));
        List<TimeSpan> delays = [];
        HttpClient httpClient = new(handler);
        OpenAiCompatibleConversationProviderClient sut = CreateSut(
            httpClient,
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            },
            () => 0.5d);

        ConversationProviderPayload payload = await sut.SendAsync(
            CreateRequest(),
            CancellationToken.None);

        payload.RetryCount.Should().Be(2);
        handler.RequestBodies.Should().HaveCount(3);
        delays.Should().Equal([
            TimeSpan.FromMilliseconds(125),
            TimeSpan.FromMilliseconds(250)
        ]);
    }

    [Fact]
    public async Task SendAsync_Should_NotRetryNonRetryableHttpStatusCodes()
    {
        SequenceHandler handler = new(
            CreateResponse(HttpStatusCode.BadRequest, """{ "error": "bad request" }"""),
            CreateResponse(HttpStatusCode.OK, """
                {
                  "id": "resp_not_used",
                  "choices": [
                    {
                      "message": {
                        "content": "Should not be used."
                      }
                    }
                  ]
                }
                """));
        HttpClient httpClient = new(handler);
        OpenAiCompatibleConversationProviderClient sut = CreateSut(
            httpClient,
            (_, _) => Task.CompletedTask,
            () => 0.5d);

        Func<Task> action = async () => await sut.SendAsync(
            CreateRequest(),
            CancellationToken.None);

        await action.Should()
            .ThrowAsync<ConversationProviderException>()
            .WithMessage("*HTTP 400*");
        handler.RequestBodies.Should().ContainSingle();
    }

    private static ToolDefinition CreateToolDefinition(string name)
    {
        using JsonDocument schemaDocument = JsonDocument.Parse(
            """{ "type": "object", "properties": {}, "additionalProperties": false }""");

        return new ToolDefinition(
            name,
            $"Description for {name}",
            schemaDocument.RootElement.Clone());
    }

    private static OpenAiCompatibleConversationProviderClient CreateSut(HttpClient httpClient)
    {
        return CreateSut(httpClient, delayAsync: null, nextJitter: null);
    }

    private static OpenAiCompatibleConversationProviderClient CreateSut(
        HttpClient httpClient,
        Func<TimeSpan, CancellationToken, Task>? delayAsync,
        Func<double>? nextJitter)
    {
        return new OpenAiCompatibleConversationProviderClient(
            httpClient,
            NullLogger<OpenAiCompatibleConversationProviderClient>.Instance,
            delayAsync,
            nextJitter);
    }

    private static ConversationProviderRequest CreateRequest()
    {
        return new ConversationProviderRequest(
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "http://127.0.0.1:1234/v1"),
            "test-key",
            "gpt-4.1",
            [
                ConversationRequestMessage.User("Retry this request.")
            ],
            "You are helpful.",
            [CreateToolDefinition("file_read")]);
    }

    private static HttpResponseMessage CreateResponse(HttpStatusCode statusCode, string body)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _responseBody;
        private readonly string _responseIdHeaderName;

        public RecordingHandler(string responseBody, string responseIdHeaderName = "x-request-id")
        {
            _responseBody = responseBody;
            _responseIdHeaderName = responseIdHeaderName;
        }

        public string? AuthorizationHeader { get; private set; }

        public string? RequestBody { get; private set; }

        public HttpMethod? RequestMethod { get; private set; }

        public Uri? RequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestMethod = request.Method;
            AuthorizationHeader = request.Headers.Authorization?.ToString();
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
            response.Headers.Add(_responseIdHeaderName, "req_789");

            return response;
        }
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public SequenceHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        public List<string?> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBodies.Add(request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken));

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No HTTP response was queued for this request.");
            }

            return _responses.Dequeue();
        }
    }
}
