namespace NanoAgent.Presentation.Cli.Terminal;

internal sealed class ConsoleInteractionGate : IConsoleInteractionGate
{
    private readonly object _syncRoot = new();

    public IDisposable EnterScope()
    {
        Monitor.Enter(_syncRoot);
        return new Releaser(_syncRoot);
    }

    private sealed class Releaser : IDisposable
    {
        private object? _syncRoot;

        public Releaser(object syncRoot)
        {
            _syncRoot = syncRoot;
        }

        public void Dispose()
        {
            object? syncRoot = Interlocked.Exchange(ref _syncRoot, null);
            if (syncRoot is not null)
            {
                Monitor.Exit(syncRoot);
            }
        }
    }
}
