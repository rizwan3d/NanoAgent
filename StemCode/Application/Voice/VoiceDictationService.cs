using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace StemCode.Application.Voice;

public sealed class VoiceDictationService : IVoiceDictationService
{
    private static readonly VoiceModelOption[] BuiltInModels =
    [
        new("fast", "Fast", "Small download and lower resource use."),
        new("balanced", "Balanced", "Recommended balance of speed and accuracy.", IsRecommended: true),
        new("accurate", "Accurate", "Larger download with higher transcription accuracy.")
    ];

    private readonly string _settingsPath;
    private readonly string _runtimePath;

    public VoiceDictationService(string? settingsPath = null, string? runtimePath = null)
    {
        _settingsPath = settingsPath ?? GetDefaultSettingsPath();
        _runtimePath = runtimePath ?? ResolveRuntimePath();
    }

    public static VoiceDictationService CreateDefault() => new();

    public async Task<VoiceSettings?> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
        {
            return null;
        }

        await using FileStream stream = File.OpenRead(_settingsPath);
        return await JsonSerializer.DeserializeAsync(
            stream,
            VoiceJsonContext.Default.VoiceSettings,
            cancellationToken);
    }

    public async Task SaveSettingsAsync(VoiceSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = _settingsPath + ".tmp";
        await using (FileStream stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                settings,
                VoiceJsonContext.Default.VoiceSettings,
                cancellationToken);
        }

        File.Move(temporaryPath, _settingsPath, overwrite: true);
    }

    public async Task<IReadOnlyList<VoiceModelOption>> GetModelsAsync(
        bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            List<string> args = ["models", "--json"];
            if (refresh)
            {
                args.Add("--refresh");
            }

            string output = await RunForTextAsync(args, cancellationToken);
            VoiceModelOption[]? models = JsonSerializer.Deserialize(
                output,
                VoiceJsonContext.Default.VoiceModelOptionArray);
            if (models is { Length: > 0 })
            {
                return models;
            }
        }
        catch (Exception exception) when (IsRuntimeUnavailable(exception))
        {
        }

        return BuiltInModels;
    }

    public async Task<IReadOnlyList<VoiceInputDevice>> GetInputDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            string output = await RunForTextAsync(["devices", "--json"], cancellationToken);
            VoiceInputDevice[]? devices = JsonSerializer.Deserialize(
                output,
                VoiceJsonContext.Default.VoiceInputDeviceArray);
            if (devices is { Length: > 0 })
            {
                return devices;
            }
        }
        catch (Exception exception) when (IsRuntimeUnavailable(exception))
        {
        }

        return [new VoiceInputDevice(string.Empty, "System default", IsDefault: true)];
    }

    public async Task EnsureModelAsync(
        string modelId,
        IProgress<VoiceProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        progress?.Report(new VoiceProgress(VoiceProgressStage.Discovering, Message: "Checking voice model"));
        _ = await RunProgressCommandAsync(
            ["model", "ensure", "--id", modelId, "--progress-json"],
            progress,
            cancellationToken);
    }

    public async Task<string> DictateAsync(
        VoiceSettings settings,
        IProgress<VoiceProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.IsConfigured)
        {
            throw new InvalidOperationException("Voice setup has not been completed.");
        }

        List<string> args =
        [
            "dictate",
            "--model",
            settings.ModelId,
            "--progress-json"
        ];

        if (!string.IsNullOrWhiteSpace(settings.InputDeviceId))
        {
            args.Add("--device");
            args.Add(settings.InputDeviceId);
        }

        RuntimeCommandResult result = await RunProgressCommandAsync(args, progress, cancellationToken);
        if (string.IsNullOrWhiteSpace(result.Text))
        {
            throw new InvalidOperationException("No speech was detected.");
        }

        return result.Text.Trim();
    }

    public async Task UpdateModelsAsync(
        IProgress<VoiceProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _ = await RunProgressCommandAsync(
            ["model", "update", "--progress-json"],
            progress,
            cancellationToken);
    }

    private async Task<string> RunForTextAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using Process process = StartRuntime(arguments);
        Task<string> standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        string error = await standardError;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(GetRuntimeError(error));
        }

        return output.Trim();
    }

    private async Task<RuntimeCommandResult> RunProgressCommandAsync(
        IReadOnlyList<string> arguments,
        IProgress<VoiceProgress>? progress,
        CancellationToken cancellationToken)
    {
        using Process process = StartRuntime(arguments);
        Task<string> standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        string? text = null;

        try
        {
            while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (TryParseProgressLine(line, out VoiceProgress? parsedProgress, out string? parsedText))
                {
                    if (parsedProgress is not null)
                    {
                        progress?.Report(parsedProgress);
                    }

                    if (!string.IsNullOrWhiteSpace(parsedText))
                    {
                        text = parsedText;
                    }
                }
            }

            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TerminateVoiceRuntime(process);
            throw;
        }

        string error = await standardError;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(GetRuntimeError(error));
        }

        return new RuntimeCommandResult(text);
    }

    private static void TerminateVoiceRuntime(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch (Exception)
        {
            // Best-effort cleanup when a voice operation is cancelled.
        }
    }

    private Process StartRuntime(IReadOnlyList<string> arguments)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = _runtimePath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            return Process.Start(startInfo)
                ?? throw new InvalidOperationException("Unable to start the voice runtime.");
        }
        catch (Win32Exception exception)
        {
            throw new VoiceRuntimeUnavailableException(
                "Voice runtime is not available in this installation.",
                exception);
        }
        catch (FileNotFoundException exception)
        {
            throw new VoiceRuntimeUnavailableException(
                "Voice runtime is not available in this installation.",
                exception);
        }
    }

    private static bool TryParseProgressLine(
        string line,
        out VoiceProgress? progress,
        out string? text)
    {
        progress = null;
        text = null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("text", out JsonElement textElement) &&
                textElement.ValueKind == JsonValueKind.String)
            {
                text = textElement.GetString();
            }

            if (!root.TryGetProperty("stage", out JsonElement stageElement) ||
                stageElement.ValueKind != JsonValueKind.String)
            {
                return text is not null;
            }

            VoiceProgressStage stage = ParseStage(stageElement.GetString());
            double? fraction = null;
            if (root.TryGetProperty("fraction", out JsonElement fractionElement) &&
                fractionElement.ValueKind == JsonValueKind.Number &&
                fractionElement.TryGetDouble(out double parsedFraction))
            {
                fraction = Math.Clamp(parsedFraction, 0d, 1d);
            }

            string? message = root.TryGetProperty("message", out JsonElement messageElement) &&
                messageElement.ValueKind == JsonValueKind.String
                    ? messageElement.GetString()
                    : null;

            progress = new VoiceProgress(stage, fraction, message);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static VoiceProgressStage ParseStage(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "download" or "downloading" => VoiceProgressStage.Downloading,
            "record" or "recording" => VoiceProgressStage.Recording,
            "transcribe" or "transcribing" => VoiceProgressStage.Transcribing,
            "update" or "updating" => VoiceProgressStage.Updating,
            _ => VoiceProgressStage.Discovering
        };
    }

    private static bool IsRuntimeUnavailable(Exception exception)
    {
        return exception is VoiceRuntimeUnavailableException;
    }

    private static string GetRuntimeError(string standardError)
    {
        string message = standardError.Trim();
        return string.IsNullOrWhiteSpace(message)
            ? "Voice runtime command failed."
            : message;
    }

    private static string ResolveRuntimePath()
    {
        string? configured = Environment.GetEnvironmentVariable("STEMCODE_VOICE_RUNTIME");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        string fileName = OperatingSystem.IsWindows() ? "stemcode-voice.exe" : "stemcode-voice";
        string alongsideApplication = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(alongsideApplication))
        {
            return alongsideApplication;
        }

        string inVoiceFolder = Path.Combine(AppContext.BaseDirectory, "voice", fileName);
        if (File.Exists(inVoiceFolder))
        {
            return inVoiceFolder;
        }

        string inToolsFolder = Path.Combine(AppContext.BaseDirectory, "Tools", "voice", fileName);
        if (File.Exists(inToolsFolder))
        {
            return inToolsFolder;
        }

        return fileName;
    }

    private static string GetDefaultSettingsPath()
    {
        string baseDirectory;
        if (OperatingSystem.IsWindows())
        {
            baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }
        else if (OperatingSystem.IsMacOS())
        {
            baseDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library",
                "Application Support");
        }
        else
        {
            baseDirectory = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }

        return Path.Combine(baseDirectory, "StemCode", "voice.json");
    }

    private sealed record RuntimeCommandResult(string? Text);

    private sealed class VoiceRuntimeUnavailableException : InvalidOperationException
    {
        public VoiceRuntimeUnavailableException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
