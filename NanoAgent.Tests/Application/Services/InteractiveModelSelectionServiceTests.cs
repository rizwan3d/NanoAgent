using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Services;
using NanoAgent.Domain.Models;
using FluentAssertions;
using Moq;

namespace NanoAgent.Tests.Application.Services;

public sealed class InteractiveModelSelectionServiceTests
{
    [Fact]
    public async Task SelectAsync_Should_PromptWithAvailableModelsAndSaveSelection()
    {
        CapturingSelectionPrompt selectionPrompt = new("model-b");
        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        AgentProviderProfile providerProfile = new(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1");
        ReplSessionContext session = new(
            providerProfile,
            "model-a",
            ["model-a", "model-b"],
            reasoningEffort: "on");

        configurationStore
            .Setup(store => store.SaveAsync(
                new AgentConfiguration(providerProfile, "model-b", "on"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        InteractiveModelSelectionService sut = new(
            selectionPrompt,
            new ModelActivationService(),
            configurationStore.Object);

        ReplCommandResult result = await sut.SelectAsync(session, CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
        result.Message.Should().Contain("model-b");
        session.ActiveModelId.Should().Be("model-b");

        SelectionPromptRequest<string> request = selectionPrompt.LastRequest!;
        request.Title.Should().Be("Choose active model");
        request.DefaultIndex.Should().Be(0);
        request.Options.Select(option => option.Value).Should().Equal("model-a", "model-b");
        request.Options[0].Description.Should().Be("Currently active.");
        configurationStore.VerifyAll();
    }

    [Fact]
    public async Task SelectAsync_Should_NotSave_When_SelectedModelIsAlreadyActive()
    {
        CapturingSelectionPrompt selectionPrompt = new("model-a");
        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        ReplSessionContext session = new(
            new AgentProviderProfile(ProviderKind.OpenAi, null),
            "model-a",
            ["model-a", "model-b"]);

        InteractiveModelSelectionService sut = new(
            selectionPrompt,
            new ModelActivationService(),
            configurationStore.Object);

        ReplCommandResult result = await sut.SelectAsync(session, CancellationToken.None);

        result.Message.Should().Contain("Already using 'model-a'");
        session.ActiveModelId.Should().Be("model-a");
        configurationStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SelectAsync_Should_ReturnWarning_When_PromptIsCancelled()
    {
        CancellingSelectionPrompt selectionPrompt = new();
        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        ReplSessionContext session = new(
            new AgentProviderProfile(ProviderKind.OpenAi, null),
            "model-a",
            ["model-a", "model-b"]);

        InteractiveModelSelectionService sut = new(
            selectionPrompt,
            new ModelActivationService(),
            configurationStore.Object);

        ReplCommandResult result = await sut.SelectAsync(session, CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Warning);
        result.Message.Should().Be("Model selection cancelled.");
        session.ActiveModelId.Should().Be("model-a");
        configurationStore.VerifyNoOtherCalls();
    }

    private sealed class CapturingSelectionPrompt : ISelectionPrompt
    {
        private readonly string _selectedModelId;

        public CapturingSelectionPrompt(string selectedModelId)
        {
            _selectedModelId = selectedModelId;
        }

        public SelectionPromptRequest<string>? LastRequest { get; private set; }

        public Task<T> PromptAsync<T>(
            SelectionPromptRequest<T> request,
            CancellationToken cancellationToken)
        {
            LastRequest = request as SelectionPromptRequest<string>
                ?? throw new InvalidOperationException("Unexpected prompt type.");

            return Task.FromResult((T)(object)_selectedModelId);
        }
    }

    private sealed class CancellingSelectionPrompt : ISelectionPrompt
    {
        public Task<T> PromptAsync<T>(
            SelectionPromptRequest<T> request,
            CancellationToken cancellationToken)
        {
            throw new PromptCancelledException();
        }
    }
}
