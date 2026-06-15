using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using NanoAgent.Infrastructure.Anthropic;
using NanoAgent.Infrastructure.Conversation;
using NanoAgent.Infrastructure.GitHub;
using NanoAgent.Infrastructure.OpenAi;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace NanoAgent.Tests.Infrastructure.Conversation;

public sealed class ConversationProviderAdapterTests
{
    [Fact]
    public async Task OpenAiCompatibleAdapter_Should_ApplyOpenRouterHeaders()
    {
        RecordingHandler handler = new(CreateJsonResponse("resp_openrouter", "Hello."));
        OpenAiCompatibleConversationProviderAdapter sut = new(
            CreateExecutor(handler),
            new ConversationProviderRequestPayloadFactory());

        await sut.SendAsync(
            CreateRequest(ProviderKind.OpenRouter, modelId: "openai/gpt-4o"),
            CancellationToken.None);

        handler.RequestUri.Should().Be(new Uri("https://openrouter.ai/api/v1/chat/completions"));
        handler.RefererHeader.Should().Be("https://github.com/rizwan3d/NanoAgent");
        handler.TitleHeader.Should().Be("NanoAgent");
    }

    [Fact]
    public async Task OpenAiChatGptAccountAdapter_Should_RefreshCredentialsAfterUnauthorized()
    {
        SequenceHandler handler = new(
            CreateResponse(HttpStatusCode.Unauthorized, """{"error":"expired"}"""),
            CreateResponse(HttpStatusCode.OK, "data: {\"id\":\"resp_1\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"Hello.\"}]}]}\n\n"));
        Mock<IOpenAiChatGptAccountCredentialService> credentialService = new(MockBehavior.Strict);
        credentialService
            .SetupSequence(service => service.ResolveAsync("stored", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenAiChatGptAccountResolvedCredential("expired-token", "account-1"))
            .ReturnsAsync(new OpenAiChatGptAccountResolvedCredential("fresh-token", "account-1"));
        OpenAiChatGptAccountConversationProviderAdapter sut = new(
            CreateExecutor(handler),
            new ConversationProviderRequestPayloadFactory(),
            new ConversationProviderResponseNormalizer(),
            credentialService.Object,
            sessionId: "session-1");

        ConversationProviderPayload payload = await sut.SendAsync(
            CreateRequest(ProviderKind.OpenAiChatGptAccount),
            CancellationToken.None);

        handler.AuthorizationHeaders.Should().Equal("Bearer expired-token", "Bearer fresh-token");
        payload.RawContent.Should().Contain("\"Hello.\"");
        credentialService.Verify(service => service.ResolveAsync("stored", false, It.IsAny<CancellationToken>()), Times.Once);
        credentialService.Verify(service => service.ResolveAsync("stored", true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OpenAiChatGptAccountAdapter_Should_StreamAssistantTextDeltas()
    {
        RecordingHandler handler = new("""
            event: response.created
            data: {"type":"response.created","response":{"id":"resp_stream"}}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","response_id":"resp_stream","delta":"Hello "}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","response_id":"resp_stream","delta":"world"}

            event: response.completed
            data: {"type":"response.completed","response":{"id":"resp_stream","error":null,"output":[]}}

            data: [DONE]

            """);
        List<string> chunks = [];
        Mock<IOpenAiChatGptAccountCredentialService> credentialService = new(MockBehavior.Strict);
        credentialService
            .Setup(service => service.ResolveAsync("stored", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenAiChatGptAccountResolvedCredential("token", "account-1"));
        OpenAiChatGptAccountConversationProviderAdapter sut = new(
            CreateExecutor(handler),
            new ConversationProviderRequestPayloadFactory(),
            new ConversationProviderResponseNormalizer(),
            credentialService.Object,
            sessionId: "session-1");

        ConversationProviderPayload payload = await sut.SendAsync(
            CreateRequest(ProviderKind.OpenAiChatGptAccount) with
            {
                OnAssistantMessageChunkAsync = (text, _) =>
                {
                    chunks.Add(text);
                    return Task.CompletedTask;
                }
            },
            CancellationToken.None);

        chunks.Should().Equal("Hello ", "world");
        payload.AssistantMessageWasStreamed.Should().BeTrue();
        payload.RawContent.Should().Contain("\"Hello world\"");
    }

    [Fact]
    public async Task AnthropicClaudeAccountAdapter_Should_NormalizeMessagesResponse()
    {
        RecordingHandler handler = new("""
            {
              "id": "msg_1",
              "content": [
                { "type": "text", "text": "Claude reply." }
              ],
              "stop_reason": "end_turn",
              "usage": { "input_tokens": 10, "output_tokens": 5 }
            }
            """);
        Mock<IAnthropicClaudeAccountCredentialService> credentialService = new(MockBehavior.Strict);
        credentialService
            .Setup(service => service.ResolveAsync("stored", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnthropicClaudeAccountResolvedCredential("claude-token"));
        AnthropicClaudeAccountConversationProviderAdapter sut = new(
            CreateExecutor(handler),
            new ConversationProviderRequestPayloadFactory(),
            new ConversationProviderResponseNormalizer(),
            credentialService.Object);

        ConversationProviderPayload payload = await sut.SendAsync(
            CreateRequest(ProviderKind.AnthropicClaudeAccount, modelId: "claude-sonnet-4-5"),
            CancellationToken.None);

        handler.RequestUri.Should().Be(new Uri("https://api.anthropic.com/v1/messages"));
        payload.RawContent.Should().Contain("\"Claude reply.\"");
        payload.RawContent.Should().Contain("\"finish_reason\":\"stop\"");
    }

    [Fact]
    public async Task GitHubCopilotAdapter_Should_AddVisionHeaderForImageInput()
    {
        RecordingHandler handler = new(CreateJsonResponse("resp_copilot", "Hello."));
        Mock<IGitHubCopilotCredentialService> credentialService = new(MockBehavior.Strict);
        credentialService
            .Setup(service => service.ResolveAsync("stored", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitHubCopilotResolvedCredential("copilot-token", null, new Uri("https://api.githubcopilot.com/")));
        GitHubCopilotConversationProviderAdapter sut = new(
            CreateExecutor(handler),
            new ConversationProviderRequestPayloadFactory(),
            credentialService.Object);

        await sut.SendAsync(
            CreateRequest(
                ProviderKind.GitHubCopilot,
                messages:
                [
                    ConversationRequestMessage.User(
                        "Inspect image.",
                        [
                            new ConversationAttachment(
                                "image.png",
                                "image/png",
                                Convert.ToBase64String([1, 2, 3]),
                                textContent: null)
                        ])
                ]),
            CancellationToken.None);

        handler.RequestUri.Should().Be(new Uri("https://api.githubcopilot.com/chat/completions"));
        handler.CopilotVisionHeader.Should().Be("true");
    }

    [Fact]
    public async Task OllamaCloudAdapter_Should_ConvertChatResponseToChatCompletionShape()
    {
        RecordingHandler handler = new("""
            {
              "created_at": "2025-01-01T00:00:00Z",
              "message": {
                "content": "Ollama reply."
              },
              "prompt_eval_count": 12,
              "eval_count": 4
            }
            """);
        OllamaCloudConversationProviderAdapter sut = new(
            CreateExecutor(handler),
            new ConversationProviderRequestPayloadFactory(),
            new ConversationProviderResponseNormalizer());

        ConversationProviderPayload payload = await sut.SendAsync(
            CreateRequest(ProviderKind.OllamaCloud),
            CancellationToken.None);

        handler.RequestUri.Should().Be(new Uri("https://ollama.com/api/chat"));
        payload.RawContent.Should().Contain("\"Ollama reply.\"");
        payload.RawContent.Should().Contain("\"completion_tokens\":4");
    }

    [Fact]
    public async Task OpenCodeZenAdapter_Should_UseMessagesEndpointForClaudeModels()
    {
        RecordingHandler handler = new("""
            {
              "id": "msg_1",
              "content": [
                { "type": "text", "text": "Zen reply." }
              ],
              "stop_reason": "end_turn"
            }
            """);
        OpenCodeZenConversationProviderAdapter sut = new(
            CreateExecutor(handler),
            new ConversationProviderRequestPayloadFactory(),
            new ConversationProviderResponseNormalizer());

        ConversationProviderPayload payload = await sut.SendAsync(
            CreateRequest(ProviderKind.OpenCodeZen, modelId: "claude-sonnet-4-5"),
            CancellationToken.None);

        handler.RequestUri.Should().Be(new Uri("https://opencode.ai/zen/v1/messages"));
        handler.AnthropicVersionHeader.Should().Be("2023-06-01");
        payload.RawContent.Should().Contain("\"Zen reply.\"");
    }

    [Fact]
    public async Task ExecuteAsync_Should_CapServerProvidedRetryDelay()
    {
        HttpResponseMessage rateLimited = CreateResponse(HttpStatusCode.TooManyRequests, "slow down");
        rateLimited.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(1));
        SequenceHandler handler = new(
            rateLimited,
            CreateResponse(HttpStatusCode.OK, CreateJsonResponse("resp_capped", "Hello.")));

        List<TimeSpan> delays = [];
        ConversationProviderHttpExecutor executor = new(
            new HttpClient(handler),
            NullLogger.Instance,
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            },
            () => 0d);

        ConversationProviderPayload payload = await executor.ExecuteAsync(
            ProviderKind.OpenRouter,
            () => new HttpRequestMessage(HttpMethod.Post, "https://example.test/v1/chat/completions"),
            CancellationToken.None);

        payload.RetryCount.Should().Be(1);
        delays.Should().ContainSingle();
        delays[0].Should().Be(TimeSpan.FromSeconds(5));
    }

    private static ConversationProviderRequest CreateRequest(
        ProviderKind providerKind,
        string apiKey = "stored",
        string modelId = "gpt-4.1",
        IReadOnlyList<ConversationRequestMessage>? messages = null)
    {
        return new ConversationProviderRequest(
            new AgentProviderProfile(providerKind, providerKind switch
            {
                ProviderKind.OpenRouter => null,
                ProviderKind.OpenAiChatGptAccount => null,
                ProviderKind.AnthropicClaudeAccount => null,
                ProviderKind.GitHubCopilot => null,
                ProviderKind.OllamaCloud => "https://ollama.com/",
                ProviderKind.OpenCodeZen => "https://opencode.ai/api/",
                _ => "http://127.0.0.1:1234/v1"
            }),
            apiKey,
            modelId,
            messages ?? [ConversationRequestMessage.User("Say hello.")],
            "You are helpful.",
            []);
    }

    private static IConversationProviderHttpExecutor CreateExecutor(HttpMessageHandler handler)
    {
        return new ConversationProviderHttpExecutor(
            new HttpClient(handler),
            NullLogger.Instance,
            (_, _) => Task.CompletedTask,
            () => 0d);
    }

    private static string CreateJsonResponse(string id, string content)
    {
        return $$"""
            {
              "id": "{{id}}",
              "choices": [
                {
                  "message": {
                    "content": "{{content}}"
                  }
                }
              ]
            }
            """;
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

        public RecordingHandler(string responseBody)
        {
            _responseBody = responseBody;
        }

        public string? AnthropicVersionHeader { get; private set; }
        public string? CopilotVisionHeader { get; private set; }
        public string? RefererHeader { get; private set; }
        public string? RequestBody { get; private set; }
        public HttpMethod? RequestMethod { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? TitleHeader { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestMethod = request.Method;
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            RefererHeader = request.Headers.TryGetValues("HTTP-Referer", out IEnumerable<string>? refererValues)
                ? refererValues.Single()
                : null;
            TitleHeader = request.Headers.TryGetValues("X-Title", out IEnumerable<string>? titleValues)
                ? titleValues.Single()
                : null;
            CopilotVisionHeader = request.Headers.TryGetValues("Copilot-Vision-Request", out IEnumerable<string>? visionValues)
                ? visionValues.Single()
                : null;
            AnthropicVersionHeader = request.Headers.TryGetValues("anthropic-version", out IEnumerable<string>? anthropicValues)
                ? anthropicValues.Single()
                : null;

            HttpResponseMessage response = CreateResponse(HttpStatusCode.OK, _responseBody);
            response.Headers.Add("x-request-id", "req_789");
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

        public List<string?> AuthorizationHeaders { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            AuthorizationHeaders.Add(request.Headers.Authorization?.ToString());
            HttpResponseMessage response = _responses.Dequeue();
            response.Headers.Add("x-request-id", "req_789");
            return Task.FromResult(response);
        }
    }
}
