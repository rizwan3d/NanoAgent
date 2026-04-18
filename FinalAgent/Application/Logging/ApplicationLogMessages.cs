using Microsoft.Extensions.Logging;

namespace FinalAgent.Application.Logging;

internal static partial class ApplicationLogMessages
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Application runner started for target '{targetName}' with {repeatCount} iteration(s).")]
    public static partial void RunnerStarted(ILogger logger, string targetName, int repeatCount);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Iteration {iteration}/{totalIterations}: {message}")]
    public static partial void GreetingPublished(ILogger logger, int iteration, int totalIterations, string message);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Information,
        Message = "Application runner completed successfully.")]
    public static partial void RunnerCompleted(ILogger logger);
}
