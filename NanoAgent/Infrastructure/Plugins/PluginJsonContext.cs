using NanoAgent.Application.Models;
using System.Text.Json.Serialization;

namespace NanoAgent.Infrastructure.Plugins;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(PluginMarketplaceConfig))]
[JsonSerializable(typeof(PluginMarketplaceEntry))]
[JsonSerializable(typeof(PluginManifest))]
[JsonSerializable(typeof(PluginManifestFile))]
[JsonSerializable(typeof(InstalledPluginLock))]
[JsonSerializable(typeof(InstalledPluginEntry))]
internal sealed partial class PluginJsonContext : JsonSerializerContext
{
}
