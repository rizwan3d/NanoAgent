using FluentAssertions;
using NanoAgent.Application.Services;

namespace NanoAgent.Tests.Application.Services;

public sealed class HeuristicTokenEstimatorTests
{
    private readonly HeuristicTokenEstimator _sut = new();

    [Theory]
    [InlineData("hello", 1)]
    [InlineData("hello world", 2)]
    [InlineData("a b c d e", 5)]
    [InlineData("word", 1)]
    [InlineData("a", 1)]
    public void Estimate_Should_ReturnAtLeastOne_For_NonEmptyInput(string text, int expectedMin)
    {
        int result = _sut.Estimate(text);

        result.Should().BeGreaterThanOrEqualTo(expectedMin);
    }

    [Fact]
    public void Estimate_Should_ReturnMaxOfCharacterAndWordBasedEstimates()
    {
        // "a" -> 1 char / 4 = 1, 1 word = 1, max(1,1) = 1
        _sut.Estimate("a").Should().Be(1);

        // "abc" -> 3 chars / 4 = 1 (ceil), 1 word = 1, max = 1
        _sut.Estimate("abc").Should().Be(1);

        // "abcd" -> 4 chars / 4 = 1, 1 word = 1, max = 1
        _sut.Estimate("abcd").Should().Be(1);

        // "abcde" -> 5 chars / 4 = 2 (ceil), 1 word = 1, max = 2
        _sut.Estimate("abcde").Should().Be(2);
    }

    [Fact]
    public void Estimate_Should_HandleMultipleWords()
    {
        int result = _sut.Estimate("one two three four five six");

        // 6 words, 27 chars / 4 = 7 (ceil), max(6, 7) = 7
        result.Should().Be(7);
    }

    [Fact]
    public void Estimate_Should_Throw_When_TextIsEmpty()
    {
        Action act = () => _sut.Estimate("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Estimate_Should_Throw_When_TextIsWhitespace()
    {
        Action act = () => _sut.Estimate("   ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Estimate_Should_Throw_When_TextIsNull()
    {
        Action act = () => _sut.Estimate(null!);

        act.Should().Throw<ArgumentException>();
    }
}
