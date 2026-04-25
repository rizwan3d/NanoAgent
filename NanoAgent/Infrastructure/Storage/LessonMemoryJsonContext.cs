using NanoAgent.Application.Models;
using System.Text.Json.Serialization;

namespace NanoAgent.Infrastructure.Storage;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false)]
[JsonSerializable(typeof(LessonMemoryEntry))]
internal sealed partial class LessonMemoryJsonContext : JsonSerializerContext
{
}
