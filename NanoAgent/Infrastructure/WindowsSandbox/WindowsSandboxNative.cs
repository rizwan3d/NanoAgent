using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text;

namespace NanoAgent.Infrastructure.WindowsSandbox;

internal static class WindowsSandboxNative
{
    public const uint CreateUnicodeEnvironment = 0x00000400;
    public const uint CreateNoWindow = 0x08000000;
    public const uint ExtendedStartupInfoPresent = 0x00080000;
    public const uint ErrorPrivilegeNotHeld = 1314;
    public const uint DisableMaxPrivilege = 0x00000001;
    public const uint LuaToken = 0x00000004;
    public const uint WriteRestricted = 0x00000008;
    public const uint TokenAssignPrimary = 0x0001;
    public const uint TokenDuplicate = 0x0002;
    public const uint TokenImpersonate = 0x0004;
    public const uint TokenQuery = 0x0008;
    public const uint TokenAdjustPrivileges = 0x0020;
    public const uint TokenAdjustDefault = 0x0080;
    public const uint TokenUser = 1;
    public const uint TokenGroups = 2;
    public const uint TokenDefaultDacl = 6;
    public const uint TokenAllAccessForSandbox = TokenAssignPrimary | TokenDuplicate | TokenImpersonate | TokenQuery | TokenAdjustPrivileges | TokenAdjustDefault;
    public const uint SeGroupLogonId = 0xC0000000;
    public const uint SePrivilegeEnabled = 0x00000002;
    public const uint GrantAccess = 1;
    public const uint TrusteeIsSid = 0;
    public const uint TrusteeIsUnknown = 0;
    public const uint GenericAll = 0x10000000;
    public const uint Delete = 0x00010000;
    public const uint ReadControl = 0x00020000;
    public const uint WriteDac = 0x00040000;
    public const uint WriteOwner = 0x00080000;
    public const uint DaclSecurityInformation = 0x00000004;
    public const uint SetAccess = 2;
    public const uint FileAttributeNormal = 0x00000080;
    public const uint FileShareRead = 0x00000001;
    public const uint FileShareWrite = 0x00000002;
    public const uint OpenExisting = 3;
    public const int SeKernelObject = 6;
    public const int SeWindowObject = 7;
    public const uint SecurityBuiltinDomainRid = 0x00000020;
    public const uint DomainAliasRidAdmins = 0x00000220;
    public const uint ErrorCancelled = 1223;
    public const uint Infinite = 0xFFFFFFFF;
    public const uint WaitObject0 = 0x00000000;
    public const uint WaitTimeout = 0x00000102;
    public const uint WaitFailed = 0xFFFFFFFF;
    public const int StartfUseStdHandles = 0x00000100;
    public const int LogonWithProfile = 0x00000001;
    public const int Logon32LogonInteractive = 2;
    public const int Logon32ProviderDefault = 0;
    public const int SeeMaskNoCloseProcess = 0x00000040;
    public const int SwHide = 0;
    public static readonly IntPtr ProcThreadAttributeHandleList = 0x00020002;

    [StructLayout(LayoutKind.Sequential)]
    public struct StartupInfo
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct StartupInfoEx
    {
        public StartupInfo StartupInfo;

        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessInformation
    {
        public IntPtr hProcess;

        public IntPtr hThread;

        public int dwProcessId;

        public int dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SidAndAttributes
    {
        public IntPtr Sid;

        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SecurityAttributes
    {
        public int nLength;

        public IntPtr lpSecurityDescriptor;

        [MarshalAs(UnmanagedType.Bool)]
        public bool bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Trustee
    {
        public IntPtr pMultipleTrustee;

        public int MultipleTrusteeOperation;

        public uint TrusteeForm;

        public uint TrusteeType;

        public IntPtr ptstrName;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ExplicitAccess
    {
        public uint grfAccessPermissions;

        public uint grfAccessMode;

        public uint grfInheritance;

        public Trustee Trustee;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TokenDefaultDaclInformation
    {
        public IntPtr DefaultDacl;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Luid
    {
        public uint LowPart;

        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LuidAndAttributes
    {
        public Luid Luid;

        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TokenPrivileges
    {
        public uint PrivilegeCount;

        public LuidAndAttributes Privileges;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct ShellExecuteInfo
    {
        public int cbSize;
        public int fMask;
        public IntPtr hwnd;
        public string? lpVerb;
        public string? lpFile;
        public string? lpParameters;
        public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool OpenProcessToken(
        IntPtr processHandle,
        uint desiredAccess,
        out SafeAccessTokenHandle tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool LogonUser(
        string username,
        string? domain,
        string password,
        int logonType,
        int logonProvider,
        out SafeAccessTokenHandle tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool CreateRestrictedToken(
        SafeAccessTokenHandle existingTokenHandle,
        uint flags,
        uint disableSidCount,
        IntPtr sidsToDisable,
        uint deletePrivilegeCount,
        IntPtr privilegesToDelete,
        uint restrictedSidCount,
        [In] SidAndAttributes[] sidsToRestrict,
        out SafeAccessTokenHandle newTokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool GetTokenInformation(
        SafeAccessTokenHandle tokenHandle,
        uint tokenInformationClass,
        IntPtr tokenInformation,
        uint tokenInformationLength,
        out uint returnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool SetTokenInformation(
        SafeAccessTokenHandle tokenHandle,
        uint tokenInformationClass,
        IntPtr tokenInformation,
        uint tokenInformationLength);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool LookupPrivilegeValue(
        string? systemName,
        string name,
        out Luid luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool AdjustTokenPrivileges(
        SafeAccessTokenHandle tokenHandle,
        bool disableAllPrivileges,
        ref TokenPrivileges newState,
        uint bufferLength,
        IntPtr previousState,
        IntPtr returnLength);

    [DllImport("advapi32.dll", EntryPoint = "SetEntriesInAclW", SetLastError = true)]
    public static extern uint SetEntriesInAcl(
        uint countOfExplicitEntries,
        [In] ExplicitAccess[] explicitEntries,
        IntPtr oldAcl,
        out IntPtr newAcl);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool ConvertStringSidToSid(
        string stringSid,
        out IntPtr sid);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr LocalFree(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool CreateProcessAsUser(
        SafeAccessTokenHandle hToken,
        string? lpApplicationName,
        StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref StartupInfoEx lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    [DllImport("advapi32.dll", EntryPoint = "CreateProcessAsUserW", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool CreateProcessAsUserBasic(
        SafeAccessTokenHandle hToken,
        string? lpApplicationName,
        StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref StartupInfo lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool CreateProcessWithTokenW(
        SafeAccessTokenHandle hToken,
        int dwLogonFlags,
        string? lpApplicationName,
        StringBuilder lpCommandLine,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref StartupInfo lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool CreateProcessWithLogonW(
        string username,
        string? domain,
        string password,
        int logonFlags,
        string? applicationName,
        StringBuilder commandLine,
        uint creationFlags,
        IntPtr environment,
        string? currentDirectory,
        ref StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CreatePipe(
        out SafeFileHandle hReadPipe,
        out SafeFileHandle hWritePipe,
        ref SecurityAttributes lpPipeAttributes,
        int nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetHandleInformation(
        SafeHandle hObject,
        uint dwMask,
        uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(
        IntPtr hHandle,
        uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetExitCodeProcess(
        IntPtr hProcess,
        out uint lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool TerminateProcess(
        IntPtr hProcess,
        uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern uint GetSecurityInfo(
        SafeHandle handle,
        int objectType,
        uint securityInfo,
        IntPtr ppsidOwner,
        IntPtr ppsidGroup,
        out IntPtr ppDacl,
        IntPtr ppSacl,
        out IntPtr ppSecurityDescriptor);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern uint SetSecurityInfo(
        SafeHandle handle,
        int objectType,
        uint securityInfo,
        IntPtr psidOwner,
        IntPtr psidGroup,
        IntPtr pDacl,
        IntPtr pSacl);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern uint SetSecurityInfo(
        IntPtr handle,
        int objectType,
        uint securityInfo,
        IntPtr psidOwner,
        IntPtr psidGroup,
        IntPtr pDacl,
        IntPtr pSacl);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateDesktopW")]
    public static extern IntPtr CreateDesktop(
        string lpszDesktop,
        string? lpszDevice,
        IntPtr pDevmode,
        uint dwFlags,
        uint dwDesiredAccess,
        IntPtr lpsa);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool InitializeProcThreadAttributeList(
        IntPtr lpAttributeList,
        int dwAttributeCount,
        int dwFlags,
        ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList,
        uint dwFlags,
        IntPtr attribute,
        IntPtr lpValue,
        IntPtr cbSize,
        IntPtr lpPreviousValue,
        IntPtr lpReturnSize);

    [DllImport("kernel32.dll")]
    public static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool ShellExecuteEx(ref ShellExecuteInfo lpExecInfo);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool AllocateAndInitializeSid(
        ref SidIdentifierAuthority pIdentifierAuthority,
        byte nSubAuthorityCount,
        uint nSubAuthority0,
        uint nSubAuthority1,
        uint nSubAuthority2,
        uint nSubAuthority3,
        uint nSubAuthority4,
        uint nSubAuthority5,
        uint nSubAuthority6,
        uint nSubAuthority7,
        out IntPtr pSid);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool CheckTokenMembership(
        IntPtr tokenHandle,
        IntPtr sidToCheck,
        out bool isMember);

    [DllImport("advapi32.dll")]
    public static extern IntPtr FreeSid(IntPtr pSid);

    [StructLayout(LayoutKind.Sequential)]
    public struct SidIdentifierAuthority
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] Value;
    }
}
