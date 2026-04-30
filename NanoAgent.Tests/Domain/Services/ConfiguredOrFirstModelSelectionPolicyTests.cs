using FluentAssertions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Domain.Models;
using NanoAgent.Domain.Services;

namespace NanoAgent.Tests.Domain.Services;

public sealed class ConfiguredOrFirstModelSelectionPolicyTests
{
    private readonly ConfiguredOrFirstModelSelectionPolicy _sut = new();

    [Fact]
    public void Select_Should_UseConfiguredDefault_When_ItMatchesAvailableModels()
    {
        ModelSelectionContext context = new(
            [new AvailableModel("gpt-5"), new AvailableModel("gpt-5-mini")],
            "gpt-5-mini");

        ModelSelectionDecision result = _sut.Select(context);

        result.Should().Be(new ModelSelectionDecision(
            "gpt-5-mini",
            ModelSelectionSource.ConfiguredDefault,
            ConfiguredDefaultModelStatus.Matched,
            "gpt-5-mini"));
    }

    [Fact]
    public void Select_Should_UseFirstReturnedModel_When_ConfiguredDefaultIsMissing()
    {
        ModelSelectionContext context = new(
            [new AvailableModel("gpt-4.1"), new AvailableModel("gpt-5-mini")],
            null);

        ModelSelectionDecision result = _sut.Select(context);

        result.Should().Be(new ModelSelectionDecision(
            "gpt-4.1",
            ModelSelectionSource.FirstReturnedModel,
            ConfiguredDefaultModelStatus.NotConfigured,
            null));
    }

    [Fact]
    public void Select_Should_UseFirstReturnedModel_When_ConfiguredDefaultIsNotReturned()
    {
        ModelSelectionContext context = new(
            [new AvailableModel("gpt-4.1"), new AvailableModel("gpt-5-mini")],
            "gpt-5");

        ModelSelectionDecision result = _sut.Select(context);

        result.Should().Be(new ModelSelectionDecision(
            "gpt-4.1",
            ModelSelectionSource.FirstReturnedModel,
            ConfiguredDefaultModelStatus.NotFound,
            "gpt-5"));
    }

    [Fact]
    public void Select_Should_UseConfiguredDefault_When_ModelIdMatchesTerminalSegmentOfAvailableModel()
    {
        ModelSelectionContext context = new(
            [new AvailableModel("openai/gpt-oss-20b"), new AvailableModel("qwen/qwen3-coder-30b")],
            "gpt-oss-20b");

        ModelSelectionDecision result = _sut.Select(context);

        result.Should().Be(new ModelSelectionDecision(
            "openai/gpt-oss-20b",
            ModelSelectionSource.ConfiguredDefault,
            ConfiguredDefaultModelStatus.Matched,
            "gpt-oss-20b"));
    }

    [Fact]
    public void Select_Should_ThrowModelSelectionException_When_NoModelsAreAvailable()
    {
        ModelSelectionContext context = new([], null);

        Action act = () => _sut.Select(context);

        act.Should().Throw<ModelSelectionException>()
            .WithMessage("*provider returned no available models*");
    }
}
