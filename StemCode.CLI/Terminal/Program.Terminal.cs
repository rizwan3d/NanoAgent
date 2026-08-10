using System.Runtime.InteropServices;

namespace StemCode.CLI;

public static partial class Program
{
    private const int LeftShiftVirtualKey = 0xA0;
    private const int RightShiftVirtualKey = 0xA1;
    private const int LeftControlVirtualKey = 0xA2;
    private const int RightControlVirtualKey = 0xA3;
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

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
