using NanoAgent.Infrastructure.Secrets;
using FluentAssertions;

namespace NanoAgent.Tests.Infrastructure.Secrets;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_Should_CapCapturedStandardOutputAndError()
    {
        ProcessExecutionRequest request = OperatingSystem.IsWindows()
            ? new ProcessExecutionRequest(
                "powershell",
                [
                    "-NoProfile",
                    "-NonInteractive",
                    "-Command",
                    "[Console]::Out.Write(('o' * 20000)); [Console]::Error.Write(('e' * 20000))"
                ],
                MaxOutputCharacters: 128)
            : new ProcessExecutionRequest(
                "/bin/sh",
                [
                    "-c",
                    "printf '%*s' 20000 '' | tr ' ' o; printf '%*s' 20000 '' | tr ' ' e >&2"
                ],
                MaxOutputCharacters: 128);

        ProcessExecutionResult result = await new ProcessRunner().RunAsync(
            request,
            CancellationToken.None);

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Length.Should().BeLessThanOrEqualTo(128);
        result.StandardError.Length.Should().BeLessThanOrEqualTo(128);
        result.StandardOutput.Should().EndWith("...");
        result.StandardError.Should().EndWith("...");
    }
}
