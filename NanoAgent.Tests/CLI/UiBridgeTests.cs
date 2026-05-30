using FluentAssertions;
using NanoAgent.Application.Backend;
using NanoAgent.Application.Models;
using NanoAgent.Application.UI;
using NanoAgent.CLI;

namespace NanoAgent.Tests.CLI;

public sealed class UiBridgeTests
{
    [Fact]
    public async Task RequestSelectionAsync_Should_ClearActiveModal_AndCancelTask_WhenCancellationRequested()
    {
        UiBridge sut = new();
        AppState state = new(sut, new TestBackend());
        using CancellationTokenSource cancellation = new();

        Task<string> task = sut.RequestSelectionAsync(
            new SelectionPromptRequest<string>(
                "Choose provider",
                [new SelectionPromptOption<string>("OpenAI", "openai")]),
            cancellation.Token);

        sut.ApplyPending(state);
        state.ActiveModal.Should().NotBeNull();

        cancellation.Cancel();
        sut.ApplyPending(state);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        state.ActiveModal.Should().BeNull();
    }

    [Fact]
    public async Task RequestTextAsync_Should_ClearActiveModal_AndCancelTask_WhenCancellationRequested()
    {
        UiBridge sut = new();
        AppState state = new(sut, new TestBackend());
        using CancellationTokenSource cancellation = new();

        Task<string> task = sut.RequestTextAsync(
            new TextPromptRequest("API key", "Paste the API key."),
            isSecret: true,
            cancellation.Token);

        sut.ApplyPending(state);
        state.ActiveModal.Should().NotBeNull();

        cancellation.Cancel();
        sut.ApplyPending(state);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        state.ActiveModal.Should().BeNull();
    }

    private sealed class TestBackend : INanoAgentBackend
    {
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public Task<BackendSessionInfo> InitializeAsync(
            IUiBridge uiBridge,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<BackendCommandResult> RunCommandAsync(
            string commandText,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<BackendCommandResult> SelectModelAsync(
            CancellationToken cancellationToken)
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
}
