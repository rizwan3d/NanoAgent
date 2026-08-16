namespace StemCode.Application.Voice;

public interface IVoiceDictationService
{
    Task<VoiceSettings?> LoadSettingsAsync(CancellationToken cancellationToken = default);

    Task SaveSettingsAsync(VoiceSettings settings, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VoiceModelOption>> GetModelsAsync(
        bool refresh = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VoiceInputDevice>> GetInputDevicesAsync(
        CancellationToken cancellationToken = default);

    Task EnsureModelAsync(
        string modelId,
        IProgress<VoiceProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<string> DictateAsync(
        VoiceSettings settings,
        IProgress<VoiceProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task UpdateModelsAsync(
        IProgress<VoiceProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
