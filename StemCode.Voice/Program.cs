using PortAudioSharp;

namespace StemCode.Voice;

/// <summary>
/// Standalone "voice kit" runtime. StemCode shells out to this executable to
/// list models/devices and to record + transcribe speech using Whisper.
/// Protocol (one JSON document per stdout line unless noted):
///   models --json [--refresh]
///   devices --json
///   model ensure --id <id> --progress-json
///   model update --progress-json
///   dictate --model <id> [--device <n>] --progress-json
/// Models are downloaded automatically on first use.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            return await RunAsync(args, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (PlatformNotSupportedException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("A voice runtime command is required.");
            return 2;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "models":
                return RunModels();
            case "devices":
                return RunDevices();
            case "model":
                return await RunModelCommandAsync(args, cancellationToken);
            case "dictate":
                return await RunDictateAsync(args, cancellationToken);
            default:
                Console.Error.WriteLine($"Unknown voice runtime command: {args[0]}");
                return 2;
        }
    }

    private static int RunModels()
    {
        IReadOnlyList<ModelOption> options = VoiceModelCatalog.All
            .Select(spec => new ModelOption(spec.Id, spec.Label, spec.Description, spec.IsRecommended))
            .ToArray();

        VoiceProtocol.WriteModels(options);
        return 0;
    }

    private static int RunDevices()
    {
        IReadOnlyList<InputDevice> devices = ListInputDevices();
        VoiceProtocol.WriteDevices(devices);
        return 0;
    }

    private static IReadOnlyList<InputDevice> ListInputDevices()
    {
        PortAudio.Initialize();
        try
        {
            int deviceCount = PortAudio.DeviceCount;
            if (deviceCount <= 0)
            {
                return [new InputDevice(string.Empty, "System default", IsDefault: true)];
            }

            int defaultDevice = PortAudio.DefaultInputDevice;
            var devices = new List<InputDevice>(deviceCount);

            for (int index = 0; index < deviceCount; index++)
            {
                DeviceInfo info = PortAudio.GetDeviceInfo(index);
                if (info.maxInputChannels < 1)
                {
                    continue;
                }

                string name = string.IsNullOrWhiteSpace(info.name)
                    ? $"Input device {index}"
                    : info.name.Trim();
                devices.Add(new InputDevice(index.ToString(), name, IsDefault: index == defaultDevice));
            }

            if (devices.Count == 0)
            {
                return [new InputDevice(string.Empty, "System default", IsDefault: true)];
            }

            return devices;
        }
        finally
        {
            PortAudio.Terminate();
        }
    }

    private static async Task<int> RunModelCommandAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("A model subcommand (ensure|update) is required.");
            return 2;
        }

        string subcommand = args[1].ToLowerInvariant();
        if (subcommand == "ensure")
        {
            string id = GetOptionValue(args, "--id") ?? VoiceModelCatalog.Default.Id;
            VoiceModelCatalog.TryGet(id, out VoiceModelSpec spec);
            await WhisperTranscriber.EnsureModelAsync(spec, cancellationToken);
            return 0;
        }

        if (subcommand == "update")
        {
            await WhisperTranscriber.EnsureModelAsync(VoiceModelCatalog.Default, cancellationToken);
            return 0;
        }

        Console.Error.WriteLine($"Unknown model subcommand: {args[1]}");
        return 2;
    }

    private static async Task<int> RunDictateAsync(string[] args, CancellationToken cancellationToken)
    {
        string modelId = GetOptionValue(args, "--model") ?? VoiceModelCatalog.Default.Id;
        VoiceModelCatalog.TryGet(modelId, out VoiceModelSpec spec);

        int? deviceNumber = null;
        string? deviceArgument = GetOptionValue(args, "--device");
        if (!string.IsNullOrWhiteSpace(deviceArgument) && int.TryParse(deviceArgument, out int parsed))
        {
            deviceNumber = parsed;
        }

        await WhisperTranscriber.EnsureModelAsync(spec, cancellationToken);
        VoiceProtocol.WriteProgress(stage: "recording", message: "Listening");

        float[] samples = await MicrophoneCapture.CaptureAsync(deviceNumber, cancellationToken);

        VoiceProtocol.WriteProgress(stage: "transcribing", message: "Transcribing voice");
        string transcript = await WhisperTranscriber.TranscribeAsync(spec, samples, cancellationToken);
        VoiceProtocol.WriteProgress(stage: "completed", text: transcript);
        return 0;
    }

    private static string? GetOptionValue(string[] args, string option)
    {
        for (int index = 1; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], option, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}
