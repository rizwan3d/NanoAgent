using FluentAssertions;
using StemCode.Application.Models;
using StemCode.Sdk;
using StemCode.Sdk.Events;
using StemCode.Sdk.Internal;

namespace StemCode.Tests.Sdk;

public sealed class HeadlessUiBridgeTests
{
    private static StemCodeClient CreateClient()
    {
        return StemCodeClient.CreateBuilder().UseOllama().Build();
    }

    [Fact]
    public void Show_Methods_Should_RaiseClientEvents()
    {
        StemCodeClient client = CreateClient();
        HeadlessUiBridge bridge = new(client, interactionHandler: null);

        StatusMessageEventArgs? statusArgs = null;
        AssistantMessageChunkEventArgs? messageChunkArgs = null;
        AssistantReasoningEventArgs? reasoningArgs = null;
        client.StatusMessage += (_, e) => statusArgs = e;
        client.AssistantMessageChunkReceived += (_, e) => messageChunkArgs = e;
        client.ReasoningReceived += (_, e) => reasoningArgs = e;

        bridge.ShowSuccess("done");
        bridge.ShowAssistantMessageChunk("hello");
        bridge.ShowAssistantReasoning("thinking");

        statusArgs.Should().NotBeNull();
        statusArgs!.Severity.Should().Be(StatusMessageSeverity.Success);
        statusArgs.Message.Should().Be("done");
        messageChunkArgs.Should().NotBeNull();
        messageChunkArgs!.Text.Should().Be("hello");
        reasoningArgs.Should().NotBeNull();
        reasoningArgs!.ReasoningText.Should().Be("thinking");
    }

    [Fact]
    public async Task RequestText_Should_Throw_When_NoHandlerConfigured()
    {
        StemCodeClient client = CreateClient();
        HeadlessUiBridge bridge = new(client, interactionHandler: null);

        Func<Task<string>> act = () => bridge.RequestTextAsync(
            new TextPromptRequest("API key"),
            isSecret: true,
            CancellationToken.None);

        await act.Should().ThrowAsync<StemCodeInteractionRequiredException>();
    }

    [Fact]
    public async Task RequestText_Should_DelegateToHandler_When_Configured()
    {
        StemCodeClient client = CreateClient();
        FakeInteractionHandler handler = new("answer");
        HeadlessUiBridge bridge = new(client, handler);

        string result = await bridge.RequestTextAsync(
            new TextPromptRequest("Name"),
            isSecret: false,
            CancellationToken.None);

        result.Should().Be("answer");
    }

    private sealed class FakeInteractionHandler : IAgentInteractionHandler
    {
        private readonly string _textAnswer;

        public FakeInteractionHandler(string textAnswer)
        {
            _textAnswer = textAnswer;
        }

        public Task<T> ProvideSelectionAsync<T>(
            SelectionPromptRequest<T> request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(request.Options[0].Value);
        }

        public Task<string> ProvideTextAsync(
            TextPromptRequest request,
            bool isSecret,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_textAnswer);
        }
    }
}
