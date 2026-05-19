using FluentAssertions;
using NanoAgent.Application.Tools;

namespace NanoAgent.Tests.Application.Tools;

public sealed class ShellCommandTextTests
{
    [Theory]
    [InlineData("dotnet test", false)]
    [InlineData("dotnet test && dotnet pack", true)]
    [InlineData("cat file.txt | rg foo", true)]
    [InlineData("echo hi > out.txt", true)]
    [InlineData("echo $(whoami)", true)]
    [InlineData("echo `whoami`", true)]
    public void ContainsControlSyntax_ShouldDetectExpectedPatterns(string commandText, bool expected)
    {
        bool result = ShellCommandText.ContainsControlSyntax(commandText);

        result.Should().Be(expected);
    }

    [Fact]
    public void ParseSegments_ShouldSplitOnConditionalOperatorsAndNewlines()
    {
        IReadOnlyList<ShellCommandSegment> segments = ShellCommandText.ParseSegments(
            "dotnet test && dotnet pack || echo fail; git status\r\necho done");

        segments.Should().Equal(
            new ShellCommandSegment(ShellCommandSegmentCondition.Always, "dotnet test"),
            new ShellCommandSegment(ShellCommandSegmentCondition.OnSuccess, "dotnet pack"),
            new ShellCommandSegment(ShellCommandSegmentCondition.OnFailure, "echo fail"),
            new ShellCommandSegment(ShellCommandSegmentCondition.Always, "git status"),
            new ShellCommandSegment(ShellCommandSegmentCondition.Always, "echo done"));
    }

    [Fact]
    public void ParseSegments_ShouldIgnoreOperatorsInsideQuotes()
    {
        IReadOnlyList<ShellCommandSegment> segments = ShellCommandText.ParseSegments(
            "echo \"a && b\"; echo 'c || d'");

        segments.Should().Equal(
            new ShellCommandSegment(ShellCommandSegmentCondition.Always, "echo \"a && b\""),
            new ShellCommandSegment(ShellCommandSegmentCondition.Always, "echo 'c || d'"));
    }

    [Fact]
    public void ParseSegments_ShouldThrow_WhenCommandTextIsBlank()
    {
        Action act = () => ShellCommandText.ParseSegments("   ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TryGetCommandName_ShouldReturnNormalizedExecutableName()
    {
        bool success = ShellCommandText.TryGetCommandName("\"/usr/local/bin/dotnet.exe\" test", out string name);

        success.Should().BeTrue();
        name.Should().Be("dotnet");
    }

    [Fact]
    public void TryGetCommandName_ShouldReturnFalse_WhenNoMeaningfulCommandExists()
    {
        bool success = ShellCommandText.TryGetCommandName("   ", out string name);

        success.Should().BeFalse();
        name.Should().BeEmpty();
    }

    [Fact]
    public void Tokenize_ShouldRespectQuotedSegments_AndRemoveOuterQuotes()
    {
        string[] tokens = ShellCommandText.Tokenize("git commit -m \"hello world\" 'file name.txt'");

        tokens.Should().Equal("git", "commit", "-m", "hello world", "file name.txt");
    }

    [Theory]
    [InlineData(@"C:/tools/dotnet.exe", "dotnet")]
    [InlineData("./scripts/run.sh", "run")]
    [InlineData("###", "")]
    [InlineData("  ", "")]
    public void NormalizeCommandToken_ShouldReturnExpectedValue(string token, string expected)
    {
        string result = ShellCommandText.NormalizeCommandToken(token);

        result.Should().Be(expected);
    }

    [Fact]
    public void NormalizeCommandText_ShouldNormalizeSuspiciousUnicode()
    {
        string normalized = ShellCommandText.NormalizeCommandText("echo one\uFF06\uFF06 echo two");

        normalized.Should().Contain("&&");
    }

    [Fact]
    public void NormalizeCommandText_ShouldThrow_WhenInputIsNull()
    {
        Action act = () => ShellCommandText.NormalizeCommandText(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
