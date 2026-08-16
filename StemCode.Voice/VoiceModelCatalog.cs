using Whisper.net.Ggml;

namespace StemCode.Voice;

/// <summary>
/// Describes one logical voice model and the Whisper model that backs it.
/// </summary>
internal sealed record VoiceModelSpec(
    string Id,
    string Label,
    string Description,
    bool IsRecommended,
    GgmlType GgmlType,
    QuantizationType Quantization);

/// <summary>
/// Maps the user-facing model identifiers (<c>fast</c>, <c>balanced</c>,
/// <c>accurate</c>) to concrete Whisper models and knows where downloaded
/// model files live on disk.
/// </summary>
internal static class VoiceModelCatalog
{
    private static readonly VoiceModelSpec[] Specs =
    [
        new("fast", "Fast", "Smallest download with the lowest resource use.", false, GgmlType.TinyEn, QuantizationType.Q5_0),
        new("balanced", "Balanced", "Recommended balance of speed and accuracy.", true, GgmlType.SmallEn, QuantizationType.Q5_0),
        new("accurate", "Accurate", "Larger download with higher transcription accuracy.", false, GgmlType.MediumEn, QuantizationType.Q5_0),
    ];

    public static IReadOnlyList<VoiceModelSpec> All => Specs;

    public static VoiceModelSpec Default => Specs[1];

    public static bool TryGet(string id, out VoiceModelSpec spec)
    {
        foreach (VoiceModelSpec candidate in Specs)
        {
            if (string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                spec = candidate;
                return true;
            }
        }

        spec = Default;
        return false;
    }

    public static string ModelsDirectory
    {
        get
        {
            string baseDirectory = OperatingSystem.IsWindows()
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "StemCode", "Voice", "Models")
                : OperatingSystem.IsMacOS()
                    ? Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "Library", "Application Support", "StemCode", "Voice", "Models")
                    : Path.Combine(
                        Environment.GetEnvironmentVariable("XDG_DATA_HOME")
                            ?? Path.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                ".local", "share"),
                        "StemCode", "Voice", "Models");

            return baseDirectory;
        }
    }

    public static string ModelPath(string id)
    {
        string safeId = string.IsNullOrWhiteSpace(id) ? Default.Id : id;
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            safeId = safeId.Replace(invalid, '_');
        }

        return Path.Combine(ModelsDirectory, $"{safeId}.bin");
    }
}
