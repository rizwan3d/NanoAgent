using FluentAssertions;
using NanoAgent.CLI;

namespace NanoAgent.Tests.CLI;

public sealed class CollapsedInputPasteTests
{
    [Fact]
    public void Should_Set_Properties()
    {
        var paste = new CollapsedInputPaste(startIndex: 5, length: 10, lineCount: 3);

        paste.StartIndex.Should().Be(5);
        paste.Length.Should().Be(10);
        paste.LineCount.Should().Be(3);
    }

    [Fact]
    public void EndIndex_Should_Be_StartIndex_Plus_Length()
    {
        var paste = new CollapsedInputPaste(startIndex: 5, length: 10, lineCount: 3);

        paste.EndIndex.Should().Be(15);
    }

    [Fact]
    public void Should_Allow_Updating_Properties()
    {
        var paste = new CollapsedInputPaste(startIndex: 0, length: 0, lineCount: 0);

        paste.StartIndex = 10;
        paste.Length = 20;
        paste.LineCount = 5;

        paste.StartIndex.Should().Be(10);
        paste.Length.Should().Be(20);
        paste.LineCount.Should().Be(5);
        paste.EndIndex.Should().Be(30);
    }
}
