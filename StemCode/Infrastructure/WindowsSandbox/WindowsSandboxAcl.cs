using Microsoft.Win32.SafeHandles;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.InteropServices;

namespace StemCode.Infrastructure.WindowsSandbox;

internal static class WindowsSandboxAcl
{
    private const FileSystemRights DenyWriteRights =
        FileSystemRights.Write |
        FileSystemRights.WriteData |
        FileSystemRights.AppendData |
        FileSystemRights.WriteExtendedAttributes |
        FileSystemRights.WriteAttributes |
        FileSystemRights.Delete |
        FileSystemRights.DeleteSubdirectoriesAndFiles |
        FileSystemRights.ChangePermissions |
        FileSystemRights.TakeOwnership;

    /// <summary>
    /// Rights withheld from restricted paths. Read, list, traverse, and attribute access are all
    /// denied so a restricted file cannot be read, enumerated, or probed for metadata.
    /// </summary>
    internal const FileSystemRights DenyReadRights =
        FileSystemRights.Read |
        FileSystemRights.ReadData |
        FileSystemRights.ListDirectory |
        FileSystemRights.ReadAttributes |
        FileSystemRights.ReadExtendedAttributes |
        FileSystemRights.ReadPermissions |
        FileSystemRights.ExecuteFile |
        FileSystemRights.Traverse;

    public static void AddAllowAce(string path, string sid, FileSystemRights rights)
    {
        ApplyRule(path, sid, rights, AccessControlType.Allow, remove: false);
    }

    public static void AddDenyWriteAce(string path, string sid)
    {
        ApplyRule(path, sid, DenyWriteRights, AccessControlType.Deny, remove: false);
    }

    /// <summary>
    /// Denies read access to <paramref name="path"/> for <paramref name="sid"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="FileSystemSecurity"/> keeps deny entries ahead of allow entries in canonical
    /// order, and explicit entries ahead of inherited ones, so this deny overrides a recursive
    /// read grant inherited from the workspace root.
    /// </remarks>
    public static void AddDenyReadAce(string path, string sid)
    {
        ApplyRule(path, sid, DenyReadRights, AccessControlType.Deny, remove: false);
    }

    public static void RevokeAce(string path, string sid, FileSystemRights rights, AccessControlType type)
    {
        ApplyRule(path, sid, rights, type, remove: true);
    }

    public static bool HasAce(string path, string sid, FileSystemRights rights, AccessControlType type)
    {
        FileSystemSecurity security = GetSecurity(path);
        SecurityIdentifier identity = new(sid);
        AuthorizationRuleCollection rules = security.GetAccessRules(
            includeExplicit: true,
            includeInherited: true,
            typeof(SecurityIdentifier));

        foreach (FileSystemAccessRule rule in rules.OfType<FileSystemAccessRule>())
        {
            if (rule.AccessControlType == type &&
                rule.IdentityReference == identity &&
                (rule.FileSystemRights & rights) == rights)
            {
                return true;
            }
        }

        return false;
    }

    public static void AllowNulDevice(string sid)
    {
        SecurityIdentifier identity = new(sid);
        byte[] sidBytes = new byte[identity.BinaryLength];
        identity.GetBinaryForm(sidBytes, 0);
        IntPtr sidPointer = Marshal.AllocHGlobal(sidBytes.Length);
        try
        {
            Marshal.Copy(sidBytes, 0, sidPointer, sidBytes.Length);

            using SafeFileHandle nulHandle = WindowsSandboxNative.CreateFile(
                @"\\.\NUL",
                WindowsSandboxNative.ReadControl | WindowsSandboxNative.WriteDac,
                WindowsSandboxNative.FileShareRead | WindowsSandboxNative.FileShareWrite,
                IntPtr.Zero,
                WindowsSandboxNative.OpenExisting,
                WindowsSandboxNative.FileAttributeNormal,
                IntPtr.Zero);
            if (nulHandle.IsInvalid)
            {
                return;
            }

            uint getSecurityResult = WindowsSandboxNative.GetSecurityInfo(
                nulHandle,
                WindowsSandboxNative.SeKernelObject,
                WindowsSandboxNative.DaclSecurityInformation,
                IntPtr.Zero,
                IntPtr.Zero,
                out IntPtr existingDacl,
                IntPtr.Zero,
                out IntPtr securityDescriptor);
            if (getSecurityResult != 0)
            {
                return;
            }

            try
            {
                WindowsSandboxNative.ExplicitAccess access = new()
                {
                    grfAccessPermissions = (uint)(FileSystemRights.ReadAndExecute | FileSystemRights.Write),
                    grfAccessMode = WindowsSandboxNative.SetAccess,
                    grfInheritance = 0,
                    Trustee = new WindowsSandboxNative.Trustee
                    {
                        pMultipleTrustee = IntPtr.Zero,
                        MultipleTrusteeOperation = 0,
                        TrusteeForm = WindowsSandboxNative.TrusteeIsSid,
                        TrusteeType = WindowsSandboxNative.TrusteeIsUnknown,
                        ptstrName = sidPointer
                    }
                };

                uint setEntriesResult = WindowsSandboxNative.SetEntriesInAcl(
                    1,
                    [access],
                    existingDacl,
                    out IntPtr newDacl);
                if (setEntriesResult != 0)
                {
                    return;
                }

                try
                {
                    _ = WindowsSandboxNative.SetSecurityInfo(
                        nulHandle,
                        WindowsSandboxNative.SeKernelObject,
                        WindowsSandboxNative.DaclSecurityInformation,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        newDacl,
                        IntPtr.Zero);
                }
                finally
                {
                    if (newDacl != IntPtr.Zero)
                    {
                        WindowsSandboxNative.LocalFree(newDacl);
                    }
                }
            }
            finally
            {
                if (securityDescriptor != IntPtr.Zero)
                {
                    WindowsSandboxNative.LocalFree(securityDescriptor);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(sidPointer);
        }
    }

    private static void ApplyRule(
        string path,
        string sid,
        FileSystemRights rights,
        AccessControlType type,
        bool remove)
    {
        try
        {
            FileSystemSecurity security = GetSecurity(path);
            SecurityIdentifier identity = new(sid);
            InheritanceFlags inheritance = Directory.Exists(path)
                ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit
                : InheritanceFlags.None;
            PropagationFlags propagation = Directory.Exists(path)
                ? PropagationFlags.None
                : PropagationFlags.NoPropagateInherit;
            FileSystemAccessRule rule = new(
                identity,
                rights,
                inheritance,
                propagation,
                type);

            bool changed = false;
            if (remove)
            {
                security.RemoveAccessRuleAll(rule);
                changed = true;
            }
            else if (!HasEquivalentRule(security, identity, rights, type))
            {
                security.AddAccessRule(rule);
                changed = true;
            }

            if (changed)
            {
                SetSecurity(path, security);
            }
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new UnauthorizedAccessException(
                $"Unable to update sandbox ACLs for '{path}'. {exception.Message}{BuildOwnershipHint(path)}",
                exception);
        }
    }

    private static bool HasEquivalentRule(
        FileSystemSecurity security,
        SecurityIdentifier identity,
        FileSystemRights rights,
        AccessControlType type)
    {
        AuthorizationRuleCollection rules = security.GetAccessRules(
            includeExplicit: true,
            includeInherited: false,
            typeof(SecurityIdentifier));
        return rules.OfType<FileSystemAccessRule>().Any(rule =>
            rule.AccessControlType == type &&
            rule.IdentityReference == identity &&
            (rule.FileSystemRights & rights) == rights);
    }

    private static FileSystemSecurity GetSecurity(string path)
    {
        return Directory.Exists(path)
            ? new DirectoryInfo(path).GetAccessControl()
            : new FileInfo(path).GetAccessControl();
    }

    private static void SetSecurity(string path, FileSystemSecurity security)
    {
        if (Directory.Exists(path))
        {
            new DirectoryInfo(path).SetAccessControl((DirectorySecurity)security);
            return;
        }

        new FileInfo(path).SetAccessControl((FileSecurity)security);
    }

    private static string BuildOwnershipHint(string path)
    {
        string? owner = TryGetOwner(path);
        if (string.IsNullOrWhiteSpace(owner))
        {
            return string.Empty;
        }

        string ownerMessage = $" Current owner: '{owner}'.";
        return IsSandboxAccount(owner)
            ? ownerMessage + " This path is owned by a StemCode sandbox account. Run /setup-sandbox to repair the workspace or change the folder owner back to your normal user."
            : ownerMessage;
    }

    private static bool IsSandboxAccount(string owner)
    {
        return owner.EndsWith("\\" + WindowsSandboxPaths.OfflineUsername, StringComparison.OrdinalIgnoreCase) ||
               owner.EndsWith("\\" + WindowsSandboxPaths.OnlineUsername, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetOwner(string path)
    {
        try
        {
            IdentityReference? owner = GetSecurity(path).GetOwner(typeof(NTAccount));
            return owner?.Value;
        }
        catch
        {
            return null;
        }
    }
}
