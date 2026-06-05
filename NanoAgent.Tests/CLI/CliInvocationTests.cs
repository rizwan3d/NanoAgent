using FluentAssertions;
using NanoAgent.CLI;

namespace NanoAgent.Tests.CLI;

public sealed class CliInvocationTests
{
    [Fact]
    public void Parse_Should_Default_To_Interactive_Mode_When_NoPromptIsProvided()
    {
        CliInvocation invocation = CliInvocation.Parse(
            [],
            stdinRedirected: false,
            () => throw new InvalidOperationException());

        invocation.Mode.Should().Be(CliMode.Interactive);
        invocation.Prompt.Should().BeNull();
    }

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
    public void Parse_Should_EnableAutoApprovalForOneShotPrompt()
    {
        CliInvocation invocation = CliInvocation.Parse(
            ["--yes", "Hello"],
            stdinRedirected: false,
            () => throw new InvalidOperationException());

        invocation.Mode.Should().Be(CliMode.SingleTurn);
        invocation.AutoApproveAllTools.Should().BeTrue();
    }

    [Fact]
    public void Parse_Should_EnableAutoApprovalForAcpMode()
    {
        CliInvocation invocation = CliInvocation.Parse(
            ["--acp", "-y"],
            stdinRedirected: false,
            () => throw new InvalidOperationException());

        invocation.Mode.Should().Be(CliMode.Acp);
        invocation.AutoApproveAllTools.Should().BeTrue();
    }

    [Fact]
    public void Parse_Should_ReadPromptFromRedirectedStandardInput_ByDefault()
    {
        bool readCalled = false;

        CliInvocation invocation = CliInvocation.Parse(
            [],
            stdinRedirected: true,
            () =>
            {
                readCalled = true;
                return "prompt from stdin";
            });

        invocation.Mode.Should().Be(CliMode.SingleTurn);
        invocation.Prompt.Should().Be("prompt from stdin");
        readCalled.Should().BeTrue();
    }

    [Fact]
    public void Parse_Should_ReadPromptFromExplicitStdinOption()
    {
        bool readCalled = false;

        CliInvocation invocation = CliInvocation.Parse(
            ["--stdin"],
            stdinRedirected: true,
            () =>
            {
                readCalled = true;
                return "prompt from explicit stdin";
            });

        invocation.Mode.Should().Be(CliMode.SingleTurn);
        invocation.Prompt.Should().Be("prompt from explicit stdin");
        readCalled.Should().BeTrue();
    }

    [Fact]
    public void Parse_Should_RespectDoubleDashPromptSeparator()
    {
        CliInvocation invocation = CliInvocation.Parse(
            ["--profile", "review", "--", "--not-an-option", "follow up"],
            stdinRedirected: false,
            () => throw new InvalidOperationException());

        invocation.Mode.Should().Be(CliMode.SingleTurn);
        invocation.Prompt.Should().Be("--not-an-option follow up");
        invocation.BackendArgs.Should().Equal("--profile", "review");
    }

    [Fact]
    public void Parse_Should_ForwardBackendRuntimeOptions()
    {
        CliInvocation invocation = CliInvocation.Parse(
            [
                "--surface", "desktop",
                "--section", "section-1",
                "--session", "section-2",
                "--profile", "review",
                "--thinking", "on"
            ],
            stdinRedirected: false,
            () => throw new InvalidOperationException());

        invocation.Mode.Should().Be(CliMode.Interactive);
        invocation.BackendArgs.Should().Equal(
            "--surface", "desktop",
            "--section", "section-1",
            "--session", "section-2",
            "--profile", "review",
            "--thinking", "on");
        invocation.RuntimeArguments.SectionId.Should().Be("section-2");
        invocation.RuntimeArguments.ProfileName.Should().Be("review");
        invocation.RuntimeArguments.ThinkingMode.Should().Be("on");
        invocation.RuntimeArguments.AppSurface.Should().Be("desktop");
    }

    [Fact]
    public void Parse_Should_ParseProviderAuthKey()
    {
        CliInvocation invocation = CliInvocation.Parse(
            ["--provider-auth-key", "sk-test", "--prompt", "Hello"],
            stdinRedirected: false,
            () => throw new InvalidOperationException());

        invocation.ProviderAuthKey.Should().Be("sk-test");
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

    [Fact]
    public void Parse_Should_RejectUnknownOption()
    {
        Action act = () => CliInvocation.Parse(
            ["--unknown"],
            stdinRedirected: false,
            () => throw new InvalidOperationException());

        act.Should().Throw<ArgumentException>()
            .WithMessage("Unknown option '--unknown'.");
    }

    [Fact]
    public void Parse_Should_RejectBlankRedirectedInput()
    {
        Action act = () => CliInvocation.Parse(
            [],
            stdinRedirected: true,
            () => "   ");

        act.Should().Throw<ArgumentException>()
            .WithMessage("No prompt was provided.");
    }

    [Fact]
    public void Parse_Should_RejectConflictingModes()
    {
        Action act = () => CliInvocation.Parse(
            ["--acp", "--interactive"],
            stdinRedirected: false,
            () => throw new InvalidOperationException());

        act.Should().Throw<ArgumentException>()
            .WithMessage("--acp cannot be combined with --interactive.");
    }

    [Fact]
    public void Parse_Should_RejectInteractiveModeWhenJsonIsRequested()
    {
        Action act = () => CliInvocation.Parse(
            ["--interactive", "--json"],
            stdinRedirected: false,
            () => throw new InvalidOperationException());

        act.Should().Throw<ArgumentException>()
            .WithMessage("--json requires a one-shot prompt.");
    }

    [Fact]
    public void Parse_Should_RejectMissingBackendRuntimeOptionValue()
    {
        Action act = () => CliInvocation.Parse(
            ["--profile"],
            stdinRedirected: false,
            () => throw new InvalidOperationException());

        act.Should().Throw<ArgumentException>()
            .WithMessage("Missing value for --profile.");
    }
}
