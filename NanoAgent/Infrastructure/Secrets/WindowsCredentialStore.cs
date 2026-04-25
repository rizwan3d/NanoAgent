using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace NanoAgent.Infrastructure.Secrets;

internal sealed class WindowsCredentialStore : IPlatformCredentialStore
{
    private const int CredentialTypeGeneric = 1;
    private const int PersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    internal const int MaxCredentialBlobSize = 5 * 512;
    private const string ChunkManifestPrefix = "NanoAgent.WindowsCredentialStore.ChunkedSecret.v1:";

    public Task<string?> LoadAsync(SecretReference secretReference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(secretReference);
        cancellationToken.ThrowIfCancellationRequested();

        string targetName = BuildTargetName(secretReference);
        byte[]? primaryBlob = ReadCredentialBlob(targetName, "load", throwIfNotFound: false);
        if (primaryBlob is null || primaryBlob.Length == 0)
        {
            return Task.FromResult<string?>(null);
        }

        string primaryValue = Encoding.Unicode.GetString(primaryBlob);
        if (!TryParseChunkManifest(primaryValue, out int chunkCount, out _))
        {
            return Task.FromResult<string?>(primaryValue);
        }

        List<byte[]?> chunkBlobs = new(chunkCount);
        for (int index = 0; index < chunkCount; index++)
        {
            chunkBlobs.Add(ReadCredentialBlob(
                BuildChunkTargetName(targetName, index),
                "load",
                throwIfNotFound: false));
        }

        return Task.FromResult(DecodeStoredCredentialBlobs(primaryBlob, chunkBlobs));
    }

    public Task SaveAsync(
        SecretReference secretReference,
        string secretValue,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(secretReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretValue);
        cancellationToken.ThrowIfCancellationRequested();

        string targetName = BuildTargetName(secretReference);
        int? previousChunkCount = ReadStoredChunkCount(targetName);
        StoredCredentialBlobs storedCredentialBlobs = CreateStoredCredentialBlobs(secretValue);

        for (int index = 0; index < storedCredentialBlobs.ChunkBlobs.Count; index++)
        {
            WriteCredentialBlob(
                BuildChunkTargetName(targetName, index),
                secretReference.AccountName,
                storedCredentialBlobs.ChunkBlobs[index]);
        }

        WriteCredentialBlob(targetName, secretReference.AccountName, storedCredentialBlobs.PrimaryBlob);
        DeleteStaleChunks(targetName, storedCredentialBlobs.ChunkBlobs.Count, previousChunkCount);

        return Task.CompletedTask;
    }

    internal static StoredCredentialBlobs CreateStoredCredentialBlobs(string secretValue)
    {
        byte[] legacyBlob = Encoding.Unicode.GetBytes(secretValue);
        if (legacyBlob.Length <= MaxCredentialBlobSize)
        {
            return new StoredCredentialBlobs(legacyBlob, []);
        }

        byte[] secretBytes = Encoding.UTF8.GetBytes(secretValue);
        List<byte[]> chunks = new((secretBytes.Length + MaxCredentialBlobSize - 1) / MaxCredentialBlobSize);
        for (int offset = 0; offset < secretBytes.Length; offset += MaxCredentialBlobSize)
        {
            int length = Math.Min(MaxCredentialBlobSize, secretBytes.Length - offset);
            byte[] chunk = new byte[length];
            Buffer.BlockCopy(secretBytes, offset, chunk, 0, length);
            chunks.Add(chunk);
        }

        byte[] manifestBlob = Encoding.Unicode.GetBytes(BuildChunkManifest(chunks.Count, secretBytes.Length));
        return new StoredCredentialBlobs(manifestBlob, chunks);
    }

    internal static string? DecodeStoredCredentialBlobs(
        byte[] primaryBlob,
        IReadOnlyList<byte[]?> chunkBlobs)
    {
        string primaryValue = Encoding.Unicode.GetString(primaryBlob);
        if (!TryParseChunkManifest(primaryValue, out int expectedChunkCount, out int expectedByteLength))
        {
            return primaryValue;
        }

        if (chunkBlobs.Count != expectedChunkCount)
        {
            return null;
        }

        using MemoryStream secretStream = new(expectedByteLength);
        foreach (byte[]? chunkBlob in chunkBlobs)
        {
            if (chunkBlob is null)
            {
                return null;
            }

            secretStream.Write(chunkBlob);
        }

        if (secretStream.Length != expectedByteLength)
        {
            return null;
        }

        return Encoding.UTF8.GetString(secretStream.ToArray());
    }

    private static string BuildTargetName(SecretReference secretReference)
    {
        return $"{secretReference.ServiceName}:{secretReference.AccountName}";
    }

    private static string BuildChunkTargetName(string targetName, int index)
    {
        return $"{targetName}:chunk:{index}";
    }

    private static string BuildChunkManifest(int chunkCount, int byteLength)
    {
        return $"{ChunkManifestPrefix}{chunkCount}:{byteLength}";
    }

    private static bool TryParseChunkManifest(
        string value,
        out int chunkCount,
        out int byteLength)
    {
        chunkCount = 0;
        byteLength = 0;

        if (!value.StartsWith(ChunkManifestPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> manifest = value.AsSpan(ChunkManifestPrefix.Length);
        int separatorIndex = manifest.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex == manifest.Length - 1)
        {
            return false;
        }

        if (!int.TryParse(manifest[..separatorIndex], out chunkCount) ||
            !int.TryParse(manifest[(separatorIndex + 1)..], out byteLength) ||
            chunkCount <= 0 ||
            byteLength <= 0)
        {
            return false;
        }

        long minimumByteLength = ((long)chunkCount - 1) * MaxCredentialBlobSize + 1;
        long maximumByteLength = (long)chunkCount * MaxCredentialBlobSize;
        return byteLength >= minimumByteLength && byteLength <= maximumByteLength;
    }

    private static int? ReadStoredChunkCount(string targetName)
    {
        byte[]? primaryBlob = ReadCredentialBlob(targetName, "load", throwIfNotFound: false);
        if (primaryBlob is null)
        {
            return null;
        }

        string primaryValue = Encoding.Unicode.GetString(primaryBlob);
        return TryParseChunkManifest(primaryValue, out int chunkCount, out _)
            ? chunkCount
            : null;
    }

    private static byte[]? ReadCredentialBlob(
        string targetName,
        string operation,
        bool throwIfNotFound)
    {
        if (!CredRead(
            targetName,
            CredentialTypeGeneric,
            0,
            out nint credentialPointer))
        {
            int errorCode = Marshal.GetLastWin32Error();
            if (errorCode == ErrorNotFound && !throwIfNotFound)
            {
                return null;
            }

            throw CreateException(operation, errorCode);
        }

        try
        {
            NativeCredential credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.CredentialBlob == nint.Zero || credential.CredentialBlobSize <= 0)
            {
                return [];
            }

            byte[] buffer = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, buffer, 0, buffer.Length);
            return buffer;
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    private static void WriteCredentialBlob(
        string targetName,
        string accountName,
        byte[] credentialBlob)
    {
        if (credentialBlob.Length > MaxCredentialBlobSize)
        {
            throw new SecretStorageException(
                $"Unable to save the secret in Windows Credential Manager. Credential blob length {credentialBlob.Length} exceeds the Windows limit of {MaxCredentialBlobSize} bytes.");
        }

        nint secretPointer = Marshal.AllocCoTaskMem(credentialBlob.Length);
        try
        {
            Marshal.Copy(credentialBlob, 0, secretPointer, credentialBlob.Length);

            NativeCredential credential = new()
            {
                Type = CredentialTypeGeneric,
                TargetName = targetName,
                CredentialBlobSize = credentialBlob.Length,
                CredentialBlob = secretPointer,
                Persist = PersistLocalMachine,
                UserName = accountName
            };

            if (!CredWrite(ref credential, 0))
            {
                throw CreateException("save", Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(secretPointer);
        }
    }

    private static void DeleteStaleChunks(
        string targetName,
        int currentChunkCount,
        int? previousChunkCount)
    {
        if (previousChunkCount is not int chunkCount || chunkCount <= currentChunkCount)
        {
            return;
        }

        for (int index = currentChunkCount; index < chunkCount; index++)
        {
            TryDeleteCredential(BuildChunkTargetName(targetName, index));
        }
    }

    private static bool TryDeleteCredential(string targetName)
    {
        if (CredDelete(targetName, CredentialTypeGeneric, 0))
        {
            return true;
        }

        return Marshal.GetLastWin32Error() == ErrorNotFound;
    }

    private static SecretStorageException CreateException(string operation, int errorCode)
    {
        return new SecretStorageException(
            $"Unable to {operation} the secret in Windows Credential Manager. {new Win32Exception(errorCode).Message}",
            new Win32Exception(errorCode));
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(nint credential);

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(
        string target,
        int type,
        int reservedFlag,
        out nint credential);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(
        string target,
        int type,
        int flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(
        ref NativeCredential userCredential,
        int flags);

    internal sealed record StoredCredentialBlobs(
        byte[] PrimaryBlob,
        IReadOnlyList<byte[]> ChunkBlobs);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public FILETIME LastWritten;
        public int CredentialBlobSize;
        public nint CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public nint Attributes;
        public string? TargetAlias;
        public string? UserName;
    }
}
