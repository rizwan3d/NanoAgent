using FluentAssertions;
using StemCode.Application.Abstractions;
using StemCode.Infrastructure.Secrets;
using StemCode.Infrastructure.Updates;
using StemCode.Tests.Infrastructure.Secrets.TestDoubles;
using System.Collections.Generic;
using Xunit;

namespace StemCode.Tests.Infrastructure.Updates;

public sealed class StemCodeInstanceServiceTests
{
    [Theory]
    [InlineData("stemcode", true)]
    [InlineData("StemCode.CLI", true)]
    [InlineData("stemcode.exe", true)]
    [InlineData("STEMCODE.EXE", true)]
    [InlineData("dotnet", false)]
    [InlineData("node", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void MatchesStemCodeName_Should_RecognizeStemCodeBinaries(string? name, bool expected)
    {
        StemCodeInstanceService.MatchesStemCodeName(name!).Should().Be(expected);
    }

    [Fact]
    public void ParseWindowsLine_Should_ExcludeCurrentProcessAndNonStemCode()
    {
        int current = 5678;

        StemCodeInstanceService
            .ParseWindowsLine("\"stemcode.exe\",\"1234\",\"Services\",\"0\",\"12,345 K\"", current)
            .Should().Be(new RunningStemCodeInstance(1234, "stemcode.exe"));

        StemCodeInstanceService
            .ParseWindowsLine("\"dotnet.exe\",\"4321\",\"Console\",\"1\",\"1 K\"", current)
            .Should().BeNull();

        // The current session must never be flagged as an "other" instance.
        StemCodeInstanceService
            .ParseWindowsLine($"\"stemcode.exe\",\"{current}\",\"Console\",\"1\",\"1 K\"", current)
            .Should().BeNull();
    }

    [Fact]
    public void ParsePosixLine_Should_SkipHeaderAndExcludeNonStemCode()
    {
        int current = 5678;

        StemCodeInstanceService.ParsePosixLine("  PID COMM", current).Should().BeNull();

        StemCodeInstanceService
            .ParsePosixLine(" 1234 StemCode.CLI", current)
            .Should().Be(new RunningStemCodeInstance(1234, "StemCode.CLI"));

        StemCodeInstanceService
            .ParsePosixLine(" 4321 dotnet", current)
            .Should().BeNull();

        StemCodeInstanceService
            .ParsePosixLine($" {current} stemcode", current)
            .Should().BeNull();
    }

    [Fact]
    public async Task GetOtherRunningInstancesAsync_Should_RunEnumerationCommandAndFilterStemCode()
    {
        FakeProcessRunner processRunner = new();
        string output = OperatingSystem.IsWindows()
            ? "\"stemcode.exe\",\"1234\",\"Services\",\"0\",\"12,345 K\"\r\n\"dotnet.exe\",\"5678\",\"Console\",\"1\",\"45,678 K\"\r\n"
            : "  PID COMM\n 1234 StemCode.CLI\n 5678 dotnet\n";
        processRunner.EnqueueResult(new ProcessExecutionResult(0, output, string.Empty));

        StemCodeInstanceService sut = new(processRunner);

        IReadOnlyList<RunningStemCodeInstance> instances = await sut.GetOtherRunningInstancesAsync(
            CancellationToken.None);

        instances.Should().ContainSingle(
            instance => instance.ProcessId == 1234 &&
                        instance.ProcessName.Contains("stemcode", StringComparison.OrdinalIgnoreCase));
        instances.Should().NotContain(
            instance => instance.ProcessName.Contains("dotnet", StringComparison.OrdinalIgnoreCase));

        ProcessExecutionRequest request = processRunner.Requests.Should().ContainSingle().Subject;
        request.FileName.Should().Be(OperatingSystem.IsWindows() ? "tasklist" : "ps");
    }

    [Fact]
    public async Task TerminateAsync_Should_RunPlatformKillCommandForTarget()
    {
        FakeProcessRunner processRunner = new();
        processRunner.EnqueueResult(new ProcessExecutionResult(0, string.Empty, string.Empty));

        StemCodeInstanceService sut = new(processRunner);
        await sut.TerminateAsync(new RunningStemCodeInstance(1234, "stemcode.exe"), CancellationToken.None);

        ProcessExecutionRequest request = processRunner.Requests.Should().ContainSingle().Subject;
        request.FileName.Should().Be(OperatingSystem.IsWindows() ? "taskkill" : "kill");
        request.Arguments.Should().Contain("1234");
    }
}
