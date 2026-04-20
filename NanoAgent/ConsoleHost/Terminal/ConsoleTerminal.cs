namespace NanoAgent.ConsoleHost.Terminal;

internal sealed class ConsoleTerminal : IConsoleTerminal
{
    public ConsoleColor BackgroundColor
    {
        get => Console.BackgroundColor;
        set => Console.BackgroundColor = value;
    }

    public int CursorLeft
    {
        get
        {
            try
            {
                return Console.CursorLeft;
            }
            catch (IOException)
            {
                return 0;
            }
        }
    }

    public int CursorTop => Console.CursorTop;

    public ConsoleColor ForegroundColor
    {
        get => Console.ForegroundColor;
        set => Console.ForegroundColor = value;
    }

    public bool IsInputRedirected => Console.IsInputRedirected;

    public bool IsOutputRedirected => Console.IsOutputRedirected;

    public bool KeyAvailable
    {
        get
        {
            try
            {
                return Console.KeyAvailable;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
        }
    }

    public int WindowHeight
    {
        get
        {
            try
            {
                return Console.WindowHeight;
            }
            catch (IOException)
            {
                return 24;
            }
        }
    }

    public int WindowWidth
    {
        get
        {
            try
            {
                return Console.WindowWidth;
            }
            catch (IOException)
            {
                return 80;
            }
        }
    }

    public ConsoleKeyInfo ReadKey(bool intercept)
    {
        return Console.ReadKey(intercept);
    }

    public string? ReadLine()
    {
        return Console.ReadLine();
    }

    public void ResetColor()
    {
        Console.ResetColor();
    }

    public void SetCursorPosition(int left, int top)
    {
        Console.SetCursorPosition(left, top);
    }

    public void Write(string value)
    {
        Console.Write(value);
    }

    public void WriteLine()
    {
        Console.WriteLine();
    }

    public void WriteLine(string value)
    {
        Console.WriteLine(value);
    }
}
