using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;

namespace NanoAgent.Infrastructure.WindowsSandbox;

internal sealed class WindowsSandboxDesktop : IDisposable
{
    private const uint DesktopReadObjects = 0x0001;
    private const uint DesktopCreateWindow = 0x0002;
    private const uint DesktopCreateMenu = 0x0004;
    private const uint DesktopHookControl = 0x0008;
    private const uint DesktopJournalRecord = 0x0010;
    private const uint DesktopJournalPlayback = 0x0020;
    private const uint DesktopEnumerate = 0x0040;
    private const uint DesktopWriteObjects = 0x0080;
    private const uint DesktopSwitchDesktop = 0x0100;
    private const uint DesktopAllAccess = DesktopReadObjects |
                                          DesktopCreateWindow |
                                          DesktopCreateMenu |
                                          DesktopHookControl |
                                          DesktopJournalRecord |
                                          DesktopJournalPlayback |
                                          DesktopEnumerate |
                                          DesktopWriteObjects |
                                          DesktopSwitchDesktop |
                                          WindowsSandboxNative.Delete |
                                          WindowsSandboxNative.ReadControl |
                                          WindowsSandboxNative.WriteDac |
                                          WindowsSandboxNative.WriteOwner;

    private readonly IntPtr _handle;

    private WindowsSandboxDesktop(IntPtr handle, string desktopName)
    {
        _handle = handle;
        DesktopName = desktopName;
    }

    public const string DefaultDesktop = @"Winsta0\Default";

    public string DesktopName { get; }

    public static WindowsSandboxDesktop Prepare(bool usePrivateDesktop)
    {
        if (!usePrivateDesktop)
        {
            return new WindowsSandboxDesktop(IntPtr.Zero, DefaultDesktop);
        }

        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        string name = "NanoAgentSandboxDesktop-" + Convert.ToHexString(bytes).ToLowerInvariant();
        IntPtr handle = WindowsSandboxNative.CreateDesktop(
            name,
            null,
            IntPtr.Zero,
            0,
            DesktopAllAccess,
            IntPtr.Zero);
        if (handle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"CreateDesktopW failed for '{name}'.");
        }

        try
        {
            GrantDesktopAccess(handle);
            return new WindowsSandboxDesktop(handle, @"Winsta0\" + name);
        }
        catch
        {
            WindowsSandboxNative.CloseDesktop(handle);
            throw;
        }
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            WindowsSandboxNative.CloseDesktop(_handle);
        }
    }

    private static void GrantDesktopAccess(IntPtr desktopHandle)
    {
        using SafeAccessTokenHandle token = OpenCurrentProcessToken();
        string logonSid = ResolveLogonSid(token);
        if (!WindowsSandboxNative.ConvertStringSidToSid(logonSid, out IntPtr sid))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"ConvertStringSidToSid failed for '{logonSid}'.");
        }

        try
        {
            WindowsSandboxNative.ExplicitAccess access = new()
            {
                grfAccessPermissions = DesktopAllAccess,
                grfAccessMode = WindowsSandboxNative.GrantAccess,
                grfInheritance = 0,
                Trustee = new WindowsSandboxNative.Trustee
                {
                    pMultipleTrustee = IntPtr.Zero,
                    MultipleTrusteeOperation = 0,
                    TrusteeForm = WindowsSandboxNative.TrusteeIsSid,
                    TrusteeType = WindowsSandboxNative.TrusteeIsUnknown,
                    ptstrName = sid
                }
            };

            uint result = WindowsSandboxNative.SetEntriesInAcl(
                1,
                [access],
                IntPtr.Zero,
                out IntPtr newAcl);
            if (result != 0)
            {
                throw new Win32Exception(unchecked((int)result), "SetEntriesInAclW failed for private desktop.");
            }

            try
            {
                result = WindowsSandboxNative.SetSecurityInfo(
                    desktopHandle,
                    WindowsSandboxNative.SeWindowObject,
                    WindowsSandboxNative.DaclSecurityInformation,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    newAcl,
                    IntPtr.Zero);
                if (result != 0)
                {
                    throw new Win32Exception(unchecked((int)result), "SetSecurityInfo failed for private desktop.");
                }
            }
            finally
            {
                if (newAcl != IntPtr.Zero)
                {
                    WindowsSandboxNative.LocalFree(newAcl);
                }
            }
        }
        finally
        {
            WindowsSandboxNative.LocalFree(sid);
        }
    }

    private static SafeAccessTokenHandle OpenCurrentProcessToken()
    {
        if (!WindowsSandboxNative.OpenProcessToken(
                WindowsSandboxNative.GetCurrentProcess(),
                WindowsSandboxNative.TokenQuery,
                out SafeAccessTokenHandle token))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcessToken failed for desktop ACL setup.");
        }

        return token;
    }

    private static string ResolveLogonSid(SafeAccessTokenHandle token)
    {
        _ = WindowsSandboxNative.GetTokenInformation(
            token,
            WindowsSandboxNative.TokenGroups,
            IntPtr.Zero,
            0,
            out uint requiredLength);
        if (requiredLength == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetTokenInformation TokenGroups size query failed.");
        }

        IntPtr buffer = Marshal.AllocHGlobal(checked((int)requiredLength));
        try
        {
            if (!WindowsSandboxNative.GetTokenInformation(
                    token,
                    WindowsSandboxNative.TokenGroups,
                    buffer,
                    requiredLength,
                    out _))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetTokenInformation TokenGroups failed.");
            }

            int groupCount = Marshal.ReadInt32(buffer);
            int offset = IntPtr.Size == 8 ? 8 : 4;
            int groupSize = IntPtr.Size + sizeof(uint) + (IntPtr.Size == 8 ? 4 : 0);
            for (int index = 0; index < groupCount; index++)
            {
                IntPtr entry = IntPtr.Add(buffer, offset + (index * groupSize));
                IntPtr sid = Marshal.ReadIntPtr(entry);
                uint attributes = unchecked((uint)Marshal.ReadInt32(entry, IntPtr.Size));
                if ((attributes & WindowsSandboxNative.SeGroupLogonId) == WindowsSandboxNative.SeGroupLogonId)
                {
                    return new SecurityIdentifier(sid).Value;
                }
            }

            throw new InvalidOperationException("Current token does not contain a logon SID.");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
