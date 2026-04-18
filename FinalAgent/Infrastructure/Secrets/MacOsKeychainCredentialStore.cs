using System.Runtime.InteropServices;
using System.Text;

namespace FinalAgent.Infrastructure.Secrets;

internal sealed class MacOsKeychainCredentialStore : IPlatformCredentialStore
{
    private const int DuplicateItemStatus = -25299;
    private const int ItemNotFoundStatus = -25300;

    public Task<string?> LoadAsync(SecretReference secretReference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(secretReference);
        cancellationToken.ThrowIfCancellationRequested();

        byte[] serviceName = Encoding.UTF8.GetBytes(secretReference.ServiceName);
        byte[] accountName = Encoding.UTF8.GetBytes(secretReference.AccountName);

        uint passwordLength = 0;
        nint passwordData = nint.Zero;
        nint itemReference = nint.Zero;

        int status = SecKeychainFindGenericPassword(
            nint.Zero,
            (uint)serviceName.Length,
            serviceName,
            (uint)accountName.Length,
            accountName,
            out passwordLength,
            out passwordData,
            out itemReference);

        if (status == ItemNotFoundStatus)
        {
            return Task.FromResult<string?>(null);
        }

        if (status != 0)
        {
            throw CreateException("load", status);
        }

        try
        {
            if (passwordData == nint.Zero || passwordLength == 0)
            {
                return Task.FromResult<string?>(null);
            }

            byte[] buffer = new byte[passwordLength];
            Marshal.Copy(passwordData, buffer, 0, buffer.Length);
            return Task.FromResult<string?>(Encoding.UTF8.GetString(buffer));
        }
        finally
        {
            if (passwordData != nint.Zero)
            {
                SecKeychainItemFreeContent(nint.Zero, passwordData);
            }

            if (itemReference != nint.Zero)
            {
                CFRelease(itemReference);
            }
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

        byte[] serviceName = Encoding.UTF8.GetBytes(secretReference.ServiceName);
        byte[] accountName = Encoding.UTF8.GetBytes(secretReference.AccountName);
        byte[] secretData = Encoding.UTF8.GetBytes(secretValue);

        nint itemReference = nint.Zero;

        int addStatus = SecKeychainAddGenericPassword(
            nint.Zero,
            (uint)serviceName.Length,
            serviceName,
            (uint)accountName.Length,
            accountName,
            (uint)secretData.Length,
            secretData,
            out itemReference);

        try
        {
            if (addStatus == 0)
            {
                return Task.CompletedTask;
            }

            if (addStatus != DuplicateItemStatus)
            {
                throw CreateException("save", addStatus);
            }
        }
        finally
        {
            if (itemReference != nint.Zero)
            {
                CFRelease(itemReference);
            }
        }

        nint existingItem = FindExistingItemReference(serviceName, accountName);
        try
        {
            int updateStatus = SecKeychainItemModifyAttributesAndData(
                existingItem,
                nint.Zero,
                (uint)secretData.Length,
                secretData);

            if (updateStatus != 0)
            {
                throw CreateException("save", updateStatus);
            }
        }
        finally
        {
            if (existingItem != nint.Zero)
            {
                CFRelease(existingItem);
            }
        }

        return Task.CompletedTask;
    }

    private static SecretStorageException CreateException(string operation, int status)
    {
        return SecretStorageException.ForOperation(
            "macOS Keychain",
            operation,
            $"Keychain error {status}.");
    }

    private static nint FindExistingItemReference(byte[] serviceName, byte[] accountName)
    {
        uint passwordLength = 0;
        nint passwordData = nint.Zero;
        nint itemReference = nint.Zero;

        int status = SecKeychainFindGenericPassword(
            nint.Zero,
            (uint)serviceName.Length,
            serviceName,
            (uint)accountName.Length,
            accountName,
            out passwordLength,
            out passwordData,
            out itemReference);

        try
        {
            if (status == ItemNotFoundStatus)
            {
                throw CreateException("save", status);
            }

            if (status != 0)
            {
                throw CreateException("save", status);
            }

            return itemReference;
        }
        finally
        {
            if (passwordData != nint.Zero)
            {
                SecKeychainItemFreeContent(nint.Zero, passwordData);
            }
        }
    }

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(nint value);

    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainAddGenericPassword(
        nint keychain,
        uint serviceNameLength,
        byte[] serviceName,
        uint accountNameLength,
        byte[] accountName,
        uint passwordLength,
        byte[] passwordData,
        out nint itemReference);

    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainFindGenericPassword(
        nint keychainOrArray,
        uint serviceNameLength,
        byte[] serviceName,
        uint accountNameLength,
        byte[] accountName,
        out uint passwordLength,
        out nint passwordData,
        out nint itemReference);

    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainItemFreeContent(
        nint attributeList,
        nint data);

    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainItemModifyAttributesAndData(
        nint itemReference,
        nint attributeList,
        uint dataLength,
        byte[] data);
}
