using System.Text.Json;
using System.Text.Json.Serialization;

namespace StemCode.Voice;

/// <summary>
/// A voice model advertised to the host. Property names match the lowercase
/// JSON the StemCode voice client (VoiceJsonContext) expects.
/// </summary>
internal sealed record ModelOption(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("isRecommended")] bool IsRecommended);

/// <summary>
/// A capture device advertised to the host.
/// </summary>
internal sealed record InputDevice(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("isDefault")] bool IsDefault);

/// <summary>
/// A single progress/result line emitted on stdout. The host parses these as
/// JSON: <c>stage</c>/<c>fraction</c>/<c>message</c> drive progress UI and
/// <c>text</c> carries the final transcript.
/// </summary>
internal sealed record ProgressLine(
    [property: JsonPropertyName("stage")] string? Stage = null,
    [property: JsonPropertyName("fraction")] double? Fraction = null,
    [property: JsonPropertyName("message")] string? Message = null,
    [property: JsonPropertyName("text")] string? Text = null);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ModelOption[]))]
[JsonSerializable(typeof(InputDevice[]))]
[JsonSerializable(typeof(ProgressLine))]
internal sealed partial class VoiceRuntimeJsonContext : JsonSerializerContext;

/// <summary>
/// Helpers for emitting the voice protocol on stdout.
/// </summary>
internal static class VoiceProtocol
{
    public static void WriteModels(IReadOnlyList<ModelOption> models)
    {
        string json = JsonSerializer.Serialize(
            models.ToArray(),
            VoiceRuntimeJsonContext.Default.ModelOptionArray);
        Console.Out.WriteLine(json);
    }

    public static void WriteDevices(IReadOnlyList<InputDevice> devices)
    {
        string json = JsonSerializer.Serialize(
            devices.ToArray(),
            VoiceRuntimeJsonContext.Default.InputDeviceArray);
        Console.Out.WriteLine(json);
    }

    public static void WriteProgress(
        string? stage = null,
        double? fraction = null,
        string? message = null,
        string? text = null)
    {
        string json = JsonSerializer.Serialize(
            new ProgressLine(stage, fraction, message, text),
            VoiceRuntimeJsonContext.Default.ProgressLine);
        Console.Out.WriteLine(json);
    }
}
