using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace NanoAgent.Infrastructure.WindowsSandbox;

internal sealed class WindowsSandboxToken : IDisposable
{
    private readonly List<IntPtr> _allocatedSids;

    private WindowsSandboxToken(SafeAccessTokenHandle handle, List<IntPtr> allocatedSids)
    {
        Handle = handle;
        _allocatedSids = allocatedSids;
    }

    public SafeAccessTokenHandle Handle { get; }

    public static WindowsSandboxToken CreateRestricted(string nanoAgentHome, IEnumerable<string> restrictingSidStrings)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows restricted tokens are only available on Windows.");
        }

        WindowsSandboxCredentials credentials = WindowsSandboxIdentity.LoadOfflineCredentials(nanoAgentHome);
        if (!WindowsSandboxNative.LogonUser(
                credentials.Username,
                ".",
                credentials.Password,
                WindowsSandboxNative.Logon32LogonInteractive,
                WindowsSandboxNative.Logon32ProviderDefault,
                out SafeAccessTokenHandle sandboxUserToken))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"LogonUserW failed for '{credentials.Username}'.");
        }

        using (sandboxUserToken)
        {
            return CreateRestrictedFromToken(sandboxUserToken, restrictingSidStrings, includeUserSid: true);
        }
    }

    public static WindowsSandboxToken CreateRestricted(IEnumerable<string> restrictingSidStrings)
    {
        return CreateRestrictedFromCurrentToken(restrictingSidStrings, includeUserSid: false);
    }

    public static WindowsSandboxToken CreateRestrictedForCurrentUser(IEnumerable<string> restrictingSidStrings)
    {
        return CreateRestrictedFromCurrentToken(restrictingSidStrings, includeUserSid: true);
    }

    private static WindowsSandboxToken CreateRestrictedFromCurrentToken(
        IEnumerable<string> restrictingSidStrings,
        bool includeUserSid)
    {
        if (!WindowsSandboxNative.OpenProcessToken(
                Process.GetCurrentProcess().Handle,
                WindowsSandboxNative.TokenAllAccessForSandbox,
                out SafeAccessTokenHandle currentToken))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcessToken failed.");
        }

        using (currentToken)
        {
            return CreateRestrictedFromToken(currentToken, restrictingSidStrings, includeUserSid);
        }
    }

    private static WindowsSandboxToken CreateRestrictedFromToken(
        SafeAccessTokenHandle baseToken,
        IEnumerable<string> restrictingSidStrings,
        bool includeUserSid)
    {
        List<IntPtr> allocatedSids = [];
        try
        {
            List<WindowsSandboxNative.SidAndAttributes> restrictingSids = [];
            foreach (string sidText in restrictingSidStrings.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!WindowsSandboxNative.ConvertStringSidToSid(sidText, out IntPtr sid))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"ConvertStringSidToSid failed for '{sidText}'.");
                }

                allocatedSids.Add(sid);
                restrictingSids.Add(new WindowsSandboxNative.SidAndAttributes { Sid = sid });
            }

            if (includeUserSid)
            {
                AddUserRestrictingSid(baseToken, restrictingSids, allocatedSids);
            }

            AddLogonRestrictingSid(baseToken, restrictingSids, allocatedSids);
            AddWellKnownRestrictingSid(WellKnownSidType.WorldSid, restrictingSids, allocatedSids);

            if (!WindowsSandboxNative.CreateRestrictedToken(
                    baseToken,
                    WindowsSandboxNative.DisableMaxPrivilege | WindowsSandboxNative.LuaToken | WindowsSandboxNative.WriteRestricted,
                    0,
                    IntPtr.Zero,
                    0,
                    IntPtr.Zero,
                    (uint)restrictingSids.Count,
                    restrictingSids.ToArray(),
                    out SafeAccessTokenHandle restrictedToken))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateRestrictedToken failed.");
            }

            SetDefaultDacl(restrictedToken, restrictingSids);
            EnablePrivilege(restrictedToken, "SeChangeNotifyPrivilege");
            return new WindowsSandboxToken(restrictedToken, allocatedSids);
        }
        catch
        {
            foreach (IntPtr sid in allocatedSids)
            {
                WindowsSandboxNative.LocalFree(sid);
            }

            throw;
        }
    }

    private static void AddUserRestrictingSid(
        SafeAccessTokenHandle token,
        List<WindowsSandboxNative.SidAndAttributes> restrictingSids,
        List<IntPtr> allocatedSids)
    {
        string userSid = GetUserSid(token);
        if (!WindowsSandboxNative.ConvertStringSidToSid(userSid, out IntPtr sid))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"ConvertStringSidToSid failed for '{userSid}'.");
        }

        allocatedSids.Add(sid);
        restrictingSids.Add(new WindowsSandboxNative.SidAndAttributes { Sid = sid });
    }

    private static void AddLogonRestrictingSid(
        SafeAccessTokenHandle token,
        List<WindowsSandboxNative.SidAndAttributes> restrictingSids,
        List<IntPtr> allocatedSids)
    {
        string logonSid = GetLogonSid(token);
        if (!WindowsSandboxNative.ConvertStringSidToSid(logonSid, out IntPtr sid))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"ConvertStringSidToSid failed for '{logonSid}'.");
        }

        allocatedSids.Add(sid);
        restrictingSids.Add(new WindowsSandboxNative.SidAndAttributes { Sid = sid });
    }

    private static string GetLogonSid(SafeAccessTokenHandle token)
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

    private static string GetUserSid(SafeAccessTokenHandle token)
    {
        _ = WindowsSandboxNative.GetTokenInformation(
            token,
            WindowsSandboxNative.TokenUser,
            IntPtr.Zero,
            0,
            out uint requiredLength);
        if (requiredLength == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetTokenInformation TokenUser size query failed.");
        }

        IntPtr buffer = Marshal.AllocHGlobal(checked((int)requiredLength));
        try
        {
            if (!WindowsSandboxNative.GetTokenInformation(
                    token,
                    WindowsSandboxNative.TokenUser,
                    buffer,
                    requiredLength,
                    out _))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetTokenInformation TokenUser failed.");
            }

            return new SecurityIdentifier(Marshal.ReadIntPtr(buffer)).Value;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void SetDefaultDacl(
        SafeAccessTokenHandle token,
        IReadOnlyCollection<WindowsSandboxNative.SidAndAttributes> restrictingSids)
    {
        WindowsSandboxNative.ExplicitAccess[] entries = restrictingSids
            .Select(static sid => new WindowsSandboxNative.ExplicitAccess
            {
                grfAccessPermissions = WindowsSandboxNative.GenericAll,
                grfAccessMode = WindowsSandboxNative.GrantAccess,
                grfInheritance = 0,
                Trustee = new WindowsSandboxNative.Trustee
                {
                    pMultipleTrustee = IntPtr.Zero,
                    MultipleTrusteeOperation = 0,
                    TrusteeForm = WindowsSandboxNative.TrusteeIsSid,
                    TrusteeType = WindowsSandboxNative.TrusteeIsUnknown,
                    ptstrName = sid.Sid
                }
            })
            .ToArray();

        uint result = WindowsSandboxNative.SetEntriesInAcl(
            (uint)entries.Length,
            entries,
            IntPtr.Zero,
            out IntPtr newAcl);
        if (result != 0)
        {
            throw new Win32Exception(unchecked((int)result), "SetEntriesInAclW failed for restricted token default DACL.");
        }

        try
        {
            WindowsSandboxNative.TokenDefaultDaclInformation info = new()
            {
                DefaultDacl = newAcl
            };
            IntPtr infoBuffer = Marshal.AllocHGlobal(Marshal.SizeOf<WindowsSandboxNative.TokenDefaultDaclInformation>());
            try
            {
                Marshal.StructureToPtr(info, infoBuffer, fDeleteOld: false);
                if (!WindowsSandboxNative.SetTokenInformation(
                        token,
                        WindowsSandboxNative.TokenDefaultDacl,
                        infoBuffer,
                        (uint)Marshal.SizeOf<WindowsSandboxNative.TokenDefaultDaclInformation>()))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "SetTokenInformation TokenDefaultDacl failed.");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(infoBuffer);
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

    private static void EnablePrivilege(SafeAccessTokenHandle token, string privilegeName)
    {
        if (!WindowsSandboxNative.LookupPrivilegeValue(null, privilegeName, out WindowsSandboxNative.Luid luid))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"LookupPrivilegeValueW failed for '{privilegeName}'.");
        }

        WindowsSandboxNative.TokenPrivileges privileges = new()
        {
            PrivilegeCount = 1,
            Privileges = new WindowsSandboxNative.LuidAndAttributes
            {
                Luid = luid,
                Attributes = WindowsSandboxNative.SePrivilegeEnabled
            }
        };

        if (!WindowsSandboxNative.AdjustTokenPrivileges(
                token,
                disableAllPrivileges: false,
                ref privileges,
                0,
                IntPtr.Zero,
                IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"AdjustTokenPrivileges failed for '{privilegeName}'.");
        }

        int error = Marshal.GetLastWin32Error();
        if (error != 0)
        {
            throw new Win32Exception(error, $"AdjustTokenPrivileges failed for '{privilegeName}'.");
        }
    }

    public void Dispose()
    {
        Handle.Dispose();
        foreach (IntPtr sid in _allocatedSids)
        {
            WindowsSandboxNative.LocalFree(sid);
        }
    }

    private static void AddWellKnownRestrictingSid(
        WellKnownSidType type,
        List<WindowsSandboxNative.SidAndAttributes> restrictingSids,
        List<IntPtr> allocatedSids)
    {
        SecurityIdentifier sidObject = new(type, null);
        string sidText = sidObject.Value;
        if (!WindowsSandboxNative.ConvertStringSidToSid(sidText, out IntPtr sid))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"ConvertStringSidToSid failed for '{sidText}'.");
        }

        allocatedSids.Add(sid);
        restrictingSids.Add(new WindowsSandboxNative.SidAndAttributes { Sid = sid });
    }
}
