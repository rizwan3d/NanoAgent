namespace NanoAgent;

internal sealed class OpenAiCompatibleProgressTracker
{
    private readonly object _gate = new();
    private int _exactCompletedTokens;
    private int? _currentEstimatedTokens;
    private bool _currentRequestExact;

    public int? DisplayTokens
    {
        get
        {
            lock (_gate)
            {
                int total = _exactCompletedTokens + (_currentEstimatedTokens ?? 0);
                return total > 0 ? total : null;
            }
        }
    }

    public bool IsEstimate
    {
        get
        {
            lock (_gate)
            {
                return _currentEstimatedTokens.HasValue && !_currentRequestExact;
            }
        }
    }

    public bool CurrentRequestIsExact
    {
        get
        {
            lock (_gate)
            {
                return _currentRequestExact;
            }
        }
    }

    public void BeginRequest()
    {
        lock (_gate)
        {
            _currentEstimatedTokens = null;
            _currentRequestExact = false;
        }
    }

    public void UpdateCurrentEstimate(int estimatedTokens)
    {
        lock (_gate)
        {
            if (_currentRequestExact)
            {
                return;
            }

            _currentEstimatedTokens = Math.Max(estimatedTokens, 1);
        }
    }

    public void CompleteRequestWithExactTokens(int exactTokens)
    {
        lock (_gate)
        {
            _exactCompletedTokens += Math.Max(exactTokens, 0);
            _currentEstimatedTokens = null;
            _currentRequestExact = true;
        }
    }
}
