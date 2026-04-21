using System.Text.Json;
using System.Net;
using System.Net.Http;
using System.Text;
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
        return new OpenAiCompatibleConversationProviderClient(
            httpClient,
            NullLogger<OpenAiCompatibleConversationProviderClient>.Instance);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _responseBody;

        public RecordingHandler(string responseBody)
        {
            _responseBody = responseBody;
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
            response.Headers.Add("x-request-id", "req_789");

            return response;
        }
    }
}
