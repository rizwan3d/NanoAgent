using System.Text.Json;
using StemCode.Application.Voice;

namespace StemCode.CLI;

internal static class VoiceConsoleRunner
{
    private const string ConfigureOption = "--voice-configure";
    private const string DevicesOption = "--voice-devices";
    private const string DictateOption = "--voice-dictate";
    private const string ModelsOption = "--voice-models";
    private const string UpdateOption = "--voice-update";

    public static bool IsInvocation(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return false;
        }

        return string.Equals(args[0], ConfigureOption, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(args[0], DevicesOption, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(args[0], DictateOption, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(args[0], ModelsOption, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(args[0], UpdateOption, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<int> RunAsync(string[] args)
    {
        IVoiceDictationService service = VoiceDictationService.CreateDefault();
        try
        {
            if (string.Equals(args[0], ModelsOption, StringComparison.OrdinalIgnoreCase))
            {
                IReadOnlyList<VoiceModelOption> models = await service.GetModelsAsync(
                    refresh: args.Any(arg => string.Equals(arg, "--refresh", StringComparison.OrdinalIgnoreCase)),
                    CancellationToken.None);
                Console.Out.Write(JsonSerializer.Serialize(models.ToArray(), VoiceJsonContext.Default.VoiceModelOptionArray));
                return 0;
            }

            if (string.Equals(args[0], DevicesOption, StringComparison.OrdinalIgnoreCase))
            {
                IReadOnlyList<VoiceInputDevice> devices = await service.GetInputDevicesAsync(CancellationToken.None);
                Console.Out.Write(JsonSerializer.Serialize(devices.ToArray(), VoiceJsonContext.Default.VoiceInputDeviceArray));
                return 0;
            }

            if (string.Equals(args[0], ConfigureOption, StringComparison.OrdinalIgnoreCase))
            {
                string? modelId = GetOptionValue(args, "--model");
                if (string.IsNullOrWhiteSpace(modelId))
                {
                    Console.Error.WriteLine("A voice model is required.");
                    return 2;
                }

                string? deviceId = GetOptionValue(args, "--device");
                VoiceSettings settings = new(
                    modelId.Trim(),
                    string.IsNullOrWhiteSpace(deviceId) ? null : deviceId.Trim());
                await service.SaveSettingsAsync(settings, CancellationToken.None);
                await service.EnsureModelAsync(settings.ModelId, CreateProgress(), CancellationToken.None);
                Console.Out.WriteLine("Voice setup saved.");
                return 0;
            }

            if (string.Equals(args[0], UpdateOption, StringComparison.OrdinalIgnoreCase))
            {
                await service.UpdateModelsAsync(CreateProgress(), CancellationToken.None);
                _ = await service.GetModelsAsync(refresh: true, CancellationToken.None);
                Console.Out.WriteLine("Voice models are up to date.");
                return 0;
            }

            VoiceSettings? configured = await service.LoadSettingsAsync(CancellationToken.None);
            if (configured is null || !configured.IsConfigured)
            {
                configured = VoiceSettings.Default;
                await service.SaveSettingsAsync(configured, CancellationToken.None);
            }

            await service.EnsureModelAsync(configured.ModelId, CreateProgress(), CancellationToken.None);
            string transcript = await service.DictateAsync(configured, CreateProgress(), CancellationToken.None);
            Console.Out.Write(transcript);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static string? GetOptionValue(IReadOnlyList<string> args, string option)
    {
        for (int index = 1; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], option, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static IProgress<VoiceProgress> CreateProgress()
    {
        return new Progress<VoiceProgress>(progress =>
        {
            string label = progress.Stage switch
            {
                VoiceProgressStage.Downloading => "Downloading voice model",
                VoiceProgressStage.Recording => "Listening",
                VoiceProgressStage.Transcribing => "Transcribing voice",
                VoiceProgressStage.Updating => "Updating voice models",
                _ => "Preparing voice"
            };

            if (progress.Fraction is double fraction)
            {
                int percentage = (int)Math.Round(Math.Clamp(fraction, 0d, 1d) * 100d);
                Console.Error.WriteLine($"{label} {percentage}%");
            }
            else
            {
                Console.Error.WriteLine(
                    string.IsNullOrWhiteSpace(progress.Message)
                        ? label
                        : progress.Message.Trim());
            }
        });
    }
}
