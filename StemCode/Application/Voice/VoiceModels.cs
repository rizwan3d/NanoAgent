namespace StemCode.Application.Voice;

public enum VoiceProgressStage
{
    Discovering,
    Downloading,
    Recording,
    Transcribing,
    Updating
}

public sealed record VoiceProgress(
    VoiceProgressStage Stage,
    double? Fraction = null,
    string? Message = null);

public sealed record VoiceModelOption(
    string Id,
    string Label,
    string Description,
    bool IsRecommended = false);

public sealed record VoiceInputDevice(
    string Id,
    string Name,
    bool IsDefault = false);

public sealed record VoiceSettings(
    string ModelId,
    string? InputDeviceId)
{
    public const string DefaultModelId = "balanced";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ModelId);

    public static VoiceSettings Default { get; } = new(DefaultModelId, null);
}
