using System.Text.Json.Serialization;
using NanoAgent.Application.Models;

namespace NanoAgent.Infrastructure.Hooks;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(LifecycleHookContext))]
internal sealed partial class LifecycleHookJsonContext : JsonSerializerContext
{
}
