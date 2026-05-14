using FluentAssertions;
using NanoAgent.Domain.Services;

namespace NanoAgent.Tests.Domain.Services;

public sealed class ModelIdMatcherTests
{
    [Theory]
    [InlineData("org/models/gpt-5-mini", "gpt-5-mini")]
    [InlineData("provider/models/gpt-4.1", "gpt-4.1")]
    [InlineData("huggingface/meta-llama/Llama-2-7b", "Llama-2-7b")]
    public void HasMatchingTerminalSegment_Should_ReturnTrue_When_TerminalSegmentMatches(string modelId, string candidate)
    {
        bool result = ModelIdMatcher.HasMatchingTerminalSegment(modelId, candidate, StringComparison.OrdinalIgnoreCase);

        result.Should().BeTrue();
    }

    [Fact]
    public void HasMatchingTerminalSegment_Should_ReturnFalse_When_TerminalSegmentDiffers()
    {
        bool result = ModelIdMatcher.HasMatchingTerminalSegment("org/models/gpt-5-mini", "gpt-4.1", StringComparison.OrdinalIgnoreCase);

        result.Should().BeFalse();
    }

    [Fact]
    public void HasMatchingTerminalSegment_Should_ReturnFalse_When_NoSlashInModelId()
    {
        bool result = ModelIdMatcher.HasMatchingTerminalSegment("gpt-5-mini", "gpt-5-mini", StringComparison.OrdinalIgnoreCase);

        result.Should().BeFalse();
    }

    [Fact]
    public void HasMatchingTerminalSegment_Should_ReturnFalse_When_SlashAtEnd()
    {
        bool result = ModelIdMatcher.HasMatchingTerminalSegment("org/models/", "models", StringComparison.OrdinalIgnoreCase);

        result.Should().BeFalse();
    }

    [Fact]
    public void HasMatchingTerminalSegment_Should_Throw_When_ModelIdIsEmpty()
    {
        Action act = () => ModelIdMatcher.HasMatchingTerminalSegment("", "gpt-5", StringComparison.Ordinal);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void HasMatchingTerminalSegment_Should_Throw_When_CandidateIsEmpty()
    {
        Action act = () => ModelIdMatcher.HasMatchingTerminalSegment("org/models/gpt-5", "", StringComparison.Ordinal);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(" gpt-5-mini ", "gpt-5-mini")]
    [InlineData("  ", null)]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("\t\n", null)]
    public void NormalizeOrNull_Should_ReturnExpected(string? input, string? expected)
    {
        string? result = ModelIdMatcher.NormalizeOrNull(input);

        result.Should().Be(expected);
    }
}
