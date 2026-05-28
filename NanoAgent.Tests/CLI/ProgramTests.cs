using FluentAssertions;
using NanoAgent.CLI;
using NanoAgent.Infrastructure.WindowsSandbox;

namespace NanoAgent.Tests.CLI;

public sealed class ProgramTests
{
    [Fact]
    public void TryHandleWindowsSandboxSpecialInvocation_Should_ReturnFalse_ForRegularCliArgs()
    {
        bool handled = Program.TryHandleWindowsSandboxSpecialInvocation(
            ["--interactive"],
            out int exitCode);

        handled.Should().BeFalse();
        exitCode.Should().Be(0);
    }

    [Fact]
    public void TryHandleWindowsSandboxSpecialInvocation_Should_ReturnUsageError_WhenSetupPayloadIsMissing()
    {
        bool handled = Program.TryHandleWindowsSandboxSpecialInvocation(
            [WindowsSandboxSetupOrchestrator.SetupCommandArgument],
            out int exitCode);

        handled.Should().BeTrue();
        exitCode.Should().Be(2);
    }
}
