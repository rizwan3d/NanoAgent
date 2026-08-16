using System.Text.Json.Serialization;

namespace StemCode.Application.Voice;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(VoiceSettings))]
[JsonSerializable(typeof(VoiceModelOption))]
[JsonSerializable(typeof(VoiceInputDevice))]
[JsonSerializable(typeof(VoiceModelOption[]))]
[JsonSerializable(typeof(VoiceInputDevice[]))]
internal sealed partial class VoiceJsonContext : JsonSerializerContext
{
}
