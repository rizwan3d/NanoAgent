using Microsoft.Extensions.Logging;

namespace FinalAgent.Application.Logging;

internal static partial class ApplicationLogMessages
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Application runner started for product '{productName}'.")]
    public static partial void RunnerStarted(ILogger logger, string productName);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Existing onboarding detected for provider '{providerName}'.")]
    public static partial void ExistingOnboardingDetected(ILogger logger, string providerName);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Information,
        Message = "Onboarding completed for provider '{providerName}'.")]
    public static partial void OnboardingCompleted(ILogger logger, string providerName);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Warning,
        Message = "Stored onboarding data is incomplete. Re-running onboarding.")]
    public static partial void IncompleteOnboardingDetected(ILogger logger);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Information,
        Message = "Application runner completed successfully. Provider: '{providerName}'. Onboarded during this run: {wasOnboarded}.")]
    public static partial void RunnerCompleted(ILogger logger, string providerName, bool wasOnboarded);

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Information,
        Message = "Starting model discovery for provider '{providerName}'.")]
    public static partial void ModelDiscoveryStarted(ILogger logger, string providerName);

    [LoggerMessage(
        EventId = 1006,
        Level = LogLevel.Information,
        Message = "Model discovery selected '{modelId}' using '{selectionSource}'.")]
    public static partial void ModelDiscoveryCompleted(ILogger logger, string modelId, string selectionSource);

    [LoggerMessage(
        EventId = 1007,
        Level = LogLevel.Warning,
        Message = "Configured default model '{configuredModel}' was not returned by the provider. Falling back to the ranked preference list.")]
    public static partial void ConfiguredDefaultModelNotFound(ILogger logger, string configuredModel);

    [LoggerMessage(
        EventId = 1008,
        Level = LogLevel.Warning,
        Message = "The provider returned duplicate model identifiers. Using the de-duplicated sorted set for selection.")]
    public static partial void DuplicateModelsDetected(ILogger logger);

    [LoggerMessage(
        EventId = 1009,
        Level = LogLevel.Information,
        Message = "Interactive shell started for model '{modelId}'.")]
    public static partial void ReplStarted(ILogger logger, string modelId);

    [LoggerMessage(
        EventId = 1010,
        Level = LogLevel.Information,
        Message = "Interactive shell stopped.")]
    public static partial void ReplStopped(ILogger logger);

    [LoggerMessage(
        EventId = 1011,
        Level = LogLevel.Warning,
        Message = "REPL command '{commandText}' failed unexpectedly.")]
    public static partial void ReplCommandFailed(ILogger logger, string commandText, Exception exception);

    [LoggerMessage(
        EventId = 1012,
        Level = LogLevel.Warning,
        Message = "Conversation pipeline failed unexpectedly during the interactive shell.")]
    public static partial void ReplConversationFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1013,
        Level = LogLevel.Information,
        Message = "Interactive shell input stream closed. Exiting the shell.")]
    public static partial void ReplInputClosed(ILogger logger);
}
