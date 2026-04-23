using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using System.Text.Json.Serialization;

namespace NanoAgent.Infrastructure.Storage;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = true)]
[JsonSerializable(typeof(ConversationSectionSnapshot))]
[JsonSerializable(typeof(ConversationSectionTurn))]
[JsonSerializable(typeof(PendingExecutionPlan))]
[JsonSerializable(typeof(SessionStateSnapshot))]
[JsonSerializable(typeof(SessionFileContext))]
[JsonSerializable(typeof(SessionEditContext))]
[JsonSerializable(typeof(SessionTerminalCommand))]
[JsonSerializable(typeof(AgentProviderProfile))]
internal sealed partial class ConversationSectionStorageJsonContext : JsonSerializerContext
{
}
