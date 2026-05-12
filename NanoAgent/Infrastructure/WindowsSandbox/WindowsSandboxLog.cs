namespace NanoAgent.Infrastructure.WindowsSandbox;

internal static class WindowsSandboxLog
{
    public static void Write(string nanoAgentHome, string message)
    {
        try
        {
            WindowsSandboxPaths.EnsureStateDirectories(nanoAgentHome);
            File.AppendAllText(
                WindowsSandboxPaths.SandboxLogPath(nanoAgentHome),
                $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
