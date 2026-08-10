namespace StemCode.Infrastructure.WindowsSandbox;

internal static class WindowsSandboxLog
{
    public static void Write(string stemCodeHome, string message)
    {
        try
        {
            WindowsSandboxPaths.EnsureStateDirectories(stemCodeHome);
            File.AppendAllText(
                WindowsSandboxPaths.SandboxLogPath(stemCodeHome),
                $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
