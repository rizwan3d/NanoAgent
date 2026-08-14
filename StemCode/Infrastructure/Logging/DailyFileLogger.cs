using Microsoft.Extensions.Logging;

namespace StemCode.Infrastructure.Logging;

internal sealed class DailyFileLogger : ILogger
{
    private readonly string _categoryName;
    private readonly DailyFileLoggerProvider _provider;

    public DailyFileLogger(
        string categoryName,
        DailyFileLoggerProvider provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryName);
        ArgumentNullException.ThrowIfNull(provider);

        _categoryName = categoryName.Trim();
        _provider = provider;
    }

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
    {
        return NullScope.Instance;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel != LogLevel.None;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        if (!IsEnabled(logLevel))
        {
            return;
        }

        string message = formatter(state, exception);
        if (string.IsNullOrWhiteSpace(message) && exception is null)
        {
            return;
        }

        _provider.WriteLog(
            logLevel,
            eventId,
            _categoryName,
            message,
            exception);
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
