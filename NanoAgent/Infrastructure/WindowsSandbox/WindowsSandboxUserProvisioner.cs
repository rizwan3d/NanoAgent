using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace NanoAgent.Infrastructure.WindowsSandbox;

internal static class WindowsSandboxUserProvisioner
{
    private const uint NormalAccount = 0x0200;
    private const uint UserCannotChangePassword = 0x0040;
    private const uint PasswordNeverExpires = 0x10000;
    private const uint UserPrivUser = 1;
    private const uint NerrSuccess = 0;
    private const uint NerrUserExists = 2224;
    private const uint NerrGroupExists = 2223;
    private const uint ErrorAliasExists = 1379;
    private const uint ErrorMemberInAlias = 1378;

    public static WindowsSandboxUsersFile EnsureUsers(string nanoAgentHome)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows sandbox users are only provisioned on Windows.");
        }

        foreach (string groupName in WindowsSandboxPaths.SandboxGroupNames())
        {
            EnsureLocalGroup(groupName);
        }

        WindowsSandboxUserRecord offline = EnsureUser(WindowsSandboxPaths.OfflineUsername);
        WindowsSandboxUserRecord online = EnsureUser(WindowsSandboxPaths.OnlineUsername);
        foreach (string groupName in WindowsSandboxPaths.SandboxGroupNames())
        {
            AddUserToGroup(WindowsSandboxPaths.OfflineUsername, groupName);
            AddUserToGroup(WindowsSandboxPaths.OnlineUsername, groupName);
        }

        WindowsSandboxUsersFile users = new()
        {
            Version = WindowsSandboxPaths.SetupVersion,
            Offline = offline,
            Online = online
        };

        WindowsSandboxPaths.EnsureStateDirectories(nanoAgentHome);
        File.WriteAllText(
            WindowsSandboxPaths.SandboxUsersPath(nanoAgentHome),
            JsonSerializer.Serialize(users, WindowsSandboxJsonContext.Default.WindowsSandboxUsersFile));

        return users;
    }

    public static bool IsUserInLocalGroup(string username, string groupName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        IntPtr buffer = IntPtr.Zero;
        try
        {
            uint result = NetLocalGroupGetMembers(
                null,
                groupName,
                3,
                out buffer,
                0xFFFFFFFF,
                out uint entriesRead,
                out _,
                IntPtr.Zero);
            if (result != NerrSuccess || buffer == IntPtr.Zero)
            {
                return false;
            }

            int size = Marshal.SizeOf<LocalGroupMembersInfo3>();
            for (int index = 0; index < entriesRead; index++)
            {
                IntPtr itemPtr = IntPtr.Add(buffer, index * size);
                LocalGroupMembersInfo3 item = Marshal.PtrToStructure<LocalGroupMembersInfo3>(itemPtr);
                string memberName = item.lgrmi3_domainandname;
                if (string.Equals(memberName, username, StringComparison.OrdinalIgnoreCase) ||
                    memberName.EndsWith("\\" + username, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                NetApiBufferFree(buffer);
            }
        }
    }

    private static WindowsSandboxUserRecord EnsureUser(string username)
    {
        string password = GeneratePassword();
        UserInfo1 user = new()
        {
            usri1_name = username,
            usri1_password = password,
            usri1_password_age = 0,
            usri1_priv = UserPrivUser,
            usri1_home_dir = null,
            usri1_comment = "NanoAgent Windows sandbox account",
            usri1_flags = NormalAccount | UserCannotChangePassword | PasswordNeverExpires,
            usri1_script_path = null
        };

        uint result = NetUserAdd(null, 1, ref user, out _);
        if (result == NerrUserExists)
        {
            UserInfo1003 passwordInfo = new()
            {
                usri1003_password = password
            };
            IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf<UserInfo1003>());
            try
            {
                Marshal.StructureToPtr(passwordInfo, buffer, fDeleteOld: false);
                result = NetUserSetInfo(null, username, 1003, buffer, out _);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        if (result != NerrSuccess)
        {
            throw new Win32Exception(unchecked((int)result), $"NetUserAdd/NetUserSetInfo failed for '{username}'.");
        }

        string protectedPassword = WindowsSandboxIdentity.ProtectPassword(password);
        return new WindowsSandboxUserRecord
        {
            Username = username,
            Password = protectedPassword
        };
    }

    private static void EnsureLocalGroup(string groupName)
    {
        LocalGroupInfo1 group = new()
        {
            lgrpi1_name = groupName,
            lgrpi1_comment = "NanoAgent Windows sandbox users"
        };
        uint result = NetLocalGroupAdd(null, 1, ref group, out _);
        if (result != NerrSuccess && result != NerrGroupExists && result != ErrorAliasExists)
        {
            throw new Win32Exception(unchecked((int)result), $"NetLocalGroupAdd failed for '{groupName}'.");
        }
    }

    private static void AddUserToGroup(string username, string groupName)
    {
        LocalGroupMembersInfo3 member = new()
        {
            lgrmi3_domainandname = username
        };
        uint result = NetLocalGroupAddMembers(null, groupName, 3, ref member, 1);
        if (result != NerrSuccess && result != ErrorMemberInAlias)
        {
            throw new Win32Exception(unchecked((int)result), $"NetLocalGroupAddMembers failed for '{username}'.");
        }
    }

    private static string GeneratePassword()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes) + "aA1!";
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LocalGroupInfo1
    {
        public string lgrpi1_name;

        public string lgrpi1_comment;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LocalGroupMembersInfo3
    {
        public string lgrmi3_domainandname;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct UserInfo1
    {
        public string usri1_name;

        public string usri1_password;

        public uint usri1_password_age;

        public uint usri1_priv;

        public string? usri1_home_dir;

        public string? usri1_comment;

        public uint usri1_flags;

        public string? usri1_script_path;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct UserInfo1003
    {
        public string usri1003_password;
    }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern uint NetLocalGroupAdd(
        string? servername,
        uint level,
        ref LocalGroupInfo1 buf,
        out uint parmError);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern uint NetUserAdd(
        string? servername,
        uint level,
        ref UserInfo1 buf,
        out uint parmError);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern uint NetUserSetInfo(
        string? servername,
        string username,
        uint level,
        IntPtr buf,
        out uint parmError);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern uint NetLocalGroupAddMembers(
        string? servername,
        string groupname,
        uint level,
        ref LocalGroupMembersInfo3 buf,
        uint totalentries);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern uint NetLocalGroupGetMembers(
        string? servername,
        string localgroupname,
        uint level,
        out IntPtr bufptr,
        uint prefmaxlen,
        out uint entriesread,
        out uint totalentries,
        IntPtr resumeHandle);

    [DllImport("netapi32.dll")]
    private static extern uint NetApiBufferFree(IntPtr buffer);
}
