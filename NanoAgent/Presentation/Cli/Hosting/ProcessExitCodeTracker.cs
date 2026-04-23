using System.Threading;

namespace NanoAgent.Presentation.Cli.Hosting;

public sealed class ProcessExitCodeTracker
{
    private int _exitCode = ExitCodes.Success;

    public int ExitCode => Volatile.Read(ref _exitCode);

    public void Set(int exitCode)
    {
        Interlocked.Exchange(ref _exitCode, exitCode);
    }
}
