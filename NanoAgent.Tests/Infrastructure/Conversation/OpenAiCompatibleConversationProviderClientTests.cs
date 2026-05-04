using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using NanoAgent.Infrastructure.Anthropic;
using NanoAgent.Infrastructure.Conversation;
using NanoAgent.Infrastructure.OpenAi;
using System.Net;
using System.Text;
using System.Text.Json;

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
    public async Task SendAsync_Should_PostChatCompletionsToOpenRouterEndpointWithAppHeaders_When_OpenRouterProviderIsSelected()
    {
        RecordingHandler handler = new("""
            {
              "id": "resp_openrouter",
              "choices": [
                {
                  "message": {
                    "content": "Hello from OpenRouter."
                  }
                }
              ]
            }
            """);
        HttpClient httpClient = new(handler);
        OpenAiCompatibleConversationProviderClient sut = CreateSut(httpClient);

        ConversationProviderPayload payload = await sut.SendAsync(
            new ConversationProviderRequest(
                new AgentProviderProfile(ProviderKind.OpenRouter, null),
                "test-key",
                "openai/gpt-4o",
                [
                    ConversationRequestMessage.User("Say hello.")
                ],
                "You are helpful.",
                []),
            CancellationToken.None);

        handler.RequestUri.Should().Be(new Uri("https://openrouter.ai/api/v1/chat/completions"));
        handler.AuthorizationHeader.Should().Be("Bearer test-key");
        handler.OpenRouterRefererHeader.Should().Be("https://github.com/rizwan3d/NanoAgent");
        handler.OpenRouterTitleHeader.Should().Be("NanoAgent");
        handler.RequestBody.Should().Contain("\"model\":\"openai/gpt-4o\"");
        payload.ProviderKind.Should().Be(ProviderKind.OpenRouter);
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
    public async Task SendAsync_Should_PostMessagesRequestWithOAuthHeaders_When_AnthropicClaudeAccountProviderIsSelected()
    {
        RecordingHandler handler = new("""
            {
              "id": "msg_account",
              "type": "message",
              "role": "assistant",
              "content": [
                { "type": "text", "text": "I can help." },
                {
                  "type": "tool_use",
                  "id": "toolu_1",
                  "name": "file_read",
                  "input": {"path":"README.md"}
                }
              ],
              "stop_reason": "tool_use",
              "usage": {
                "input_tokens": 12,
                "output_tokens": 7,
                "cache_read_input_tokens": 3
              }
            }
            """, responseIdHeaderName: "request-id");
        HttpClient httpClient = new(handler);
        Mock<IAnthropicClaudeAccountCredentialService> credentialService = new(MockBehavior.Strict);
        credentialService
            .Setup(service => service.ResolveAsync(
                "stored-credentials",
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnthropicClaudeAccountResolvedCredential("access-token"));
        OpenAiCompatibleConversationProviderClient sut = CreateSut(httpClient, credentialService.Object);

        ConversationProviderPayload payload = await sut.SendAsync(
            new ConversationProviderRequest(
                new AgentProviderProfile(ProviderKind.AnthropicClaudeAccount, null),
                "stored-credentials",
                "claude-sonnet-4-6",
                [
                    ConversationRequestMessage.User("Read the README.")
                ],
                "You are helpful.",
                [CreateToolDefinition("file_read")],
                "on"),
            CancellationToken.None);

        handler.RequestUri.Should().Be(new Uri("https://api.anthropic.com/v1/messages"));
        handler.RequestMethod.Should().Be(HttpMethod.Post);
        handler.AuthorizationHeader.Should().Be("Bearer access-token");
        handler.AnthropicVersionHeader.Should().Be("2023-06-01");
        handler.AnthropicBetaHeader.Should().Contain("oauth-2025-04-20");
        handler.AnthropicAppHeader.Should().Be("cli");
        handler.RequestBody.Should().Contain("\"model\":\"claude-sonnet-4-6\"");
        handler.RequestBody.Should().Contain("You are Claude Code");
        handler.RequestBody.Should().Contain("\"type\":\"enabled\"");
        handler.RequestBody.Should().Contain("\"name\":\"file_read\"");
        payload.ProviderKind.Should().Be(ProviderKind.AnthropicClaudeAccount);

        OpenAiConversationResponseMapper mapper = new();
        ConversationResponse response = mapper.Map(payload);
        response.AssistantMessage.Should().Be("I can help.");
        response.ResponseId.Should().Be("msg_account");
        response.PromptTokens.Should().Be(15);
        response.CompletionTokens.Should().Be(7);
        response.CachedPromptTokens.Should().Be(3);
        response.ToolCalls.Should().ContainSingle()
            .Which.Should().Be(new ConversationToolCall("toolu_1", "file_read", """{"path":"README.md"}"""));
        credentialService.VerifyAll();
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
    public async Task SendAsync_Should_SerializeChatCompletionUserAttachments_AsContentParts()
    {
        RecordingHandler handler = new("""
            {
              "id": "resp_attachments",
              "choices": [
                {
                  "message": {
                    "content": "I can see them."
                  }
                }
              ]
            }
            """);
        HttpClient httpClient = new(handler);
        OpenAiCompatibleConversationProviderClient sut = CreateSut(httpClient);

        await sut.SendAsync(
            new ConversationProviderRequest(
                new AgentProviderProfile(ProviderKind.OpenAiCompatible, "http://127.0.0.1:1234/v1"),
                "test-key",
                "gpt-4.1",
                [
                    ConversationRequestMessage.User(
                        "Review these.",
                        [
                            new ConversationAttachment("screenshot.png", "image/png", "aW1hZ2U="),
                            new ConversationAttachment("notes.txt", "text/plain", "bm90ZXM=", "hello notes")
                        ])
                ],
                "You are helpful.",
                []),
            CancellationToken.None);

        using JsonDocument requestDocument = JsonDocument.Parse(handler.RequestBody!);
        JsonElement content = requestDocument.RootElement
            .GetProperty("messages")[1]
            .GetProperty("content");

        content.ValueKind.Should().Be(JsonValueKind.Array);
        content[0].GetProperty("type").GetString().Should().Be("text");
        content[0].GetProperty("text").GetString().Should().Be("Review these.");
        content[1].GetProperty("type").GetString().Should().Be("image_url");
        content[1].GetProperty("image_url").GetProperty("url").GetString()
            .Should()
            .StartWith("data:image/png;base64,");
        content[2].GetProperty("type").GetString().Should().Be("text");
        content[2].GetProperty("text").GetString().Should().Contain("Attached file: notes.txt");
        content[2].GetProperty("text").GetString().Should().Contain("hello notes");
    }

    [Fact]
    public async Task SendAsync_Should_PostResponsesRequestWithAccountHeaders_When_OpenAiChatGptAccountProviderIsSelected()
    {
        RecordingHandler handler = new("""
            {
              "id": "resp_account",
              "output": [
                {
                  "type": "message",
                  "content": [
                    { "type": "output_text", "text": "Done." }
                  ]
                }
              ]
            }
            """);
        HttpClient httpClient = new(handler);
        Mock<IOpenAiChatGptAccountCredentialService> credentialService = new(MockBehavior.Strict);
        credentialService
            .Setup(service => service.ResolveAsync(
                "stored-credentials",
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenAiChatGptAccountResolvedCredential("access-token", "acct_123"));
        OpenAiCompatibleConversationProviderClient sut = CreateSut(httpClient, credentialService.Object);

        ConversationProviderPayload payload = await sut.SendAsync(
            new ConversationProviderRequest(
                new AgentProviderProfile(ProviderKind.OpenAiChatGptAccount, null),
                "stored-credentials",
                "gpt-5.3-codex",
                [
                    ConversationRequestMessage.User("Read the file."),
                    ConversationRequestMessage.AssistantToolCalls([
                        new ConversationToolCall("call_1", "file_read", """{"path":"README.md"}""")
                    ]),
                    ConversationRequestMessage.ToolResult("call_1", """{"content":"hello"}""")
                ],
                "You are helpful.",
                [CreateToolDefinition("file_read")],
                "on"),
            CancellationToken.None);

        handler.RequestUri.Should().Be(new Uri("https://chatgpt.com/backend-api/codex/responses"));
        handler.RequestMethod.Should().Be(HttpMethod.Post);
        handler.AuthorizationHeader.Should().Be("Bearer access-token");
        handler.AccountIdHeader.Should().Be("acct_123");
        handler.OriginatorHeader.Should().Be("nanoagent");
        handler.SessionIdHeader.Should().NotBeNullOrWhiteSpace();
        payload.ProviderKind.Should().Be(ProviderKind.OpenAiChatGptAccount);

        using JsonDocument requestDocument = JsonDocument.Parse(handler.RequestBody!);
        JsonElement root = requestDocument.RootElement;
        root.GetProperty("model").GetString().Should().Be("gpt-5.3-codex");
        root.GetProperty("stream").GetBoolean().Should().BeTrue();
        root.GetProperty("store").GetBoolean().Should().BeFalse();
        root.GetProperty("instructions").GetString().Should().Be("You are helpful.");
        root.GetProperty("reasoning").GetProperty("effort").GetString().Should().Be("high");
        root.GetProperty("input")[0].GetProperty("content")[0].GetProperty("type").GetString().Should().Be("input_text");
        root.GetProperty("input")[1].GetProperty("type").GetString().Should().Be("function_call");
        root.GetProperty("input")[2].GetProperty("type").GetString().Should().Be("function_call_output");
        root.GetProperty("tools")[0].GetProperty("strict").GetBoolean().Should().BeTrue();
        credentialService.VerifyAll();
    }

    [Fact]
    public async Task SendAsync_Should_SerializeResponsesUserAttachments_AsInputParts()
    {
        RecordingHandler handler = new("""
            {
              "id": "resp_account_attachments",
              "output": [
                {
                  "type": "message",
                  "content": [
                    { "type": "output_text", "text": "Done." }
                  ]
                }
              ]
            }
            """);
        HttpClient httpClient = new(handler);
        Mock<IOpenAiChatGptAccountCredentialService> credentialService = new(MockBehavior.Strict);
        credentialService
            .Setup(service => service.ResolveAsync(
                "stored-credentials",
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenAiChatGptAccountResolvedCredential("access-token", null));
        OpenAiCompatibleConversationProviderClient sut = CreateSut(httpClient, credentialService.Object);

        await sut.SendAsync(
            new ConversationProviderRequest(
                new AgentProviderProfile(ProviderKind.OpenAiChatGptAccount, null),
                "stored-credentials",
                "gpt-5.3-codex",
                [
                    ConversationRequestMessage.User(
                        "Inspect this image.",
                        [new ConversationAttachment("clipboard.png", "image/png", "aW1hZ2U=")])
                ],
                "You are helpful.",
                []),
            CancellationToken.None);

        using JsonDocument requestDocument = JsonDocument.Parse(handler.RequestBody!);
        JsonElement content = requestDocument.RootElement
            .GetProperty("input")[0]
            .GetProperty("content");

        content[0].GetProperty("type").GetString().Should().Be("input_text");
        content[0].GetProperty("text").GetString().Should().Be("Inspect this image.");
        content[1].GetProperty("type").GetString().Should().Be("input_image");
        content[1].GetProperty("image_url").GetString()
            .Should()
            .StartWith("data:image/png;base64,");
        credentialService.VerifyAll();
    }

    [Fact]
    public async Task SendAsync_Should_ReturnResponsesPayload_When_OpenAiChatGptAccountReturnsEventStream()
    {
        RecordingHandler handler = new("""
            event: response.created
            data: {"type":"response.created","response":{"id":"resp_stream"}}

            event: response.output_item.done
            data: {"type":"response.output_item.done","output_index":0,"item":{"type":"message","content":[{"type":"output_text","text":"Done from stream."}]}}

            event: response.completed
            data: {"type":"response.completed","response":{"id":"resp_stream","error":null,"output":[],"usage":{"input_tokens":5,"output_tokens":3,"total_tokens":8}}}

            data: [DONE]

            """);
        HttpClient httpClient = new(handler);
        Mock<IOpenAiChatGptAccountCredentialService> credentialService = new(MockBehavior.Strict);
        credentialService
            .Setup(service => service.ResolveAsync(
                "stored-credentials",
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenAiChatGptAccountResolvedCredential("access-token", null));
        OpenAiCompatibleConversationProviderClient sut = CreateSut(httpClient, credentialService.Object);

        ConversationProviderPayload payload = await sut.SendAsync(
            new ConversationProviderRequest(
                new AgentProviderProfile(ProviderKind.OpenAiChatGptAccount, null),
                "stored-credentials",
                "gpt-5.3-codex",
                [ConversationRequestMessage.User("Say done.")],
                "You are helpful.",
                []),
            CancellationToken.None);

        payload.RawContent.Should().NotContain("data:");

        OpenAiConversationResponseMapper mapper = new();
        ConversationResponse response = mapper.Map(payload);
        response.AssistantMessage.Should().Be("Done from stream.");
        response.ResponseId.Should().Be("resp_stream");
        response.PromptTokens.Should().Be(5);
        response.CompletionTokens.Should().Be(3);
        response.TotalTokens.Should().Be(8);
        credentialService.VerifyAll();
    }

    [Fact]
    public async Task SendAsync_Should_UseTextDeltas_When_OpenAiChatGptAccountCompletedEventHasNoOutputItems()
    {
        RecordingHandler handler = new("""
            event: response.created
            data: {"type":"response.created","response":{"id":"resp_delta"}}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","response_id":"resp_delta","delta":"Hello "}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","response_id":"resp_delta","delta":"from deltas."}

            event: response.completed
            data: {"type":"response.completed","response":{"id":"resp_delta","error":null,"output":[]}}

            data: [DONE]

            """);
        HttpClient httpClient = new(handler);
        Mock<IOpenAiChatGptAccountCredentialService> credentialService = new(MockBehavior.Strict);
        credentialService
            .Setup(service => service.ResolveAsync(
                "stored-credentials",
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenAiChatGptAccountResolvedCredential("access-token", null));
        OpenAiCompatibleConversationProviderClient sut = CreateSut(httpClient, credentialService.Object);

        ConversationProviderPayload payload = await sut.SendAsync(
            new ConversationProviderRequest(
                new AgentProviderProfile(ProviderKind.OpenAiChatGptAccount, null),
                "stored-credentials",
                "gpt-5.3-codex",
                [ConversationRequestMessage.User("Say hello.")],
                "You are helpful.",
                []),
            CancellationToken.None);

        OpenAiConversationResponseMapper mapper = new();
        ConversationResponse response = mapper.Map(payload);
        response.AssistantMessage.Should().Be("Hello from deltas.");
        response.ResponseId.Should().Be("resp_delta");
        credentialService.VerifyAll();
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
        IOpenAiChatGptAccountCredentialService credentialService)
    {
        return new OpenAiCompatibleConversationProviderClient(
            httpClient,
            NullLogger<OpenAiCompatibleConversationProviderClient>.Instance,
            delayAsync: null,
            nextJitter: null,
            openAiChatGptAccountCredentialService: credentialService);
    }

    private static OpenAiCompatibleConversationProviderClient CreateSut(
        HttpClient httpClient,
        IAnthropicClaudeAccountCredentialService credentialService)
    {
        return new OpenAiCompatibleConversationProviderClient(
            httpClient,
            NullLogger<OpenAiCompatibleConversationProviderClient>.Instance,
            delayAsync: null,
            nextJitter: null,
            anthropicClaudeAccountCredentialService: credentialService);
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

        public string? AccountIdHeader { get; private set; }

        public string? OriginatorHeader { get; private set; }

        public string? OpenRouterRefererHeader { get; private set; }

        public string? OpenRouterTitleHeader { get; private set; }

        public string? AnthropicAppHeader { get; private set; }

        public string? AnthropicBetaHeader { get; private set; }

        public string? AnthropicVersionHeader { get; private set; }

        public string? RequestBody { get; private set; }

        public HttpMethod? RequestMethod { get; private set; }

        public Uri? RequestUri { get; private set; }

        public string? SessionIdHeader { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestMethod = request.Method;
            AuthorizationHeader = request.Headers.Authorization?.ToString();
            AccountIdHeader = request.Headers.TryGetValues("ChatGPT-Account-Id", out IEnumerable<string>? accountIdValues)
                ? accountIdValues.FirstOrDefault()
                : null;
            OriginatorHeader = request.Headers.TryGetValues("originator", out IEnumerable<string>? originatorValues)
                ? originatorValues.FirstOrDefault()
                : null;
            OpenRouterRefererHeader = request.Headers.TryGetValues("HTTP-Referer", out IEnumerable<string>? refererValues)
                ? refererValues.FirstOrDefault()
                : null;
            OpenRouterTitleHeader = request.Headers.TryGetValues("X-Title", out IEnumerable<string>? titleValues)
                ? titleValues.FirstOrDefault()
                : null;
            AnthropicVersionHeader = request.Headers.TryGetValues("anthropic-version", out IEnumerable<string>? versionValues)
                ? versionValues.FirstOrDefault()
                : null;
            AnthropicBetaHeader = request.Headers.TryGetValues("anthropic-beta", out IEnumerable<string>? betaValues)
                ? betaValues.FirstOrDefault()
                : null;
            AnthropicAppHeader = request.Headers.TryGetValues("x-app", out IEnumerable<string>? appValues)
                ? appValues.FirstOrDefault()
                : null;
            SessionIdHeader = request.Headers.TryGetValues("session_id", out IEnumerable<string>? sessionIdValues)
                ? sessionIdValues.FirstOrDefault()
                : null;
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
