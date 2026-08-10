using StemCode.Application.Models;
using StemCode.Domain.Models;
using System.Text.Json.Serialization;

namespace StemCode.Infrastructure.Storage;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    WriteIndented = true)]
[JsonSerializable(typeof(AgentConfiguration))]
[JsonSerializable(typeof(AgentProviderProfile))]
[JsonSerializable(typeof(AgentProfileConfigurationDocument))]
[JsonSerializable(typeof(ApplicationProfileDocument))]
[JsonSerializable(typeof(TelemetryProfileDocument))]
[JsonSerializable(typeof(GitAutomationProfileDocument))]
[JsonSerializable(typeof(ProviderProfileConfigurationDocument))]
[JsonSerializable(typeof(BudgetControlsSettings))]
[JsonSerializable(typeof(MemoryProfileDocument))]
[JsonSerializable(typeof(CodebaseIndexProfileDocument))]
[JsonSerializable(typeof(ToolAuditProfileDocument))]
[JsonSerializable(typeof(CustomToolProfileDocument))]
[JsonSerializable(typeof(McpServerProfileDocument))]
[JsonSerializable(typeof(McpToolProfileDocument))]
[JsonSerializable(typeof(LanguageServerProfileDocument))]
[JsonSerializable(typeof(MemorySettings))]
[JsonSerializable(typeof(CodebaseIndexSettings))]
[JsonSerializable(typeof(ToolAuditSettings))]
internal sealed partial class OnboardingStorageJsonContext : JsonSerializerContext
{
}
