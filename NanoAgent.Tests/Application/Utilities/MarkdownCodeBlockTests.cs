using FluentAssertions;
using NanoAgent.Application.Utilities;

namespace NanoAgent.Tests.Application.Utilities;

public sealed class MarkdownCodeBlockTests
{
    [Fact]
    public void Wrap_ShouldUseTripleBackticks_WhenContentContainsNoFence()
    {
        string result = MarkdownCodeBlock.Wrap("Console.WriteLine(\"hello\");", "csharp");

        result.Should().Be("```csharp\nConsole.WriteLine(\"hello\");\n```");
    }

    [Fact]
    public void Wrap_ShouldUseLongerFence_WhenContentContainsBacktickRuns()
    {
        string content = "before ``` inner fence ``` after";

        string result = MarkdownCodeBlock.Wrap(content);

        result.Should().StartWith("````\n");
        result.Should().EndWith("\n````");
    }

    [Fact]
    public void Wrap_ShouldTrimInfoString()
    {
        string result = MarkdownCodeBlock.Wrap("x", "  json  ");

        result.Should().Be("```json\nx\n```");
    }

    [Fact]
    public void Wrap_ShouldThrow_WhenContentIsNull()
    {
        Action act = () => MarkdownCodeBlock.Wrap(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
