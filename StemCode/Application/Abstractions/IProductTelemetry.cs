using StemCode.Application.Models;

namespace StemCode.Application.Abstractions;

public interface IProductTelemetry
{
    void TrackAppStarted();

    void TrackAppStopped();

    void TrackFeatureUsed(
        string featureName,
        string interactionKind,
        bool success,
        ConversationTurnMetrics? metrics = null,
        int attachmentCount = 0,
        Exception? exception = null);

    void TrackToolInvoked(
        string toolName,
        ToolResultStatus status,
        bool success,
        TimeSpan duration,
        ConversationExecutionPhase executionPhase,
        string? modelId = null,
        string? providerName = null,
        string? errorMessage = null);

    void TrackProviderRequest(
        string providerName,
        bool success,
        TimeSpan latency,
        bool streamed,
        TimeSpan streamLatency,
        int retryCount,
        string? errorMessage = null);
}
