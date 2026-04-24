using System.Runtime.InteropServices;

namespace NanoAgent.CLI;

public static partial class Program
{
    private const int LeftShiftVirtualKey = 0xA0;
    private const int RightShiftVirtualKey = 0xA1;
    private const int LeftControlVirtualKey = 0xA2;
    private const int RightControlVirtualKey = 0xA3;

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
        Console.Write(EnableBracketedPasteSequence);
        Console.Write(EnableWheelScrollingSequence);
    }

    private static void DisableTerminalWheelScrolling()
    {
        if (!Console.IsOutputRedirected)
        {
            Console.Write(DisableWheelScrollingSequence);
            Console.Write(DisableBracketedPasteSequence);
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

    private static bool IsControlKeyPressed()
    {
        return OperatingSystem.IsWindows() &&
            (IsVirtualKeyPressed(LeftControlVirtualKey) || IsVirtualKeyPressed(RightControlVirtualKey));
    }

    private static bool IsShiftKeyPressed()
    {
        return OperatingSystem.IsWindows() &&
            (IsVirtualKeyPressed(LeftShiftVirtualKey) || IsVirtualKeyPressed(RightShiftVirtualKey));
    }

    private static bool IsVirtualKeyPressed(int virtualKey)
    {
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
