using FluentAssertions;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using NanoAgent.Domain.Models;
using NanoAgent.Infrastructure.Storage;

namespace NanoAgent.Tests.Infrastructure.Storage;

public sealed class LessonFailureClassifierTests
{
    [Fact]
    public async Task ClassifyAsync_Should_SendFocusedLlmRequestAndParseJsonLesson()
    {
        RecordingConversationProviderClient providerClient = new();
        StaticConversationResponseMapper responseMapper = new(
            new ConversationResponse(
                """
                {
                  "trigger": "invalid_patch from apply_patch",
                  "problem": "The patch used unified diff headers instead of the required apply_patch envelope.",
                  "lesson": "Use *** Begin Patch and *** Update File headers for apply_patch, not ---/+++ unified diff headers.",
                  "tags": ["apply_patch", "patch-format"]
                }
                """,
                [],
                "resp_1"));
        LessonFailureClassifier sut = new(
            new StaticApiKeySecretStore("test-key"),
            providerClient,
            responseMapper,
            new StaticConversationConfigurationAccessor());

        LessonFailureClassification? result = await sut.ClassifyAsync(
            CreateSession(),
            new LessonFailureClassificationRequest(
                AgentToolNames.ApplyPatch,
                "apply_patch invalid_patch",
                "Patch text must begin with *** Begin Patch.",
                "apply_patch with --- a/README.md",
                Command: null,
                FailureSignature: "invalid_patch",
                ["auto", "failure", "apply_patch"]),
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.Lesson.Should().Contain("*** Begin Patch");
        result.Tags.Should().Contain("patch-format");
        providerClient.Requests.Should().ContainSingle();
        ConversationProviderRequest providerRequest = providerClient.Requests[0];
        providerRequest.AvailableTools.Should().BeEmpty();
        providerRequest.SystemPrompt.Should().Contain("Return only a JSON object");
        providerRequest.Messages.Should().ContainSingle();
        providerRequest.Messages[0].Content.Should().Contain("invalid_patch");
    }

    private static ReplSessionContext CreateSession()
    {
        return new ReplSessionContext(
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "gpt-test",
            ["gpt-test"]);
    }

    private sealed class RecordingConversationProviderClient : IConversationProviderClient
    {
        public List<ConversationProviderRequest> Requests { get; } = [];

        public Task<ConversationProviderPayload> SendAsync(
            ConversationProviderRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new ConversationProviderPayload(
                request.ProviderProfile.ProviderKind,
                "{}",
                "resp_1"));
        }
    }

    private sealed class StaticConversationResponseMapper : IConversationResponseMapper
    {
        private readonly ConversationResponse _response;

        public StaticConversationResponseMapper(ConversationResponse response)
        {
            _response = response;
        }

        public ConversationResponse Map(ConversationProviderPayload payload)
        {
            return _response;
        }
    }

    private sealed class StaticApiKeySecretStore : IApiKeySecretStore
    {
        private readonly string? _apiKey;

        public StaticApiKeySecretStore(string? apiKey)
        {
            _apiKey = apiKey;
        }

        public Task<string?> LoadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_apiKey);
        }

        public Task SaveAsync(string apiKey, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class StaticConversationConfigurationAccessor : IConversationConfigurationAccessor
    {
        public ConversationSettings GetSettings()
        {
            return new ConversationSettings(
                SystemPrompt: null,
                TimeSpan.FromSeconds(60),
                MaxHistoryTurns: 12,
                MaxToolRoundsPerTurn: 0);
        }
    }
}
