namespace NanoAgent.CLI;

internal sealed class SlidingWindowRequestLimiter
{
    private readonly int _maxRequests;
    private readonly Queue<DateTime> _requestTimes = new();
    private readonly object _sync = new();
    private readonly TimeSpan _window;

    public SlidingWindowRequestLimiter(int maxRequests, TimeSpan window)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRequests, 1);
        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window));
        }

        _maxRequests = maxRequests;
        _window = window;
    }

    public bool TryAcquire()
    {
        DateTime now = DateTime.UtcNow;

        lock (_sync)
        {
            while (_requestTimes.Count > 0 &&
                now - _requestTimes.Peek() >= _window)
            {
                _requestTimes.Dequeue();
            }

            if (_requestTimes.Count >= _maxRequests)
            {
                return false;
            }

            _requestTimes.Enqueue(now);
            return true;
        }
    }
}
