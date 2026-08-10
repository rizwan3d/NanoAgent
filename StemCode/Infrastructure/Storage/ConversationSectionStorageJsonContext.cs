using StemCode.Application.Models;
using StemCode.Domain.Models;
using System.Text.Json.Serialization;

namespace StemCode.Infrastructure.Storage;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = true)]
[JsonSerializable(typeof(ConversationSectionSnapshot))]
[JsonSerializable(typeof(ConversationSectionTurn))]
[JsonSerializable(typeof(ConversationAttachment))]
[JsonSerializable(typeof(ConversationFailureInfo))]
[JsonSerializable(typeof(ConversationToolCall))]
[JsonSerializable(typeof(PendingExecutionPlan))]
[JsonSerializable(typeof(SessionStateSnapshot))]
[JsonSerializable(typeof(SessionFileContext))]
[JsonSerializable(typeof(SessionEditContext))]
[JsonSerializable(typeof(SessionTerminalCommand))]
[JsonSerializable(typeof(AgentProviderProfile))]
[JsonSerializable(typeof(ModelContextMetadata))]
[JsonSerializable(typeof(ToolResultTruncationPolicy))]
internal sealed partial class ConversationSectionStorageJsonContext : JsonSerializerContext
{
}
