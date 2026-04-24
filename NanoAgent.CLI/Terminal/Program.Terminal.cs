using System.Runtime.InteropServices;

namespace NanoAgent.CLI;

public static partial class Program
{
    private static void EnableTerminalWheelScrolling()
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            TryEnableVirtualTerminalInput();
        }

        Console.Write(DisableMouseTrackingSequence);
        Console.Write(EnableAlternateScreenSequence);
        Console.Write(EnableWheelScrollingSequence);
    }

    private static void DisableTerminalWheelScrolling()
    {
        if (!Console.IsOutputRedirected)
        {
            Console.Write(DisableWheelScrollingSequence);
            Console.Write(DisableMouseTrackingSequence);
            Console.Write(DisableAlternateScreenSequence);
        }

        if (OperatingSystem.IsWindows())
        {
            TryRestoreInputMode();
        }
    }

    private static void TryEnableVirtualTerminalInput()
    {
        IntPtr inputHandle = GetStdHandle(StdInputHandle);
        if (inputHandle == IntPtr.Zero || inputHandle == new IntPtr(-1))
        {
            return;
        }

        if (!GetConsoleMode(inputHandle, out uint mode))
        {
            return;
        }

        s_originalInputMode ??= mode;
        SetConsoleMode(inputHandle, mode | EnableVirtualTerminalInput);
    }

    private static void TryRestoreInputMode()
    {
        if (s_originalInputMode is null)
        {
            return;
        }

        IntPtr inputHandle = GetStdHandle(StdInputHandle);
        if (inputHandle != IntPtr.Zero && inputHandle != new IntPtr(-1))
        {
            SetConsoleMode(inputHandle, s_originalInputMode.Value);
        }

        s_originalInputMode = null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
}
