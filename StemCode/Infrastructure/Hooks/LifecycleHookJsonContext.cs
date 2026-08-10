using StemCode.Application.Models;
using System.Text.Json.Serialization;

namespace StemCode.Infrastructure.Hooks;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(LifecycleHookContext))]
internal sealed partial class LifecycleHookJsonContext : JsonSerializerContext
{
}
