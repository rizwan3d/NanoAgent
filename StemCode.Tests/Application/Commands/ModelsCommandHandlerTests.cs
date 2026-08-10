using FluentAssertions;
using StemCode.Application.Abstractions;
using StemCode.Application.Commands;
using StemCode.Application.Models;
using StemCode.Domain.Models;

namespace StemCode.Tests.Application.Commands;

public sealed class ModelsCommandHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_Should_OpenInteractiveModelSelection()
    {
        CapturingModelSelectionService modelSelectionService = new(
            ReplCommandResult.Continue("Selected model."));
        ModelsCommandHandler sut = new(modelSelectionService);
        ReplSessionContext session = new(
            new AgentProviderProfile(ProviderKind.OpenAi, null),
            "model-a",
            ["model-a", "model-b"]);
        using CancellationTokenSource cancellation = new();

        ReplCommandResult result = await sut.ExecuteAsync(
            new ReplCommandContext(
                "models",
                string.Empty,
                [],
                "/models",
                session),
            cancellation.Token);

        result.Message.Should().Be("Selected model.");
        modelSelectionService.Session.Should().BeSameAs(session);
        modelSelectionService.CancellationToken.Should().Be(cancellation.Token);
    }

    private sealed class CapturingModelSelectionService : IInteractiveModelSelectionService
    {
        private readonly ReplCommandResult _result;

        public CapturingModelSelectionService(ReplCommandResult result)
        {
            _result = result;
        }

        public CancellationToken CancellationToken { get; private set; }

        public ReplSessionContext? Session { get; private set; }

        public Task<ReplCommandResult> SelectAsync(
            ReplSessionContext session,
            CancellationToken cancellationToken)
        {
            Session = session;
            CancellationToken = cancellationToken;
            return Task.FromResult(_result);
        }
    }
}
