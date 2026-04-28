using System.Globalization;
using NanoAgent.Application.Models;
using NanoAgent.Infrastructure.Secrets;
using NanoAgent.Infrastructure.Updates;
using NanoAgent.Tests.Infrastructure.Secrets.TestDoubles;
using FluentAssertions;

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
}
