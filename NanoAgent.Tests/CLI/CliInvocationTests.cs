using FluentAssertions;
using NanoAgent.CLI;

namespace NanoAgent.Tests.CLI;

public sealed class CliInvocationTests
{
    [Fact]
    public void Parse_Should_EnableJsonOutputForOneShotPrompt()
    {
        CliInvocation invocation = CliInvocation.Parse(
            ["--json", "--prompt", "Hello"],
            stdinRedirected: false,
            () => throw new InvalidOperationException());

        invocation.Mode.Should().Be(CliMode.SingleTurn);
        invocation.JsonOutput.Should().BeTrue();
        invocation.Prompt.Should().Be("Hello");
    }

    [Fact]
    public void Parse_Should_RejectJsonOutputWithoutOneShotPrompt()
    {
        Action act = () => CliInvocation.Parse(
            ["--json"],
            stdinRedirected: false,
            () => throw new InvalidOperationException());

        act.Should().Throw<ArgumentException>()
            .WithMessage("--json requires a one-shot prompt.");
    }

    [Fact]
    public void Parse_Should_RejectJsonOutputWithAcpMode()
    {
        Action act = () => CliInvocation.Parse(
            ["--acp", "--json"],
            stdinRedirected: false,
            () => throw new InvalidOperationException());

        act.Should().Throw<ArgumentException>()
            .WithMessage("--json cannot be combined with --acp.");
    }
}
