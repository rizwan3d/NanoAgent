using NanoAgent.Infrastructure.Secrets;
using FluentAssertions;

namespace NanoAgent.Tests.Infrastructure.Secrets;

public sealed class ApiKeySecretStoreTests
{
    [Fact]
    public async Task LoadAsync_Should_PreferEnvironmentApiKey_When_Set()
    {
        using EnvironmentVariableScope apiKey = new("NANOAGENT_API_KEY", "  env-secret  ");
        FakePlatformCredentialStore platformCredentialStore = new("stored-secret");
        ApiKeySecretStore sut = new(platformCredentialStore);

        string? result = await sut.LoadAsync(CancellationToken.None);

        result.Should().Be("env-secret");
        platformCredentialStore.LastLoadedReference.Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_Should_RequestSecretUsingProductNameReference()
    {
        using EnvironmentVariableScope apiKey = new("NANOAGENT_API_KEY", null);
        FakePlatformCredentialStore platformCredentialStore = new
        (
            "stored-secret"
        );

        ApiKeySecretStore sut = new(platformCredentialStore);

        string? result = await sut.LoadAsync(CancellationToken.None);

        result.Should().Be("stored-secret");
        platformCredentialStore.LastLoadedReference.Should().Be(
            new SecretReference("NanoAgent", "default-api-key", "NanoAgent API key"));
    }

    [Fact]
    public async Task SaveAsync_Should_TrimAndPersistSecretUsingProductNameReference()
    {
        FakePlatformCredentialStore platformCredentialStore = new(null);

        ApiKeySecretStore sut = new(platformCredentialStore);

        await sut.SaveAsync("  sk-secret  ", CancellationToken.None);

        platformCredentialStore.LastSavedReference.Should().Be(
            new SecretReference("NanoAgent", "default-api-key", "NanoAgent API key"));
        platformCredentialStore.LastSavedSecret.Should().Be("sk-secret");
    }

    private sealed class FakePlatformCredentialStore : IPlatformCredentialStore
    {
        private readonly string? _valueToLoad;

        public FakePlatformCredentialStore(string? valueToLoad)
        {
            _valueToLoad = valueToLoad;
        }

        public SecretReference? LastLoadedReference { get; private set; }

        public SecretReference? LastSavedReference { get; private set; }

        public string? LastSavedSecret { get; private set; }

        public Task<string?> LoadAsync(SecretReference secretReference, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastLoadedReference = secretReference;
            return Task.FromResult(_valueToLoad);
        }

        public Task SaveAsync(
            SecretReference secretReference,
            string secretValue,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastSavedReference = secretReference;
            LastSavedSecret = secretValue;
            return Task.CompletedTask;
        }
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previousValue;

        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _previousValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _previousValue);
        }
    }
}
