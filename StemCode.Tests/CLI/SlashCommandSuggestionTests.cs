using FluentAssertions;
using StemCode.CLI;
using Xunit;

namespace StemCode.Tests.CLI;

public sealed class SlashCommandSuggestionTests
{
    [Fact]
    public void ApplySuggestionAtCursor_Should_ReplaceWholeInputWhenCaretAtEnd()
    {
        (string input, int cursor) = Program.ApplySuggestionAtCursor(
            "!cd ./sr",
            9,
            "!cd ./src/");

        input.Should().Be("!cd ./src/");
        cursor.Should().Be(10);
    }

    [Fact]
    public void ApplySuggestionAtCursor_Should_PreserveTextAfterCaret()
    {
        (string input, int cursor) = Program.ApplySuggestionAtCursor(
            "!cd ./src and more",
            7,
            "!cd ./src/");

        input.Should().Be("!cd ./src/rc and more");
        cursor.Should().Be(10);
    }

    [Fact]
    public void ApplySuggestionAtCursor_Should_PreserveTrailingTextForPlainPath()
    {
        (string input, int cursor) = Program.ApplySuggestionAtCursor(
            "./src and more",
            5,
            "./src/");

        input.Should().Be("./src/ and more");
        cursor.Should().Be(6);
    }

    [Fact]
    public void ApplySuggestionAtCursor_Should_ClampCaretBeyondLength()
    {
        (string input, int cursor) = Program.ApplySuggestionAtCursor(
            "abc",
            99,
            "xyz");

        input.Should().Be("xyz");
        cursor.Should().Be(3);
    }

    [Fact]
    public void ApplySuggestionAtCursor_Should_InsertAtStartWhenCaretIsZero()
    {
        (string input, int cursor) = Program.ApplySuggestionAtCursor(
            "hello world",
            0,
            "xyz");

        input.Should().Be("xyzhello world");
        cursor.Should().Be(3);
    }
}
