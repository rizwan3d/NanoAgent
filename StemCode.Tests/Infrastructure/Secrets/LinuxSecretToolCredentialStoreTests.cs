using FluentAssertions;
using StemCode.Infrastructure.Secrets;
using StemCode.Tests.Infrastructure.Secrets.TestDoubles;

namespace StemCode.Tests.Infrastructure.Secrets;

public sealed class LinuxSecretToolCredentialStoreTests
{
    [Fact]
    public async Task LoadAsync_Should_ReturnNull_When_SecretIsMissing()
    {
        FakeProcessRunner processRunner = new();
        processRunner.EnqueueResult(new ProcessExecutionResult(1, string.Empty, string.Empty));

        LinuxSecretToolCredentialStore sut = new(processRunner);

        string? result = await sut.LoadAsync(
            new SecretReference("StemCode", "default-api-key", "StemCode API key"),
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_Should_ClearExistingSecretBeforeStoringNewValue()
    {
        FakeProcessRunner processRunner = new();
        processRunner.EnqueueResult(new ProcessExecutionResult(1, string.Empty, string.Empty));
        processRunner.EnqueueResult(new ProcessExecutionResult(0, string.Empty, string.Empty));

        LinuxSecretToolCredentialStore sut = new(processRunner);

        await sut.SaveAsync(
            new SecretReference("StemCode", "default-api-key", "StemCode API key"),
            "sk-secret",
            CancellationToken.None);

        processRunner.Requests.Should().HaveCount(2);
        processRunner.Requests[0].Arguments.Should().Equal(
            "clear",
            "service", "StemCode",
            "account", "default-api-key");
        processRunner.Requests[1].Arguments.Should().Equal(
            "store",
            "--label=StemCode API key",
            "service", "StemCode",
            "account", "default-api-key");
        processRunner.Requests[1].StandardInput.Should().Be("sk-secret");
    }
}
