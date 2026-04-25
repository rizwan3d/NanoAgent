using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Utilities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NanoAgent.Infrastructure.Logging;

internal sealed class DailyFileLoggerProvider : ILoggerProvider
{
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ConcurrentDictionary<string, DailyFileLogger> _loggers = new(StringComparer.Ordinal);
    private readonly IUserDataPathProvider _pathProvider;
    private readonly TimeProvider _timeProvider;
    private readonly object _writeLock = new();
    private bool _disposed;

    public DailyFileLoggerProvider(
        IUserDataPathProvider pathProvider,
        IHostEnvironment hostEnvironment,
        TimeProvider timeProvider)
    {
        _pathProvider = pathProvider;
        _hostEnvironment = hostEnvironment;
        _timeProvider = timeProvider;
    }

    public ILogger CreateLogger(string categoryName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryName);

        return _loggers.GetOrAdd(
            categoryName.Trim(),
            name => new DailyFileLogger(name, this));
    }

    public void Dispose()
    {
        _disposed = true;
        _loggers.Clear();
    }

    internal void WriteLog(
        LogLevel logLevel,
        EventId eventId,
        string categoryName,
        string message,
        Exception? exception)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            DateTimeOffset now = _timeProvider.GetLocalNow();
            string logsDirectory = ResolveLogsDirectoryPath();
            Directory.CreateDirectory(logsDirectory);

            string logFilePath = Path.Combine(
                logsDirectory,
                $"{now:yyyy-MM-dd}.log");

            string logEntry = BuildLogEntry(
                now,
                logLevel,
                eventId,
                categoryName,
                message,
                exception);

            lock (_writeLock)
            {
                File.AppendAllText(
                    logFilePath,
                    logEntry,
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never take down the interactive agent.
        }
    }

    private string ResolveLogsDirectoryPath()
    {
        try
        {
            string configuredLogsDirectory = _pathProvider.GetLogsDirectoryPath();
            if (!string.IsNullOrWhiteSpace(configuredLogsDirectory))
            {
                return configuredLogsDirectory;
            }
        }
        catch
        {
        }

        string applicationName = SanitizePathSegment(_hostEnvironment.ApplicationName);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            localAppData = string.IsNullOrWhiteSpace(userProfile)
                ? AppContext.BaseDirectory
                : Path.Combine(userProfile, ".local", "share");
        }

        return Path.Combine(
            localAppData,
            applicationName,
            "logs");
    }

    private static string BuildLogEntry(
        DateTimeOffset timestamp,
        LogLevel logLevel,
        EventId eventId,
        string categoryName,
        string message,
        Exception? exception)
    {
        StringBuilder builder = new();
        builder
            .Append(timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
            .Append(' ')
            .Append(GetLevelLabel(logLevel))
            .Append(": ")
            .Append(categoryName);

        if (eventId.Id != 0)
        {
            builder
                .Append('[')
                .Append(eventId.Id)
                .Append(']');
        }

        if (!string.IsNullOrWhiteSpace(message))
        {
            builder
                .Append(' ')
                .Append(SecretRedactor.Redact(message.Trim()));
        }

        builder.AppendLine();

        if (exception is not null)
        {
            builder
                .AppendLine(SecretRedactor.Redact(exception.ToString()));
        }

        return builder.ToString();
    }

    private static string GetLevelLabel(LogLevel logLevel)
    {
        return logLevel switch
        {
            LogLevel.Trace => "trce",
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            LogLevel.Critical => "crit",
            _ => "none"
        };
    }

    private static string SanitizePathSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "NanoAgent";
        }

        StringBuilder builder = new(value.Length);
        foreach (char character in value)
        {
            builder.Append(
                Path.GetInvalidFileNameChars().Contains(character)
                    ? '_'
                    : character);
        }

        string sanitized = builder
            .ToString()
            .Trim();

        return string.IsNullOrWhiteSpace(sanitized)
            ? "NanoAgent"
            : sanitized;
    }
}
