using FluentAssertions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Services;
using NanoAgent.Domain.Models;

namespace NanoAgent.Tests.Application.Services;

public sealed class ModelActivationServiceTests
{
    [Fact]
    public void Resolve_Should_SwitchModel_When_UniqueTerminalSegmentMatches()
    {
        ModelActivationService sut = new();
        ReplSessionContext session = new(
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "qwen/qwen3-coder-30b",
            ["qwen/qwen3-coder-30b", "openai/gpt-oss-20b"]);

        ModelActivationResult result = sut.Resolve(session, "gpt-oss-20b");

        result.Status.Should().Be(ModelActivationStatus.Switched);
        result.ResolvedModelId.Should().Be("openai/gpt-oss-20b");
        session.ActiveModelId.Should().Be("openai/gpt-oss-20b");
    }

    [Fact]
    public void Resolve_Should_ReturnAmbiguous_When_MultipleTerminalSegmentsMatch()
    {
        ModelActivationService sut = new();
        ReplSessionContext session = new(
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "vendor-a/gpt-oss-20b",
            ["vendor-a/gpt-oss-20b", "vendor-b/gpt-oss-20b"]);

        ModelActivationResult result = sut.Resolve(session, "gpt-oss-20b");

        result.Status.Should().Be(ModelActivationStatus.Ambiguous);
        result.CandidateModelIds.Should().Equal("vendor-a/gpt-oss-20b", "vendor-b/gpt-oss-20b");
    }
}
