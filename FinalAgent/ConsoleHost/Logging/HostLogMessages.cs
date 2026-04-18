using Microsoft.Extensions.Logging;

namespace FinalAgent.ConsoleHost.Logging;

internal static partial class HostLogMessages
{
    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Information,
        Message = "Host startup sequence has begun.")]
    public static partial void ApplicationStarting(ILogger logger);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "Application requested shutdown.")]
    public static partial void ApplicationStopping(ILogger logger);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Information,
        Message = "Host stopped with exit code {exitCode}.")]
    public static partial void ApplicationStopped(ILogger logger, int exitCode);

    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Information,
        Message = "Application finished with exit code {exitCode}.")]
    public static partial void RunCompleted(ILogger logger, int exitCode);

    [LoggerMessage(
        EventId = 2004,
        Level = LogLevel.Warning,
        Message = "Application execution was cancelled.")]
    public static partial void RunCancelled(ILogger logger);

    [LoggerMessage(
        EventId = 2005,
        Level = LogLevel.Error,
        Message = "Application execution failed unexpectedly.")]
    public static partial void RunFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 2006,
        Level = LogLevel.Error,
        Message = "Configuration validation failed: {validationMessage}")]
    public static partial void ConfigurationValidationFailed(ILogger logger, string validationMessage);

    [LoggerMessage(
        EventId = 2007,
        Level = LogLevel.Critical,
        Message = "Host terminated due to an unhandled exception.")]
    public static partial void UnhandledHostFailure(ILogger logger, Exception exception);
}
