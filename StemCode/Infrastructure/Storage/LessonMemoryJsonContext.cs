using StemCode.Application.Models;
using System.Text.Json.Serialization;

namespace StemCode.Infrastructure.Storage;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false)]
[JsonSerializable(typeof(LessonMemoryEntry))]
internal sealed partial class LessonMemoryJsonContext : JsonSerializerContext
{
}
