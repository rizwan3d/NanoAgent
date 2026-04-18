using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace FinalAgent.Infrastructure.Secrets;

internal sealed class WindowsCredentialStore : IPlatformCredentialStore
{
    private const int CredentialTypeGeneric = 1;
    private const int PersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public Task<string?> LoadAsync(SecretReference secretReference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(secretReference);
        cancellationToken.ThrowIfCancellationRequested();

        if (!CredRead(
            BuildTargetName(secretReference),
            CredentialTypeGeneric,
            0,
            out nint credentialPointer))
        {
            int errorCode = Marshal.GetLastWin32Error();
            if (errorCode == ErrorNotFound)
            {
                return Task.FromResult<string?>(null);
            }

            throw CreateException("load", errorCode);
        }

        try
        {
            NativeCredential credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.CredentialBlob == nint.Zero || credential.CredentialBlobSize <= 0)
            {
                return Task.FromResult<string?>(null);
            }

            byte[] buffer = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, buffer, 0, buffer.Length);
            return Task.FromResult<string?>(Encoding.Unicode.GetString(buffer));
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public Task SaveAsync(
        SecretReference secretReference,
        string secretValue,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(secretReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretValue);
        cancellationToken.ThrowIfCancellationRequested();

        byte[] secretBytes = Encoding.Unicode.GetBytes(secretValue);
        nint secretPointer = Marshal.AllocCoTaskMem(secretBytes.Length);

        try
        {
            Marshal.Copy(secretBytes, 0, secretPointer, secretBytes.Length);

            NativeCredential credential = new()
            {
                Type = CredentialTypeGeneric,
                TargetName = BuildTargetName(secretReference),
                CredentialBlobSize = secretBytes.Length,
                CredentialBlob = secretPointer,
                Persist = PersistLocalMachine,
                UserName = secretReference.AccountName
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

        return Task.CompletedTask;
    }

    private static string BuildTargetName(SecretReference secretReference)
    {
        return $"{secretReference.ServiceName}:{secretReference.AccountName}";
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

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(
        ref NativeCredential userCredential,
        int flags);

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
