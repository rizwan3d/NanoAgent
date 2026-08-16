using System.Runtime.CompilerServices;
using StemCode.Application.Voice;

namespace StemCode.CLI;

internal sealed class VoiceInteractionState
{
    private static readonly ConditionalWeakTable<AppState, VoiceInteractionState> States = new();

    public IVoiceDictationService Service { get; set; } = VoiceDictationService.CreateDefault();

    public VoiceSettings? Settings { get; set; }

    public Task? Operation { get; set; }

    public CancellationTokenSource? Cancellation { get; set; }

    public bool IsBusy { get; set; }

    public VoiceProgressStage? ProgressStage { get; set; }

    public double? ProgressFraction { get; set; }

    public static VoiceInteractionState For(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return States.GetValue(state, static _ => new VoiceInteractionState());
    }
}
