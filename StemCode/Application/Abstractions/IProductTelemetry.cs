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
}
