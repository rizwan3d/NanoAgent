using FluentAssertions;
using NanoAgent.Infrastructure.Secrets;
using NanoAgent.Tests.Infrastructure.Secrets.TestDoubles;

namespace NanoAgent.Tests.Infrastructure.Secrets;

public sealed class LinuxSecretToolCredentialStoreTests
{
    [Fact]
    public async Task LoadAsync_Should_ReturnNull_When_SecretIsMissing()
    {
        FakeProcessRunner processRunner = new();
        processRunner.EnqueueResult(new ProcessExecutionResult(1, string.Empty, string.Empty));

        LinuxSecretToolCredentialStore sut = new(processRunner);

        string? result = await sut.LoadAsync(
            new SecretReference("NanoAgent", "default-api-key", "NanoAgent API key"),
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
            new SecretReference("NanoAgent", "default-api-key", "NanoAgent API key"),
            "sk-secret",
            CancellationToken.None);

        processRunner.Requests.Should().HaveCount(2);
        processRunner.Requests[0].Arguments.Should().Equal(
            "clear",
            "service", "NanoAgent",
            "account", "default-api-key");
        processRunner.Requests[1].Arguments.Should().Equal(
            "store",
            "--label=NanoAgent API key",
            "service", "NanoAgent",
            "account", "default-api-key");
        processRunner.Requests[1].StandardInput.Should().Be("sk-secret");
    }
}
