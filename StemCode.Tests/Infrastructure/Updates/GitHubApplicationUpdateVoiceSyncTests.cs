using FluentAssertions;
using StemCode.Application.Models;
using StemCode.Infrastructure.Secrets;
using StemCode.Infrastructure.Updates;
using StemCode.Tests.Infrastructure.Secrets.TestDoubles;

namespace StemCode.Tests.Infrastructure.Updates;

public sealed class GitHubApplicationUpdateVoiceSyncTests
{
    [Fact]
    public async Task InstallAsync_Should_RunInstaller_WhenCliIsCurrent_ToSynchronizeVoiceRuntime()
    {
        FakeProcessRunner processRunner = new();
        processRunner.EnqueueResult(new ProcessExecutionResult(0, string.Empty, string.Empty));
        GitHubApplicationUpdateService sut = new(new HttpClient(), processRunner);
        ApplicationUpdateInfo updateInfo = new(
            "1.2.3",
            "1.2.3",
            new Uri("https://github.com/rizwan3d/StemCode/releases/latest"),
            IsUpdateAvailable: false);

        ApplicationUpdateInstallResult result = await sut.InstallAsync(
            updateInfo,
            progress: null,
            CancellationToken.None);

        ProcessExecutionRequest request = processRunner.Requests.Should().ContainSingle().Subject;
        request.EnvironmentVariables.Should().ContainKey("StemCode_TAG")
            .WhoseValue.Should().Be("1.2.3");
        result.IsSuccess.Should().BeTrue();
        result.Message.Should().Contain("Voice runtime synchronization");
    }
}
