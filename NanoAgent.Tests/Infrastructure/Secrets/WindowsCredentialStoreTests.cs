using FluentAssertions;
using NanoAgent.Infrastructure.Secrets;
using System.Text;

namespace NanoAgent.Tests.Infrastructure.Secrets;

public sealed class WindowsCredentialStoreTests
{
    [Fact]
    public void CreateStoredCredentialBlobs_Should_UseSingleLegacyBlobForSmallSecrets()
    {
        WindowsCredentialStore.StoredCredentialBlobs blobs =
            WindowsCredentialStore.CreateStoredCredentialBlobs("sk-secret");

        blobs.ChunkBlobs.Should().BeEmpty();
        Encoding.Unicode.GetString(blobs.PrimaryBlob).Should().Be("sk-secret");
        WindowsCredentialStore.DecodeStoredCredentialBlobs(
            blobs.PrimaryBlob,
            blobs.ChunkBlobs).Should().Be("sk-secret");
    }

    [Fact]
    public void CreateStoredCredentialBlobs_Should_ChunkSecretsThatExceedWindowsBlobLimit()
    {
        string secret = new('a', WindowsCredentialStore.MaxCredentialBlobSize + 1);

        WindowsCredentialStore.StoredCredentialBlobs blobs =
            WindowsCredentialStore.CreateStoredCredentialBlobs(secret);

        blobs.ChunkBlobs.Should().NotBeEmpty();
        blobs.PrimaryBlob.Length.Should().BeLessThanOrEqualTo(WindowsCredentialStore.MaxCredentialBlobSize);
        blobs.ChunkBlobs.Should().OnlyContain(chunk =>
            chunk.Length <= WindowsCredentialStore.MaxCredentialBlobSize);
        WindowsCredentialStore.DecodeStoredCredentialBlobs(
            blobs.PrimaryBlob,
            blobs.ChunkBlobs).Should().Be(secret);
    }

    [Fact]
    public void DecodeStoredCredentialBlobs_Should_ReturnNullWhenChunkedSecretIsIncomplete()
    {
        string secret = new('a', WindowsCredentialStore.MaxCredentialBlobSize + 1);
        WindowsCredentialStore.StoredCredentialBlobs blobs =
            WindowsCredentialStore.CreateStoredCredentialBlobs(secret);

        WindowsCredentialStore.DecodeStoredCredentialBlobs(
            blobs.PrimaryBlob,
            blobs.ChunkBlobs.Skip(1).Cast<byte[]?>().ToArray()).Should().BeNull();
    }
}
