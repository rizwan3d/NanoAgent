using NanoAgent.Application.Models;
using System.Text.Json.Serialization;
using NanoAgent.Domain.Models;

namespace NanoAgent.Infrastructure.Storage;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    WriteIndented = true)]
[JsonSerializable(typeof(AgentConfiguration))]
[JsonSerializable(typeof(AgentProviderProfile))]
[JsonSerializable(typeof(AgentProfileConfigurationDocument))]
[JsonSerializable(typeof(MemoryProfileDocument))]
[JsonSerializable(typeof(ToolAuditProfileDocument))]
[JsonSerializable(typeof(CustomToolProfileDocument))]
[JsonSerializable(typeof(McpServerProfileDocument))]
[JsonSerializable(typeof(McpToolProfileDocument))]
[JsonSerializable(typeof(MemorySettings))]
[JsonSerializable(typeof(ToolAuditSettings))]
internal sealed partial class OnboardingStorageJsonContext : JsonSerializerContext
{
}
