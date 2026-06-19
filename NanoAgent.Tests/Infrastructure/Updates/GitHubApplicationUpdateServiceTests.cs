using FluentAssertions;
using NanoAgent.Application.Models;
using NanoAgent.Infrastructure.Secrets;
using NanoAgent.Infrastructure.Updates;
using NanoAgent.Tests.Infrastructure.Secrets.TestDoubles;
using System.Globalization;

namespace NanoAgent.Tests.Infrastructure.Updates;

public sealed class GitHubApplicationUpdateServiceTests
{
    [Fact]
    public async Task InstallAsync_Should_PassCurrentProcessIdToWindowsInstaller()
    {
        FakeProcessRunner processRunner = new();
        processRunner.EnqueueResult(new ProcessExecutionResult(0, string.Empty, string.Empty));
        GitHubApplicationUpdateService sut = new(new HttpClient(), processRunner);
        ApplicationUpdateInfo updateInfo = new(
            "1.2.3",
            "1.2.4",
            new Uri("https://github.com/rizwan3d/NanoAgent/releases/latest"),
            IsUpdateAvailable: true);

        ApplicationUpdateInstallResult result = await sut.InstallAsync(
            updateInfo,
            progress: null,
            CancellationToken.None);

        ProcessExecutionRequest request = processRunner.Requests.Single();
        request.EnvironmentVariables.Should().ContainKey("NanoAgent_TAG")
            .WhoseValue.Should().Be("1.2.4");

        if (OperatingSystem.IsWindows())
        {
            request.FileName.Should().Be("powershell.exe");
            request.EnvironmentVariables.Should().ContainKey("NanoAgent_WAIT_FOR_PROCESS_ID")
                .WhoseValue.Should().Be(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            result.Message.Should().Contain("update prepared");
        }
        else
        {
            request.EnvironmentVariables.Should().NotContainKey("NanoAgent_WAIT_FOR_PROCESS_ID");
            result.Message.Should().Contain("update installed");
        }
    }

    [Fact]
    public async Task InstallAsync_Should_TargetTheRunningBinaryLocation()
    {
        FakeProcessRunner processRunner = new();
        processRunner.EnqueueResult(new ProcessExecutionResult(0, string.Empty, string.Empty));
        GitHubApplicationUpdateService sut = new(new HttpClient(), processRunner);
        ApplicationUpdateInfo updateInfo = new(
            "1.2.3",
            "1.2.4",
            new Uri("https://github.com/rizwan3d/NanoAgent/releases/latest"),
            IsUpdateAvailable: true);

        await sut.InstallAsync(updateInfo, progress: null, CancellationToken.None);

        ProcessExecutionRequest request = processRunner.Requests.Single();

        bool resolved = GitHubApplicationUpdateService.TryResolveRunningInstallLocation(
            Environment.ProcessPath,
            OperatingSystem.IsWindows(),
            out string installDirectory,
            out string commandName);

        if (resolved)
        {
            request.EnvironmentVariables.Should().ContainKey("NanoAgent_INSTALL_DIR")
                .WhoseValue.Should().Be(installDirectory);
            request.EnvironmentVariables.Should().ContainKey("NanoAgent_COMMAND_NAME")
                .WhoseValue.Should().Be(commandName);
        }
        else
        {
            request.EnvironmentVariables.Should().NotContainKey("NanoAgent_INSTALL_DIR");
            request.EnvironmentVariables.Should().NotContainKey("NanoAgent_COMMAND_NAME");
        }
    }

    [Fact]
    public async Task InstallAsync_Should_StreamInstallerOutputLinesToProgress()
    {
        FakeProcessRunner processRunner = new();
        processRunner.EnqueueResult(
            new ProcessExecutionResult(0, string.Empty, string.Empty),
            "[NanoAgent.CLI] [1/7] Checking system requirements...",
            "   ",
            "[NanoAgent.CLI] [2/7] Resolving release...");
        GitHubApplicationUpdateService sut = new(new HttpClient(), processRunner);
        ApplicationUpdateInfo updateInfo = new(
            "1.2.3",
            "1.2.4",
            new Uri("https://github.com/rizwan3d/NanoAgent/releases/latest"),
            IsUpdateAvailable: true);

        List<string> reported = [];
        CapturingProgress progress = new(reported);

        await sut.InstallAsync(updateInfo, progress, CancellationToken.None);

        // Whitespace-only lines are skipped; meaningful step lines stream through in order.
        reported.Should().Equal(
            "[NanoAgent.CLI] [1/7] Checking system requirements...",
            "[NanoAgent.CLI] [2/7] Resolving release...");
    }

    private sealed class CapturingProgress : IProgress<string>
    {
        private readonly List<string> _values;

        public CapturingProgress(List<string> values) => _values = values;

        public void Report(string value) => _values.Add(value);
    }

    [Fact]
    public void TryResolveRunningInstallLocation_Should_StripExecutableExtensionForWindows()
    {
        // Forward slashes are treated as path separators on both Windows and POSIX,
        // keeping this assertion deterministic regardless of the test host OS.
        bool resolved = GitHubApplicationUpdateService.TryResolveRunningInstallLocation(
            "C:/Tools/NanoAgent/nanoai.exe",
            stripExecutableExtension: true,
            out string installDirectory,
            out string commandName);

        resolved.Should().BeTrue();
        // Path.GetDirectoryName emits OS-native separators; normalize before comparing.
        installDirectory.Replace('\\', '/').Should().Be("C:/Tools/NanoAgent");
        commandName.Should().Be("nanoai");
    }

    [Fact]
    public void TryResolveRunningInstallLocation_Should_KeepFileNameForPosix()
    {
        bool resolved = GitHubApplicationUpdateService.TryResolveRunningInstallLocation(
            "/home/user/.local/bin/nanoai",
            stripExecutableExtension: false,
            out string installDirectory,
            out string commandName);

        resolved.Should().BeTrue();
        installDirectory.Replace('\\', '/').Should().Be("/home/user/.local/bin");
        commandName.Should().Be("nanoai");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryResolveRunningInstallLocation_Should_FailForMissingPath(string? processPath)
    {
        bool resolved = GitHubApplicationUpdateService.TryResolveRunningInstallLocation(
            processPath,
            stripExecutableExtension: true,
            out string installDirectory,
            out string commandName);

        resolved.Should().BeFalse();
        installDirectory.Should().BeEmpty();
        commandName.Should().BeEmpty();
    }

    [Theory]
    [InlineData("C:/Program Files/dotnet/dotnet.exe", true)]
    [InlineData("/usr/bin/dotnet", false)]
    public void TryResolveRunningInstallLocation_Should_FailForSharedDotnetHost(
        string processPath,
        bool stripExecutableExtension)
    {
        bool resolved = GitHubApplicationUpdateService.TryResolveRunningInstallLocation(
            processPath,
            stripExecutableExtension,
            out _,
            out _);

        resolved.Should().BeFalse();
    }
}
