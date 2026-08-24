using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace StemCode.Infrastructure.WindowsSandbox;

internal sealed class WindowsSandboxJobObject : IDisposable
{
    private SafeJobHandle? _handle;
    private bool _disposed;

    private WindowsSandboxJobObject(SafeJobHandle handle)
    {
        _handle = handle;
    }

    public static WindowsSandboxJobObject CreateKillOnClose()
    {
        SafeJobHandle handle = WindowsSandboxNative.CreateJobObject(IntPtr.Zero, null);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            WindowsSandboxNative.JobObjectExtendedLimitInformation limits = new()
            {
                BasicLimitInformation = new WindowsSandboxNative.JobObjectBasicLimitInformation
                {
                    LimitFlags = WindowsSandboxNative.JobObjectLimitKillOnJobClose
                }
            };

            int size = Marshal.SizeOf<WindowsSandboxNative.JobObjectExtendedLimitInformation>();
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(limits, buffer, fDeleteOld: false);
                if (!WindowsSandboxNative.SetInformationJobObject(
                        handle,
                        WindowsSandboxNative.JobObjectExtendedLimitInformationClass,
                        buffer,
                        (uint)size))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            return new WindowsSandboxJobObject(handle);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public void AssignProcess(IntPtr processHandle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!WindowsSandboxNative.AssignProcessToJobObject(_handle!, processHandle))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _handle?.Dispose();
        _handle = null;
    }
}

internal sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeJobHandle()
        : base(ownsHandle: true)
    {
    }

    protected override bool ReleaseHandle()
    {
        return WindowsSandboxNative.CloseHandle(handle);
    }
}
