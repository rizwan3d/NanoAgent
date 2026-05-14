using FluentAssertions;
using NanoAgent.Application.Utilities;

namespace NanoAgent.Tests.Application.Utilities;

public sealed class SuspiciousUnicodeTextTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("hello", "hello")]
    [InlineData("normal text", "normal text")]
    [InlineData("abc123", "abc123")]
    public void RenderVisible_Should_Return_Input_For_NormalText(string? input, string expected)
    {
        string result = SuspiciousUnicodeText.RenderVisible(input);

        result.Should().Be(expected);
    }

    [Fact]
    public void RenderVisible_Should_RenderSuspiciousControlCharacters()
    {
        string result = SuspiciousUnicodeText.RenderVisible("\0");

        result.Should().Be("<U+0000 NULL>");
    }

    [Fact]
    public void RenderVisible_Should_RenderBackspace()
    {
        string result = SuspiciousUnicodeText.RenderVisible("\u0008");

        result.Should().Be("<U+0008 BACKSPACE>");
    }

    [Fact]
    public void RenderVisible_Should_RenderEscape()
    {
        string result = SuspiciousUnicodeText.RenderVisible("\u001B");

        result.Should().Be("<U+001B ESCAPE>");
    }

    [Fact]
    public void RenderVisible_Should_RenderDelete()
    {
        string result = SuspiciousUnicodeText.RenderVisible("\u007F");

        result.Should().Be("<U+007F DELETE>");
    }

    [Fact]
    public void RenderVisible_Should_RenderZeroWidthSpace()
    {
        string result = SuspiciousUnicodeText.RenderVisible("\u200B");

        result.Should().Be("<U+200B ZERO WIDTH SPACE>");
    }

    [Fact]
    public void RenderVisible_Should_Preserve_Tab_Newline_And_CarriageReturn()
    {
        string result = SuspiciousUnicodeText.RenderVisible("\t\n\r");

        result.Should().Be("\t\n\r");
    }

    [Fact]
    public void RenderVisible_Should_Handle_MixedContent()
    {
        string result = SuspiciousUnicodeText.RenderVisible("hello\u200Bworld");

        result.Should().Be("hello<U+200B ZERO WIDTH SPACE>world");
    }

    [Fact]
    public void RenderVisible_Should_RenderVariationSelector()
    {
        // U+FE00 is a variation selector
        string result = SuspiciousUnicodeText.RenderVisible("\uFE00");

        result.Should().Be("<U+FE00 VARIATION SELECTOR>");
    }

    [Fact]
    public void NormalizeForCommand_Should_Throw_When_Null()
    {
        Action act = () => SuspiciousUnicodeText.NormalizeForCommand(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void NormalizeForCommand_Should_Normalize_Input()
    {
        string result = SuspiciousUnicodeText.NormalizeForCommand("héllo");

        // Normalization Form KC normalizes é to e + combining
        result.Should().NotBeNull();
    }

    [Fact]
    public void NormalizeForCommand_Should_Return_Input_For_SimpleText()
    {
        string result = SuspiciousUnicodeText.NormalizeForCommand("hello world");

        result.Should().Be("hello world");
    }
}
