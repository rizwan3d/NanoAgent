using System.Runtime.InteropServices;

namespace NanoAgent.CLI;

internal sealed class TerminalSession : IDisposable
{
    private const string ClearScreenSequence = "\u001b[2J\u001b[H";
    private const string EnableAlternateScreenSequence = "\u001b[?1049h";
    private const string DisableAlternateScreenSequence = "\u001b[?1049l";
    private const string EnableBracketedPasteSequence = "\u001b[?2004h";
    private const string DisableBracketedPasteSequence = "\u001b[?2004l";
    private const string DisableWheelScrollingSequence = "\u001b[?1007l";
    private const string SetBlackBackgroundSequence = "\u001b]11;rgb:0000/0000/0000\u001b\\";
    private const string ResetBackgroundSequence = "\u001b]111\u001b\\";
    // Any-event tracking (?1003h) plus SGR extended coordinates (?1006h): reports
    // clicks, hover/motion, and wheel events with row/column so selection modals can
    // follow the pointer in real time. This captures the mouse, so native drag-select
    // needs Shift+drag (or Reader View / Copy mode) -- an accepted trade-off for
    // richer in-app mouse interaction.
    private const string EnableMouseTrackingSequence = "\u001b[?1003h\u001b[?1006h";
    private const string DisableMouseTrackingSequence = "\u001b[?1000l\u001b[?1002l\u001b[?1003l\u001b[?1006l";
    private const int StdInputHandle = -10;
    private const uint EnableVirtualTerminalInput = 0x0200;
    private static uint? s_originalInputMode;

    private readonly bool _restoreCursorVisibility;
    private readonly bool _restoreTerminalState;
    private bool _disposed;

    private TerminalSession(bool restoreCursorVisibility, bool restoreTerminalState)
    {
        _restoreCursorVisibility = restoreCursorVisibility;
        _restoreTerminalState = restoreTerminalState;
    }

    public static TerminalSession EnterInteractiveMode()
    {
        bool restoreCursorVisibility = false;

        try
        {
            Console.CursorVisible = false;
            restoreCursorVisibility = true;
        }
        catch
        {
            restoreCursorVisibility = false;
        }

        bool restoreTerminalState = false;
        if (!Console.IsInputRedirected && !Console.IsOutputRedirected)
        {
            if (OperatingSystem.IsWindows())
            {
                TryEnableVirtualTerminalInput();
            }

            Console.Write(DisableMouseTrackingSequence);
            Console.Write(SetBlackBackgroundSequence);
            Console.Write(EnableAlternateScreenSequence);
            Console.Write(ClearScreenSequence);
            Console.Write(EnableBracketedPasteSequence);
            // Full button tracking reports wheel events as SGR codes too, so we no longer need
            // the alternate-scroll mode (?1007h) -- enabling both would double-count the wheel.
            Console.Write(EnableMouseTrackingSequence);
            restoreTerminalState = true;
        }

        return new TerminalSession(restoreCursorVisibility, restoreTerminalState);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_restoreTerminalState && !Console.IsOutputRedirected)
        {
            Console.Write(DisableWheelScrollingSequence);
            Console.Write(DisableBracketedPasteSequence);
            Console.Write(DisableMouseTrackingSequence);
            Console.Write(DisableAlternateScreenSequence);
            Console.Write(ResetBackgroundSequence);
        }

        if (OperatingSystem.IsWindows())
        {
            TryRestoreInputMode();
        }

        Console.ResetColor();

        if (_restoreCursorVisibility)
        {
            try
            {
                Console.CursorVisible = true;
            }
            catch
            {
            }
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
