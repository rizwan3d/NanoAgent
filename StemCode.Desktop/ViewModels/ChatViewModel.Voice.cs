using StemCode.Application.Voice;
using StemCode.Desktop.Models;

namespace StemCode.Desktop.ViewModels;

public partial class ChatViewModel
{
    private readonly IVoiceDictationService _voiceDictationService = VoiceDictationService.CreateDefault();
    private bool _isVoiceDictating;

    public bool IsVoiceDictating
    {
        get => _isVoiceDictating;
        private set => SetProperty(ref _isVoiceDictating, value);
    }

    public async Task<bool> TryHandleVoiceCommandAsync()
    {
        string command = Prompt.Trim();
        if (!command.Equals("/voice", StringComparison.OrdinalIgnoreCase) &&
            !command.StartsWith("/voice ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        Prompt = string.Empty;
        if (command.Equals("/voice", StringComparison.OrdinalIgnoreCase))
        {
            await DictateVoiceAsync();
            return true;
        }

        if (command.Equals("/voice setup", StringComparison.OrdinalIgnoreCase))
        {
            VoiceSettings? settings = await ConfigureVoiceAsync();
            if (settings is not null)
            {
                Messages.Add(new ChatMessage("StemCode", "Voice setup saved."));
            }
            return true;
        }

        if (command.Equals("/voice update", StringComparison.OrdinalIgnoreCase))
        {
            await UpdateVoiceModelsAsync();
            return true;
        }

        Messages.Add(new ChatMessage("StemCode", "Usage: /voice, /voice setup, or /voice update"));
        return true;
    }

    public async Task DictateVoiceAsync()
    {
        if (IsVoiceDictating || IsPromptRunning)
        {
            return;
        }

        IsVoiceDictating = true;
        try
        {
            VoiceSettings? settings = await _voiceDictationService.LoadSettingsAsync();
            if (settings is null || !settings.IsConfigured)
            {
                settings = await ConfigureVoiceAsync();
                if (settings is null)
                {
                    return;
                }
            }

            Progress<VoiceProgress> progress = new(value =>
            {
                ProgressText = FormatDesktopVoiceProgress(value);
            });

            await _voiceDictationService.EnsureModelAsync(settings.ModelId, progress);
            string transcript = await _voiceDictationService.DictateAsync(settings, progress);
            Prompt = AppendVoiceTranscript(Prompt, transcript);
            Messages.Add(new ChatMessage("StemCode", "Voice dictation added to input."));
        }
        catch (Exception exception)
        {
            Messages.Add(new ChatMessage("StemCode", $"Voice error: {exception.Message}"));
        }
        finally
        {
            IsVoiceDictating = false;
            if (!IsPromptRunning)
            {
                ProgressText = "(0s · 0 tokens)";
            }
        }
    }

    public async Task UpdateVoiceModelsAsync()
    {
        if (IsVoiceDictating || IsPromptRunning)
        {
            return;
        }

        IsVoiceDictating = true;
        try
        {
            Progress<VoiceProgress> progress = new(value =>
            {
                ProgressText = FormatDesktopVoiceProgress(value);
            });
            await _voiceDictationService.UpdateModelsAsync(progress);
            _ = await _voiceDictationService.GetModelsAsync(refresh: true);
            Messages.Add(new ChatMessage("StemCode", "Voice models are up to date."));
        }
        catch (Exception exception)
        {
            Messages.Add(new ChatMessage("StemCode", $"Voice update error: {exception.Message}"));
        }
        finally
        {
            IsVoiceDictating = false;
            if (!IsPromptRunning)
            {
                ProgressText = "(0s · 0 tokens)";
            }
        }
    }

    private async Task<VoiceSettings?> ConfigureVoiceAsync()
    {
        VoiceSettings? saved = await _voiceDictationService.LoadSettingsAsync();
        IReadOnlyList<VoiceModelOption> models = await _voiceDictationService.GetModelsAsync();
        if (models.Count == 0)
        {
            Messages.Add(new ChatMessage("StemCode", "No voice models are available."));
            return null;
        }

        int modelDefault = FindDesktopVoiceModelDefault(models, saved);
        int? modelIndex = await ShowVoiceSelectionAsync(
            "Voice model",
            "Choose the local speech model used for dictation.",
            models.Select(model => new DesktopSelectionPromptOptionDescriptor(
                model.Label,
                model.Description)).ToArray(),
            modelDefault);
        if (modelIndex is null)
        {
            return null;
        }

        VoiceModelOption selectedModel = models[modelIndex.Value];
        IReadOnlyList<VoiceInputDevice> devices = await _voiceDictationService.GetInputDevicesAsync();
        string? inputDeviceId = saved?.InputDeviceId;
        if (devices.Count > 1)
        {
            int deviceDefault = FindDesktopVoiceDeviceDefault(devices, saved);
            int? deviceIndex = await ShowVoiceSelectionAsync(
                "Voice microphone",
                "Multiple microphones were found. Choose the one to use for dictation.",
                devices.Select(device => new DesktopSelectionPromptOptionDescriptor(
                    device.Name,
                    device.IsDefault ? "System default microphone." : "Audio input device.")).ToArray(),
                deviceDefault);
            if (deviceIndex is null)
            {
                return null;
            }

            VoiceInputDevice selectedDevice = devices[deviceIndex.Value];
            inputDeviceId = string.IsNullOrWhiteSpace(selectedDevice.Id) ? null : selectedDevice.Id;
        }
        else if (devices.Count == 1)
        {
            inputDeviceId = string.IsNullOrWhiteSpace(devices[0].Id) ? null : devices[0].Id;
        }

        VoiceSettings settings = new(selectedModel.Id, inputDeviceId);
        await _voiceDictationService.SaveSettingsAsync(settings);
        return settings;
    }

    private async Task<int?> ShowVoiceSelectionAsync(
        string title,
        string description,
        IReadOnlyList<DesktopSelectionPromptOptionDescriptor> options,
        int defaultIndex)
    {
        TaskCompletionSource<int?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ActiveSelectionPrompt = new DesktopSelectionPrompt(
            title,
            description,
            options,
            defaultIndex,
            allowCancellation: true,
            autoSelectAfter: null,
            onSelected: (index, _) =>
            {
                ActiveSelectionPrompt = null;
                completion.TrySetResult(index);
            },
            onCancelled: () =>
            {
                ActiveSelectionPrompt = null;
                completion.TrySetResult(null);
            });

        return await completion.Task;
    }

    private static int FindDesktopVoiceModelDefault(
        IReadOnlyList<VoiceModelOption> models,
        VoiceSettings? settings)
    {
        if (!string.IsNullOrWhiteSpace(settings?.ModelId))
        {
            for (int index = 0; index < models.Count; index++)
            {
                if (string.Equals(models[index].Id, settings.ModelId, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }
        }

        for (int index = 0; index < models.Count; index++)
        {
            if (models[index].IsRecommended)
            {
                return index;
            }
        }

        return 0;
    }

    private static int FindDesktopVoiceDeviceDefault(
        IReadOnlyList<VoiceInputDevice> devices,
        VoiceSettings? settings)
    {
        if (!string.IsNullOrWhiteSpace(settings?.InputDeviceId))
        {
            for (int index = 0; index < devices.Count; index++)
            {
                if (string.Equals(devices[index].Id, settings.InputDeviceId, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }
        }

        for (int index = 0; index < devices.Count; index++)
        {
            if (devices[index].IsDefault)
            {
                return index;
            }
        }

        return 0;
    }

    private static string AppendVoiceTranscript(string current, string transcript)
    {
        string normalized = transcript.Trim();
        if (string.IsNullOrWhiteSpace(current))
        {
            return normalized;
        }

        return char.IsWhiteSpace(current[^1])
            ? current + normalized
            : current + " " + normalized;
    }

    private static string FormatDesktopVoiceProgress(VoiceProgress progress)
    {
        string label = progress.Stage switch
        {
            VoiceProgressStage.Downloading => "downloading voice model",
            VoiceProgressStage.Recording => "listening",
            VoiceProgressStage.Transcribing => "transcribing voice",
            VoiceProgressStage.Updating => "updating voice models",
            _ => "preparing voice"
        };

        if (progress.Fraction is not double fraction)
        {
            return $"({label})";
        }

        int percentage = (int)Math.Round(Math.Clamp(fraction, 0d, 1d) * 100d);
        return $"({label} · {percentage}%)";
    }
}
