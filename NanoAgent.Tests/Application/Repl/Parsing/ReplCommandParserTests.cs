using NanoAgent.Application.Models;
using NanoAgent.Presentation.Repl.Parsing;
using FluentAssertions;

namespace NanoAgent.Tests.Application.Repl.Parsing;

public sealed class ReplCommandParserTests
{
    private readonly ReplCommandParser _sut = new();

    [Fact]
    public void Parse_Should_ReturnCommandNameAndArguments_When_CommandContainsArguments()
    {
        ParsedReplCommand result = _sut.Parse(" /use openai/gpt-oss-20b ");

        result.RawText.Should().Be("/use openai/gpt-oss-20b");
        result.CommandName.Should().Be("use");
        result.ArgumentText.Should().Be("openai/gpt-oss-20b");
        result.Arguments.Should().Equal("openai/gpt-oss-20b");
    }

    [Fact]
    public void Parse_Should_ReturnEmptyCommandName_When_OnlySlashIsProvided()
    {
        ParsedReplCommand result = _sut.Parse("/");

        result.CommandName.Should().BeEmpty();
        result.ArgumentText.Should().BeEmpty();
        result.Arguments.Should().BeEmpty();
    }

    [Fact]
    public void Parse_Should_SplitArgumentsPredictably_When_CommandContainsMultipleTokens()
    {
        ParsedReplCommand result = _sut.Parse("/config show paths");

        result.CommandName.Should().Be("config");
        result.ArgumentText.Should().Be("show paths");
        result.Arguments.Should().Equal("show", "paths");
    }
}
