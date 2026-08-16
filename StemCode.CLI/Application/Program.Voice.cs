using StemCode.Application.Models;
using StemCode.Application.Voice;

namespace StemCode.CLI;

public static partial class Program
{
    internal static bool IsVoiceDictationKey(ConsoleKeyInfo key)
    {
        return (key.Key == ConsoleKey.R && key.Modifiers.HasFlag(ConsoleModifiers.Control)) ||
            key.KeyChar == '\u0012';
    }

    internal static bool IsVoiceOperationActive(AppState state)
    {
        return VoiceInteractionState.For(state).IsBusy;
    }

    internal static bool TryHandleVoiceInputCommand(AppState state)
    {
        if (state.InputAttachments.Count > 0)
        {
            return false;
        }

        string command = state.Input.ToString().Trim();
        if (!command.Equals("/voice", StringComparison.OrdinalIgnoreCase) &&
            !command.StartsWith("/voice ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ClearSubmittedInput(state);
        HandleVoiceCommand(state, command);
        return true;
    }

    internal static void StartVoiceDictation(AppState state)
    {
        if (!CanStartVoiceOperation(state))
        {
            return;
        }

        VoiceInteractionState voice = VoiceInteractionState.For(state);
        voice.IsBusy = true;
        state.ActivityText = "Preparing voice dictation";
        voice.Cancellation?.Dispose();
        voice.Cancellation = CancellationTokenSource.CreateLinkedTokenSource(state.LifetimeCancellation.Token);
        voice.Operation = Task.Run(async () =>
        {
            try
            {
                VoiceSettings? settings = await voice.Service.LoadSettingsAsync(voice.Cancellation.Token);
                state.UiBridge.Enqueue(appState =>
                {
                    FinishVoiceOperation(appState, restoreActivity: false);
                    if (settings is null || !settings.IsConfigured)
                    {
                        StartVoiceSetup(appState, startDictationAfterSetup: true);
                        return;
                    }

                    VoiceInteractionState.For(appState).Settings = settings;
                    StartVoiceCapture(appState, settings);
                });
            }
            catch (OperationCanceledException) when (voice.Cancellation?.IsCancellationRequested == true)
            {
                QueueVoiceCancelled(state);
            }
            catch (Exception exception)
            {
                QueueVoiceFailure(state, exception);
            }
        });
    }

    internal static int GetDefaultVoiceModelIndex(
        IReadOnlyList<VoiceModelOption> models,
        VoiceSettings? settings)
    {
        if (models.Count == 0)
        {
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(settings?.ModelId))
        {
            int savedIndex = FindIndex(models, model =>
                string.Equals(model.Id, settings.ModelId, StringComparison.OrdinalIgnoreCase));
            if (savedIndex >= 0)
            {
                return savedIndex;
            }
        }

        int recommendedIndex = FindIndex(models, static model => model.IsRecommended);
        if (recommendedIndex >= 0)
        {
            return recommendedIndex;
        }

        int defaultIndex = FindIndex(models, model =>
            string.Equals(model.Id, VoiceSettings.DefaultModelId, StringComparison.OrdinalIgnoreCase));
        return defaultIndex >= 0 ? defaultIndex : 0;
    }

    private static void HandleVoiceCommand(AppState state, string command)
    {
        string normalized = command.Trim();
        if (normalized.Equals("/voice", StringComparison.OrdinalIgnoreCase))
        {
            StartVoiceDictation(state);
            return;
        }

        if (normalized.Equals("/voice setup", StringComparison.OrdinalIgnoreCase))
        {
            StartVoiceSetup(state, startDictationAfterSetup: false);
            return;
        }

        if (normalized.Equals("/voice update", StringComparison.OrdinalIgnoreCase))
        {
            StartVoiceModelUpdate(state);
            return;
        }

        state.AddSystemMessage("Usage: /voice, /voice setup, or /voice update");
    }

    private static void StartVoiceSetup(AppState state, bool startDictationAfterSetup)
    {
        if (!CanStartVoiceOperation(state))
        {
            return;
        }

        VoiceInteractionState voice = VoiceInteractionState.For(state);
        voice.IsBusy = true;
        state.ActivityText = "Loading voice setup";
        voice.Cancellation?.Dispose();
        voice.Cancellation = CancellationTokenSource.CreateLinkedTokenSource(state.LifetimeCancellation.Token);
        voice.Operation = Task.Run(async () =>
        {
            try
            {
                VoiceSettings? savedSettings = await voice.Service.LoadSettingsAsync(voice.Cancellation.Token);
                IReadOnlyList<VoiceModelOption> models = await voice.Service.GetModelsAsync(
                    refresh: false,
                    voice.Cancellation.Token);
                IReadOnlyList<VoiceInputDevice> devices = await voice.Service.GetInputDevicesAsync(
                    voice.Cancellation.Token);

                state.UiBridge.Enqueue(appState =>
                {
                    FinishVoiceOperation(appState);
                    ShowVoiceModelSelection(
                        appState,
                        models,
                        devices,
                        savedSettings,
                        startDictationAfterSetup);
                });
            }
            catch (OperationCanceledException) when (voice.Cancellation?.IsCancellationRequested == true)
            {
                QueueVoiceCancelled(state);
            }
            catch (Exception exception)
            {
                QueueVoiceFailure(state, exception);
            }
        });
    }

    private static void ShowVoiceModelSelection(
        AppState state,
        IReadOnlyList<VoiceModelOption> models,
        IReadOnlyList<VoiceInputDevice> devices,
        VoiceSettings? savedSettings,
        bool startDictationAfterSetup)
    {
        if (models.Count == 0)
        {
            state.AddSystemMessage("No voice models are available.");
            return;
        }

        SelectionPromptOption<VoiceModelOption>[] options = models
            .Select(model => new SelectionPromptOption<VoiceModelOption>(
                model.Label,
                model,
                model.Description))
            .ToArray();

        state.ActiveModal = SelectionModalState<VoiceModelOption>.Create(
            new SelectionPromptRequest<VoiceModelOption>(
                "Voice model",
                options,
                "Choose the local speech model used for dictation.",
                DefaultIndex: GetDefaultVoiceModelIndex(models, savedSettings),
                AllowCancellation: true),
            completionToken: new object(),
            onSelected: model => ShowVoiceDeviceSelection(
                state,
                model,
                devices,
                savedSettings,
                startDictationAfterSetup),
            onCancelled: _ => state.AddSystemMessage("Voice setup cancelled."));
    }

    private static void ShowVoiceDeviceSelection(
        AppState state,
        VoiceModelOption model,
        IReadOnlyList<VoiceInputDevice> devices,
        VoiceSettings? savedSettings,
        bool startDictationAfterSetup)
    {
        if (devices.Count <= 1)
        {
            string? inputDeviceId = devices.Count == 1 && !string.IsNullOrWhiteSpace(devices[0].Id)
                ? devices[0].Id
                : null;
            SaveVoiceSetup(
                state,
                new VoiceSettings(model.Id, inputDeviceId),
                startDictationAfterSetup);
            return;
        }

        int defaultIndex = FindIndex(devices, device =>
            !string.IsNullOrWhiteSpace(savedSettings?.InputDeviceId) &&
            string.Equals(device.Id, savedSettings.InputDeviceId, StringComparison.OrdinalIgnoreCase));
        if (defaultIndex < 0)
        {
            defaultIndex = FindIndex(devices, static device => device.IsDefault);
        }
        if (defaultIndex < 0)
        {
            defaultIndex = 0;
        }

        SelectionPromptOption<VoiceInputDevice>[] options = devices
            .Select(device => new SelectionPromptOption<VoiceInputDevice>(
                device.Name,
                device,
                device.IsDefault ? "System default microphone." : "Audio input device."))
            .ToArray();

        state.ActiveModal = SelectionModalState<VoiceInputDevice>.Create(
            new SelectionPromptRequest<VoiceInputDevice>(
                "Voice microphone",
                options,
                "Multiple microphones were found. Choose the one to use for dictation.",
                DefaultIndex: defaultIndex,
                AllowCancellation: true),
            completionToken: new object(),
            onSelected: device => SaveVoiceSetup(
                state,
                new VoiceSettings(model.Id, string.IsNullOrWhiteSpace(device.Id) ? null : device.Id),
                startDictationAfterSetup),
            onCancelled: _ => state.AddSystemMessage("Voice setup cancelled."));
    }

    private static void SaveVoiceSetup(
        AppState state,
        VoiceSettings settings,
        bool startDictationAfterSetup)
    {
        VoiceInteractionState voice = VoiceInteractionState.For(state);
        voice.IsBusy = true;
        state.ActivityText = "Saving voice setup";
        voice.Operation = Task.Run(async () =>
        {
            try
            {
                await voice.Service.SaveSettingsAsync(settings, state.LifetimeCancellation.Token);
                state.UiBridge.Enqueue(appState =>
                {
                    VoiceInteractionState.For(appState).Settings = settings;
                    FinishVoiceOperation(appState);
                    appState.AddSystemMessage("Voice setup saved.");
                    if (startDictationAfterSetup)
                    {
                        StartVoiceCapture(appState, settings);
                    }
                });
            }
            catch (Exception exception)
            {
                QueueVoiceFailure(state, exception);
            }
        });
    }

    private static void StartVoiceCapture(AppState state, VoiceSettings settings)
    {
        if (!CanStartVoiceOperation(state))
        {
            return;
        }

        VoiceInteractionState voice = VoiceInteractionState.For(state);
        voice.Cancellation?.Dispose();
        voice.Cancellation = CancellationTokenSource.CreateLinkedTokenSource(state.LifetimeCancellation.Token);
        voice.IsBusy = true;
        state.ActivityText = "Preparing voice model";
        IProgress<VoiceProgress> progress = CreateVoiceProgress(state);
        voice.Operation = Task.Run(async () =>
        {
            try
            {
                await voice.Service.EnsureModelAsync(settings.ModelId, progress, voice.Cancellation.Token);
                string transcript = await voice.Service.DictateAsync(settings, progress, voice.Cancellation.Token);
                state.UiBridge.Enqueue(appState =>
                {
                    InsertInputText(appState, transcript);
                    FinishVoiceOperation(appState);
                    appState.AddSystemMessage("Voice dictation added to input.");
                });
            }
            catch (OperationCanceledException) when (voice.Cancellation.IsCancellationRequested)
            {
                state.UiBridge.Enqueue(appState =>
                {
                    FinishVoiceOperation(appState);
                    appState.AddSystemMessage("Voice dictation cancelled.");
                });
            }
            catch (Exception exception)
            {
                QueueVoiceFailure(state, exception);
            }
        });
    }

    private static void StartVoiceModelUpdate(AppState state)
    {
        if (!CanStartVoiceOperation(state))
        {
            return;
        }

        VoiceInteractionState voice = VoiceInteractionState.For(state);
        voice.IsBusy = true;
        state.ActivityText = "Checking voice model updates";
        voice.Cancellation?.Dispose();
        voice.Cancellation = CancellationTokenSource.CreateLinkedTokenSource(state.LifetimeCancellation.Token);
        IProgress<VoiceProgress> progress = CreateVoiceProgress(state);
        voice.Operation = Task.Run(async () =>
        {
            try
            {
                await voice.Service.UpdateModelsAsync(progress, voice.Cancellation.Token);
                _ = await voice.Service.GetModelsAsync(refresh: true, state.LifetimeCancellation.Token);
                state.UiBridge.Enqueue(appState =>
                {
                    FinishVoiceOperation(appState);
                    appState.AddSystemMessage("Voice models are up to date.");
                });
            }
            catch (OperationCanceledException) when (voice.Cancellation?.IsCancellationRequested == true)
            {
                QueueVoiceCancelled(state);
            }
            catch (Exception exception)
            {
                QueueVoiceFailure(state, exception);
            }
        });
    }

    private static bool CanStartVoiceOperation(AppState state)
    {
        if (state.ActiveModal is not null)
        {
            return false;
        }

        if (state.IsBusy || state.IsStreaming)
        {
            state.AddSystemMessage("Voice dictation is unavailable while StemCode is working.");
            return false;
        }

        if (VoiceInteractionState.For(state).IsBusy)
        {
            state.AddSystemMessage("A voice operation is already in progress.");
            return false;
        }

        return true;
    }

    private static IProgress<VoiceProgress> CreateVoiceProgress(AppState state)
    {
        return new Progress<VoiceProgress>(progress =>
        {
            state.UiBridge.Enqueue(appState =>
            {
                VoiceInteractionState voice = VoiceInteractionState.For(appState);
                voice.ProgressStage = progress.Stage;
                voice.ProgressFraction = progress.Fraction;
                appState.ActivityText = FormatVoiceProgress(progress);
            });
        });
    }

    private static string FormatVoiceProgress(VoiceProgress progress)
    {
        string label = progress.Stage switch
        {
            VoiceProgressStage.Downloading => "Downloading voice model",
            VoiceProgressStage.Recording => "Listening",
            VoiceProgressStage.Transcribing => "Transcribing voice",
            VoiceProgressStage.Updating => "Updating voice models",
            _ => "Preparing voice"
        };

        if (progress.Fraction is not double fraction)
        {
            return string.IsNullOrWhiteSpace(progress.Message) ? label : progress.Message.Trim();
        }

        int percentage = (int)Math.Round(Math.Clamp(fraction, 0d, 1d) * 100d);
        return $"{label} {percentage}%";
    }

    private static void QueueVoiceFailure(AppState state, Exception exception)
    {
        state.UiBridge.Enqueue(appState =>
        {
            FinishVoiceOperation(appState);
            appState.AddSystemMessage($"Voice error: {exception.Message}");
        });
    }

    private static void QueueVoiceCancelled(AppState state)
    {
        state.UiBridge.Enqueue(appState =>
        {
            FinishVoiceOperation(appState);
            appState.AddSystemMessage("Voice dictation cancelled.");
        });
    }

    internal static void StopVoiceOperation(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        VoiceInteractionState voice = VoiceInteractionState.For(state);
        voice.Cancellation?.Cancel();
    }

    private static void FinishVoiceOperation(AppState state, bool restoreActivity = true)
    {
        VoiceInteractionState voice = VoiceInteractionState.For(state);
        voice.IsBusy = false;
        voice.Operation = null;
        voice.ProgressStage = null;
        voice.ProgressFraction = null;
        voice.Cancellation?.Dispose();
        voice.Cancellation = null;
        if (restoreActivity)
        {
            state.ActivityText = state.IsReady ? "Ready" : "Idle";
        }
    }

    private static int FindIndex<T>(IReadOnlyList<T> values, Func<T, bool> predicate)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (predicate(values[index]))
            {
                return index;
            }
        }

        return -1;
    }
}
