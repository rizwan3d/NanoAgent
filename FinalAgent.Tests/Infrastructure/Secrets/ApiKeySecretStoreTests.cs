using FinalAgent.Infrastructure.Configuration;
using FinalAgent.Infrastructure.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace FinalAgent.Tests.Infrastructure.Secrets;

public sealed class ApiKeySecretStoreTests
{
    [Fact]
    public async Task LoadAsync_Should_RequestSecretUsingProductNameReference()
    {
        ApplicationOptions options = new()
        {
            ProductName = "FinalAgent",
            StorageDirectoryName = "FinalAgent"
        };

        FakePlatformCredentialStore platformCredentialStore = new
        (
            "stored-secret"
        );

        ApiKeySecretStore sut = new(
            Options.Create(options),
            platformCredentialStore);

        string? result = await sut.LoadAsync(CancellationToken.None);

        result.Should().Be("stored-secret");
        platformCredentialStore.LastLoadedReference.Should().Be(
            new SecretReference("FinalAgent", "default-api-key", "FinalAgent API key"));
    }

    [Fact]
    public async Task SaveAsync_Should_TrimAndPersistSecretUsingProductNameReference()
    {
        ApplicationOptions options = new()
        {
            ProductName = "FinalAgent",
            StorageDirectoryName = "FinalAgent"
        };

        FakePlatformCredentialStore platformCredentialStore = new(null);

        ApiKeySecretStore sut = new(
            Options.Create(options),
            platformCredentialStore);

        await sut.SaveAsync("  sk-secret  ", CancellationToken.None);

        platformCredentialStore.LastSavedReference.Should().Be(
            new SecretReference("FinalAgent", "default-api-key", "FinalAgent API key"));
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
}
