using NanoAgent.Application.Models;
using NanoAgent.Presentation.Repl.Commands;
using NanoAgent.Domain.Models;
using FluentAssertions;

namespace NanoAgent.Tests.Application.Repl.Commands;

public sealed class ModelsCommandHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_Should_ListAvailableModelsAndHighlightActiveModel_When_CommandRuns()
    {
        ModelsCommandHandler sut = new();
        ReplSessionContext session = new(
            new AgentProviderProfile(ProviderKind.OpenAi, null),
            "gpt-5-mini",
            ["gpt-5-mini", "gpt-4.1"]);

        ReplCommandResult result = await sut.ExecuteAsync(
            new ReplCommandContext("models", string.Empty, [], "/models", session),
            CancellationToken.None);

        result.Message.Should().Contain("Available models (2):");
        result.Message.Should().Contain("* gpt-5-mini (active)");
        result.Message.Should().Contain("* gpt-4.1");
    }
}
